using System;
using System.Collections.Generic;

public enum TrackStatus { Tentative, Confirmed }

public struct BearingSample
{
    public double time;    // simulated seconds
    public float  bearing; // world bearing, degrees
    public float  snr;
    public float  sigmaDeg;  // bearing accuracy of this sample
    public double shipX, shipZ; // ship position when measured (game units, system frame)

    /// <summary>Ship heading (world yaw) at the moment this hit was recorded. No longer needed to place the
    /// pixel-chain dot (the waterfall is world-bearing-centered now, so bearing alone is enough) — kept as
    /// plain metadata in case something wants "which way was the ship pointed when this was seen" later.</summary>
    public float headingDeg;
}

/// <summary>
/// A bearing track held in WORLD azimuth. Built only from detections, never from true positions.
/// The history is what later feeds range-from-maneuver (target motion analysis).
/// </summary>
public sealed class Track
{
    public int id;
    public string name;
    public TrackStatus status;

    public float  bearing;          // last measured (or coasted) world bearing, degrees
    public float  rateDegPerDay;    // fitted bearing rate, valid when hasRate
    public bool   hasRate;
    public double lastTime;         // time of the last hit
    public float  lastSnr;

    public float quality;           // 0..1, rises with hits, falls with misses
    public int hits, misses, consecutiveMisses, updates;

    public readonly List<BearingSample> history = new List<BearingSample>();

    /// <summary>Range from bearing-only motion analysis. Refined on every hit; only meaningful after a manoeuvre.</summary>
    public RangeEstimate range;
}

/// <summary>
/// Associates detections with tracks, once per waterfall line. Nearest-first assignment inside a gate around the
/// predicted bearing. Tracks are never created automatically anymore — every one starts from the player marking
/// a bearing (MarkBearing), so faint intermittent contacts or plain noise spikes can no longer spam the track
/// list on their own. A marked track starts "searching" (Tentative) and locks ("Confirmed") once it gathers
/// ConfirmHits real detections; losing lock later drops it back to searching rather than removing it. Crossing
/// or merging contacts are NOT resolved for the player: an unresolved pair coasts and identities may swap.
/// Hardware caps the total number of tracks (see MarkBearing's maxTracks) and how many can be locked at once.
/// Pure C#, no Unity dependency.
/// </summary>
public sealed class TrackManager
{
    private const double SecondsPerDay = 86400.0;
    public const int ConfirmHits = 3;      // hits needed (ever, not in a rolling window) to lock a searching track
    public const int RateFitSamples = 8;
    public const int HistoryLimit = 512;

    private readonly List<Track> _tracks = new List<Track>();
    private int _nextId = 1;
    private int _nextName = 1;

    private struct Pair { public int t, d; public float dist; }
    private readonly List<Pair> _pairs = new List<Pair>();

    public IList<Track> All { get { return _tracks; } }
    public int SelectedId;

    /// <summary>Name the next MarkBearing() call should use, edited from the Track panel. Consumed (and reset
    /// to "") the moment it's used; empty falls back to auto-numbering ("T1", "T2", ...).</summary>
    public string PendingName = "";

    /// <summary>Increments whenever the tracks are wiped (new system, new run). Track ids restart after that.</summary>
    public int Generation { get; private set; }
    public event Action Changed;

    /// <summary>Raised when a track becomes confirmed / when a confirmed track is dropped for too many misses.
    /// Not raised for player drops or a clear.</summary>
    public event Action<Track> TrackConfirmed;
    public event Action<Track> TrackLost;

    public void Clear()
    {
        _tracks.Clear();
        Generation++;
        SelectedId = 0;
        _nextId = 1;
        _nextName = 1;
        if (Changed != null) Changed();
    }

    public Track Find(int id)
    {
        for (int i = 0; i < _tracks.Count; i++) if (_tracks[i].id == id) return _tracks[i];
        return null;
    }

    public void CollectConfirmed(List<Track> result)
    {
        result.Clear();
        for (int i = 0; i < _tracks.Count; i++)
            if (_tracks[i].status == TrackStatus.Confirmed) result.Add(_tracks[i]);
    }

    public void Drop(int id)
    {
        for (int i = 0; i < _tracks.Count; i++)
            if (_tracks[i].id == id) { Remove(i); break; }
        if (Changed != null) Changed();
    }

    public void Rename(int id, string name)
    {
        for (int i = 0; i < _tracks.Count; i++)
            if (_tracks[i].id == id) { _tracks[i].name = name; break; }
        if (Changed != null) Changed();
    }

    /// <summary>
    /// Seeds a new track at a bearing the player marked (click on the waterfall/DSP). It starts Tentative
    /// ("searching", drawn red) with no hits; ordinary association in Update() will try to lock a real
    /// detection onto it. Unlike the old auto-created tentative tracks, this one is never dropped for failing
    /// to lock — it just stays red until the player either gets a lock or drops it by hand. Returns null if
    /// the hardware's track-count cap (maxTracks) is already full.
    /// </summary>
    public Track MarkBearing(double time, float worldBearingDeg, int maxTracks)
    {
        if (_tracks.Count >= maxTracks) return null;

        var tr = new Track();
        tr.id = _nextId++;
        tr.status = TrackStatus.Tentative;
        tr.quality = 0.1f;
        tr.name = ResolvePendingName();
        tr.bearing = BearingMath.Wrap360(worldBearingDeg);
        tr.lastTime = time;
        _tracks.Add(tr);
        if (Changed != null) Changed();
        return tr;
    }

    private string ResolvePendingName()
    {
        string n = PendingName != null ? PendingName.Trim() : "";
        PendingName = "";
        if (n.Length == 0) n = "T" + (_nextName++);
        return n;
    }

    // ---------------------------------------------------------------------
    public void Update(double time, List<Detection> detections, float gateDeg, int maxConfirmed, int dropAfterMisses,
                       double shipX, double shipZ, float headingDeg)
    {
        int nt = _tracks.Count, nd = detections.Count;

        // Candidate pairs inside the gate
        _pairs.Clear();
        for (int ti = 0; ti < nt; ti++)
        {
            float pred = Predict(_tracks[ti], time);
            for (int di = 0; di < nd; di++)
            {
                float dist = Math.Abs(BearingMath.Diff(detections[di].bearing, pred));
                if (dist <= gateDeg)
                {
                    var p = new Pair(); p.t = ti; p.d = di; p.dist = dist;
                    _pairs.Add(p);
                }
            }
        }
        _pairs.Sort(delegate (Pair a, Pair b) { return a.dist.CompareTo(b.dist); });

        bool[] trackHit = new bool[nt];
        bool[] detUsed  = new bool[nd];
        for (int k = 0; k < _pairs.Count; k++)
        {
            Pair p = _pairs[k];
            if (trackHit[p.t] || detUsed[p.d]) continue;
            trackHit[p.t] = true;
            detUsed[p.d] = true;
            ApplyHit(_tracks[p.t], detections[p.d], time, shipX, shipZ, headingDeg);
        }
        for (int ti = 0; ti < nt; ti++)
            if (!trackHit[ti]) ApplyMiss(_tracks[ti]);

        // Status changes only — nothing here is ever auto-removed anymore. Every track exists because the
        // player marked it; only Drop() (the player's own button) takes one away.
        int confirmed = 0;
        for (int i = 0; i < _tracks.Count; i++)
            if (_tracks[i].status == TrackStatus.Confirmed) confirmed++;

        for (int i = 0; i < _tracks.Count; i++)
        {
            Track tr = _tracks[i];
            if (tr.status == TrackStatus.Tentative)
            {
                if (tr.hits >= ConfirmHits && confirmed < maxConfirmed)
                {
                    tr.status = TrackStatus.Confirmed;
                    confirmed++;
                    if (TrackConfirmed != null) TrackConfirmed(tr);
                }
                // Still searching: no timeout, no removal. It just stays red.
            }
            else if (tr.consecutiveMisses > dropAfterMisses)
            {
                // Lost lock: back to searching (red) instead of disappearing.
                tr.status = TrackStatus.Tentative;
                tr.hits = 0;
                if (TrackLost != null) TrackLost(tr);
            }
        }

        if (Changed != null) Changed();
    }

    // ---------------------------------------------------------------------
    private void Remove(int index)
    {
        if (_tracks[index].id == SelectedId) SelectedId = 0;
        _tracks.RemoveAt(index);
    }

    private static float Predict(Track tr, double time)
    {
        if (!tr.hasRate) return tr.bearing;
        double days = (time - tr.lastTime) / SecondsPerDay;
        return BearingMath.Wrap360(tr.bearing + (float)(tr.rateDegPerDay * days));
    }

    private static void AddSample(Track tr, Detection d, double shipX, double shipZ, float headingDeg)
    {
        var s = new BearingSample();
        s.time = d.time; s.bearing = d.bearing; s.snr = d.snr;
        s.sigmaDeg = d.sigmaDeg; s.shipX = shipX; s.shipZ = shipZ; s.headingDeg = headingDeg;
        tr.history.Add(s);
        if (tr.history.Count > HistoryLimit) tr.history.RemoveAt(0);
    }

    private static void ApplyHit(Track tr, Detection d, double time, double shipX, double shipZ, float headingDeg)
    {
        tr.bearing = d.bearing;
        tr.lastTime = time;
        tr.lastSnr = d.snr;
        tr.hits++;
        tr.updates++;
        tr.consecutiveMisses = 0;
        tr.quality += 0.2f * (1f - tr.quality);
        AddSample(tr, d, shipX, shipZ, headingDeg);
        FitRate(tr);
        if (tr.status == TrackStatus.Confirmed || tr.hits >= ConfirmHits)
            tr.range = RangeEstimator.Estimate(tr.history);
    }

    private static void ApplyMiss(Track tr)
    {
        tr.misses++;
        tr.updates++;
        tr.consecutiveMisses++;
        tr.quality = Math.Max(0f, tr.quality - 0.15f);
    }

    /// <summary>Least-squares bearing rate (deg per simulated day) over the last few hits.</summary>
    private static void FitRate(Track tr)
    {
        int count = Math.Min(RateFitSamples, tr.history.Count);
        if (count < 3) return;

        int start = tr.history.Count - count;
        double t0 = tr.history[start].time;
        float b0 = tr.history[start].bearing;

        double mx = 0, my = 0;
        double[] x = new double[count], y = new double[count];
        for (int i = 0; i < count; i++)
        {
            BearingSample s = tr.history[start + i];
            x[i] = (s.time - t0) / SecondsPerDay;
            y[i] = BearingMath.Diff(s.bearing, b0);
            mx += x[i]; my += y[i];
        }
        mx /= count; my /= count;

        double sxx = 0, sxy = 0;
        for (int i = 0; i < count; i++)
        {
            sxx += (x[i] - mx) * (x[i] - mx);
            sxy += (x[i] - mx) * (y[i] - my);
        }
        if (sxx < 1e-9) return;

        tr.rateDegPerDay = (float)(sxy / sxx);
        tr.hasRate = true;
    }
}
