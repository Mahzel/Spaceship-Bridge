using System;
using System.Collections.Generic;

public enum DataKind { Stub, Raw }

/// <summary>
/// One recorded item on board. A stub is a small claim about a target (where, and how well the range is known).
/// A raw recording accumulates while a track is held: it is big, and worth more the longer and cleaner it is.
/// </summary>
public sealed class DataRecord
{
    public int id;
    public DataKind kind;
    public string label;            // track name when recorded
    public string systemId;
    public int trackId;
    public int generation;          // TrackManager.Generation when recorded; ids restart after a system change
    public double startTime, endTime;
    public float size;
    public float value;             // information value, used later by the atlas and trust
    public bool recording;          // raw only: still growing
    public bool compressed;
    public float bearing;           // world bearing claimed
    public RangeEstimate range;     // range claim at the time (estimate + sigma, never the truth)
    public string stopReason = "";  // "", "lost", "full", "player"

    /// <summary>Worst (lowest) Track.quality observed while this record was captured — running minimum from
    /// creation through every refresh/growth tick. Used by Atlas to weight the odds a delivered record turns
    /// out to be a false entry: data logged off a rock-solid confirmed track should almost never be wrong,
    /// data logged off a 10%-quality coasting track often is.</summary>
    public float sourceQuality = 1f;
}

/// <summary>
/// The probe's storage. Recording is deliberate: the player logs stubs and starts or stops raw recordings.
/// A raw recording stops by itself when its track is lost or the storage is full.
/// Plain C#: no scene dependencies.
/// </summary>
public sealed class DataStore
{
    // Tunables (set from ProbeSpec by GameState)
    public float Capacity = 100f;
    public float stubSize = 1f;
    public float rawSizePerDay = 0.25f;
    public float rawValuePerDay = 0.1f;
    public float compressedSizeFactor = 0.4f;
    public float compressedValueFactor = 0.6f;

    /// <summary>New raw recordings use lossy compression: smaller, but worth less.</summary>
    public bool compressNewRaw;

    private readonly List<DataRecord> _records = new List<DataRecord>();
    private int _nextId = 1;

    public IList<DataRecord> Records { get { return _records; } }
    public event Action Changed;

    public float Used
    {
        get { float u = 0f; for (int i = 0; i < _records.Count; i++) u += _records[i].size; return u; }
    }

    public float TotalValue
    {
        get { float v = 0f; for (int i = 0; i < _records.Count; i++) v += _records[i].value; return v; }
    }

    public float Free { get { return Math.Max(0f, Capacity - Used); } }

    public void Clear()
    {
        _records.Clear();
        _nextId = 1;
        Raise();
    }

    public DataRecord Find(int id)
    {
        for (int i = 0; i < _records.Count; i++) if (_records[i].id == id) return _records[i];
        return null;
    }

    /// <summary>The raw recording currently running on this track, or null.</summary>
    public DataRecord ActiveRawFor(int trackId, int generation)
    {
        for (int i = 0; i < _records.Count; i++)
        {
            DataRecord r = _records[i];
            if (r.kind == DataKind.Raw && r.recording && r.trackId == trackId && r.generation == generation) return r;
        }
        return null;
    }

    /// <summary>
    /// Logs (or refreshes) the stub for a track: current bearing and range claim. Returns false if it does not fit.
    /// Refreshing a stub costs no extra space.
    /// </summary>
    public bool LogStub(Track t, string systemId, int generation, double time)
    {
        DataRecord stub = null;
        for (int i = 0; i < _records.Count; i++)
        {
            DataRecord r = _records[i];
            if (r.kind == DataKind.Stub && r.trackId == t.id && r.generation == generation) { stub = r; break; }
        }

        if (stub == null)
        {
            if (Free + 1e-4f < stubSize) return false;
            stub = new DataRecord();
            stub.id = _nextId++;
            stub.kind = DataKind.Stub;
            stub.trackId = t.id;
            stub.generation = generation;
            stub.startTime = time;
            stub.size = stubSize;
            _records.Add(stub);
        }

        stub.label = t.name;
        stub.systemId = systemId;
        stub.endTime = time;
        stub.bearing = t.bearing;
        stub.range = t.range;
        stub.sourceQuality = stub.startTime == stub.endTime ? t.quality : Math.Min(stub.sourceQuality, t.quality);
        // Worth more when the range is actually constrained
        float bonus = 0f;
        if (t.range.Observable) bonus = 2f * Math.Max(0f, 1f - (float)(t.range.rangeSigma / t.range.range));
        stub.value = 1f + bonus;
        Raise();
        return true;
    }

    public bool StartRaw(Track t, string systemId, int generation, double time)
    {
        if (ActiveRawFor(t.id, generation) != null) return true;
        if (Free <= 0.01f) return false;

        var r = new DataRecord();
        r.id = _nextId++;
        r.kind = DataKind.Raw;
        r.label = t.name;
        r.systemId = systemId;
        r.trackId = t.id;
        r.generation = generation;
        r.startTime = r.endTime = time;
        r.recording = true;
        r.compressed = compressNewRaw;
        r.bearing = t.bearing;
        r.range = t.range;
        r.sourceQuality = t.quality;
        _records.Add(r);
        Raise();
        return true;
    }

    public void StopRaw(int recordId, string reason)
    {
        DataRecord r = Find(recordId);
        if (r == null || !r.recording) return;
        r.recording = false;
        r.stopReason = reason;
        Raise();
    }

    /// <summary>Dumps a record to free its space. Gone for good.</summary>
    public void Remove(int recordId)
    {
        for (int i = 0; i < _records.Count; i++)
            if (_records[i].id == recordId) { _records.RemoveAt(i); break; }
        Raise();
    }

    /// <summary>Grows the running raw recordings. Call once per frame with the simulated days elapsed.</summary>
    public void Tick(double days, double simTime, TrackManager tracks)
    {
        if (days <= 0.0) return;

        bool changed = false;
        for (int i = 0; i < _records.Count; i++)
        {
            DataRecord r = _records[i];
            if (!r.recording) continue;

            Track tr = tracks.Find(r.trackId);
            if (r.generation != tracks.Generation || tr == null || tr.status != TrackStatus.Confirmed)
            {
                r.recording = false; r.stopReason = "lost"; changed = true;
                continue;
            }

            float sizeFactor  = r.compressed ? compressedSizeFactor  : 1f;
            float valueFactor = r.compressed ? compressedValueFactor : 1f;
            float grow = rawSizePerDay * sizeFactor * (float)days;

            float room = Capacity - Used;
            bool full = grow >= room;
            if (full) grow = Math.Max(0f, room);

            float fraction = rawSizePerDay * sizeFactor * (float)days > 0f ? grow / (rawSizePerDay * sizeFactor * (float)days) : 0f;
            r.size += grow;
            r.value += rawValuePerDay * valueFactor * (float)days * fraction * (0.5f + 0.5f * tr.quality);
            r.endTime = simTime;
            r.sourceQuality = Math.Min(r.sourceQuality, tr.quality);
            changed = true;

            if (full) { r.recording = false; r.stopReason = "full"; }
        }
        if (changed) Raise();
    }

    private void Raise() { if (Changed != null) Changed(); }
}
