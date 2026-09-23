using System;
using System.Collections.Generic;

/// <summary>One packet burst sent home. Frozen snapshot of the record at send time.</summary>
public sealed class Transmission
{
    public int id;
    public int recordId;
    public string label, systemId;
    public DataKind kind;
    public float size, value;       // snapshot: later growth of the record is not part of it
    public int packets;
    public int attempt;             // n-th send of this record (fresh loss rolls for a re-send)
    public double distanceAu;
    public float powerLevel;
    public bool robust;
    public float marginDb;
    public float energy;
    public double sentTime;
    public bool lastGasp;
}

public struct LinkQuote
{
    public double distanceAu;
    public float marginDb;          // >0 = link closes comfortably
    public float packetSuccess;     // expected fraction of packets that arrive
    public float energyPerUnit;     // power spent per unit of data sent
}

public sealed class TransmitResult
{
    public int transmissions;
    public int packetsSent, packetsOk;
    public float valueSent, valueReceived;

    /// <summary>Ids of records that got at least one packet through and were not still on board (so their
    /// value was actually counted into valueReceived). Atlas entries are filed from this list — a record that
    /// never got a packet home, or came home anyway with the probe, is not a separate "transmitted" discovery.</summary>
    public readonly List<int> recordsReceived = new List<int>();
}

/// <summary>One line of the engineering log, kept across runs: what the link actually did.</summary>
public sealed class LinkLogEntry
{
    public int run;
    public double distanceAu;
    public float marginDb;
    public bool robust;
    public int packets, ok;
}

/// <summary>
/// The transmit-home link. Range costs power (required power grows with distance squared), lower margin means
/// more lost packets. The probe never learns what arrived: the acknowledgement would take as long as the trip,
/// so results appear only in the debrief (Resolve). Loss rolls come from a hash of (world seed, run, record,
/// packet, attempt), so reloading or re-checking can never re-roll them. Sending does not free storage.
/// Plain C#, no scene dependencies.
/// </summary>
public sealed class Transmitter
{
    public const double AuPerLy = 63241.077;
    public const float KneeDb = 1.0f;       // margin at which half the packets get through
    public const float SoftnessDb = 1.5f;

    private readonly GameState _state;
    private readonly List<Transmission> _sent = new List<Transmission>();
    private readonly List<LinkLogEntry> _log = new List<LinkLogEntry>();
    private int _nextId = 1;
    private bool _logged;

    /// <summary>Distance to home in AU. Set by the game bootstrap.</summary>
    public Func<double> DistanceAu = () => 0.0;
    public Func<double> Now = () => 0.0;
    public Func<int> RunNumber = () => 1;

    public float PowerLevel = 0.5f;         // 0.1 .. 1 of the transmitter's peak power
    public bool Robust;                     // robust coding: half the required power, twice the time (energy)

    public IList<Transmission> Sent { get { return _sent; } }
    public IList<LinkLogEntry> Log { get { return _log; } }
    public event Action Changed;

    public Transmitter(GameState state) { _state = state; }

    public void NewRun()
    {
        _sent.Clear();
        _nextId = 1;
        _logged = false;
        if (Changed != null) Changed();
    }

    // --- Link model ------------------------------------------------------------
    public LinkQuote Quote(double distanceAu, float level, bool robust)
    {
        ProbeSpec p = _state.Probe;
        double rangeAu = p.txRangeLy * AuPerLy;
        double d = Math.Max(distanceAu, 1.0);
        float txPower = p.txPeakPower * Math.Max(0.01f, level);
        double required = p.txPeakPower * (d / rangeAu) * (d / rangeAu) * (robust ? 0.5 : 1.0);

        var q = new LinkQuote();
        q.distanceAu = distanceAu;
        q.marginDb = (float)(10.0 * Math.Log10(txPower / required));
        q.packetSuccess = SuccessAt(q.marginDb);
        q.energyPerUnit = txPower / p.txRate * (robust ? 2f : 1f);
        return q;
    }

    public static float SuccessAt(float marginDb)
    {
        return (float)(1.0 / (1.0 + Math.Exp(-(marginDb - KneeDb) / SoftnessDb)));
    }

    public int SentCount(int recordId)
    {
        int n = 0;
        for (int i = 0; i < _sent.Count; i++) if (_sent[i].recordId == recordId) n++;
        return n;
    }

    public float EnergyFor(DataRecord r, float level, bool robust)
    {
        return r.size * Quote(DistanceAu(), level, robust).energyPerUnit;
    }

    /// <summary>Sends a copy of the record home. Storage is not freed. Returns false with a reason key if refused.</summary>
    public bool Send(DataRecord r, out string whyNot)
    {
        whyNot = "";
        if (r == null || _state.IsProbeDead) { whyNot = "tx.no"; return false; }
        float energy = EnergyFor(r, PowerLevel, Robust);
        if (energy >= _state.PowerStored) { whyNot = "tx.power"; return false; }
        _state.ChangePower(-energy);
        Record(r, PowerLevel, Robust, energy, false);
        return true;
    }

    private void Record(DataRecord r, float level, bool robust, float energy, bool gasp)
    {
        LinkQuote q = Quote(DistanceAu(), level, robust);
        var t = new Transmission();
        t.id = _nextId++;
        t.recordId = r.id;
        t.label = r.label; t.systemId = r.systemId; t.kind = r.kind;
        t.size = r.size; t.value = r.value;
        t.packets = Math.Max(1, (int)Math.Ceiling(r.size - 1e-4f));
        t.attempt = SentCount(r.id);
        t.distanceAu = DistanceAu();
        t.powerLevel = level; t.robust = robust;
        t.marginDb = q.marginDb;
        t.energy = energy;
        t.sentTime = Now();
        t.lastGasp = gasp;
        _sent.Add(t);
        if (Changed != null) Changed();
    }

    /// <summary>
    /// The dying probe's last transmission: with the reserve energy it has left, it sends the most valuable
    /// data (per unit of size) it can afford, at full power and robust coding. Returns how many records it sent.
    /// </summary>
    public int LastGasp(float reserveEnergy)
    {
        var order = new List<DataRecord>(_state.Data.Records);
        order.Sort(delegate (DataRecord a, DataRecord b)
        {
            float da = a.value / Math.Max(0.01f, a.size), db = b.value / Math.Max(0.01f, b.size);
            return db.CompareTo(da);
        });

        int count = 0;
        float left = reserveEnergy;
        for (int i = 0; i < order.Count; i++)
        {
            float e = EnergyFor(order[i], 1f, true);
            if (e > left) continue;
            left -= e;
            Record(order[i], 1f, true, e, true);
            count++;
        }
        return count;
    }

    // --- Results (debrief only) ------------------------------------------------
    /// <summary>
    /// Rolls every packet sent this run. Union over re-sends: a packet counts once, if any attempt got it through.
    /// Value received per record = its value * (packets received / packets in the largest snapshot).
    /// Appends to the engineering log.
    /// </summary>
    public TransmitResult Resolve(int worldSeed, int run, ICollection<int> stillOnBoard)
    {
        var res = new TransmitResult();
        var okSet = new Dictionary<int, HashSet<int>>();
        var total = new Dictionary<int, int>();
        var value = new Dictionary<int, float>();

        for (int i = 0; i < _sent.Count; i++)
        {
            Transmission t = _sent[i];
            float p = SuccessAt(t.marginDb);
            int ok = 0;
            HashSet<int> set;
            if (!okSet.TryGetValue(t.recordId, out set)) { set = new HashSet<int>(); okSet[t.recordId] = set; }

            for (int k = 0; k < t.packets; k++)
            {
                if (Roll(worldSeed, run, t.recordId, k, t.attempt) < p) { ok++; set.Add(k); }
            }

            int tot; total.TryGetValue(t.recordId, out tot); total[t.recordId] = Math.Max(tot, t.packets);
            float v; value.TryGetValue(t.recordId, out v); value[t.recordId] = Math.Max(v, t.value);

            res.transmissions++;
            res.packetsSent += t.packets;
            res.packetsOk += ok;
            res.valueSent += t.value;

            var e = new LinkLogEntry();
            e.run = run; e.distanceAu = t.distanceAu; e.marginDb = t.marginDb; e.robust = t.robust;
            e.packets = t.packets; e.ok = ok;
            if (!_logged) _log.Add(e);
        }

        foreach (var kv in okSet)
        {
            if (stillOnBoard != null && stillOnBoard.Contains(kv.Key)) continue; // a probe that comes home returns it anyway
            if (kv.Value.Count <= 0) continue;
            res.valueReceived += value[kv.Key] * kv.Value.Count / Math.Max(1, total[kv.Key]);
            res.recordsReceived.Add(kv.Key);
        }
        _logged = true;
        return res;
    }

    /// <summary>Uniform [0,1) from a hash of the inputs (splitmix64). Deterministic.</summary>
    public static double Roll(int seed, int run, int record, int packet, int attempt)
    {
        unchecked
        {
            ulong x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL;
            x ^= (ulong)(uint)run * 0xBF58476D1CE4E5B9UL;
            x ^= (ulong)(uint)record * 0x94D049BB133111EBUL;
            x ^= (ulong)(uint)packet * 0xD6E8FEB86659FD93UL;
            x ^= (ulong)(uint)attempt * 0xCA5A826395121157UL;
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (x >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}
