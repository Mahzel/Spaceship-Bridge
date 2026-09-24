using System;
using System.Collections.Generic;

public enum TrackStatus { Tentative, Confirmed }

[System.Serializable]
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

    public float elevationDeg, elevationSigmaDeg; // see Detection; sigma 0 = none
}

/// <summary>Where a track's elevation estimate came from, coarsest to finest in the usual case.</summary>
public enum ElevationSource { None, Waterfall, Radar, Imager, Atlas }

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
    public float  rateSigmaDegPerDay = float.PositiveInfinity; // 1-sigma of that fit (+inf when there is no fit)
    public bool   hasRate;          // the rate is measured, not noise: see TrackManager.FitRate
    public double lastTime;         // time of the last hit
    public float  lastSnr;
    public bool   lostLock;         // had a lock and lost it: frozen at its last bearing until strongly re-acquired
    public float  snrAvg;           // running SNR of the waterfall hits (0 = none yet): see the extended gate

    public float quality;           // 0..1, rises with hits, falls with misses
    public int hits, misses, consecutiveMisses, updates;

    public readonly List<BearingSample> history = new List<BearingSample>();

    /// <summary>BEST current range: whichever of the TMA solution and the aged radar fix has the smaller sigma.
    /// Recomputed by TrackManager on every waterfall line and whenever a radar fix lands. Everything
    /// downstream (TrackPanel, DataStore, System view) reads this.</summary>
    public RangeEstimate range;

    /// <summary>Range from bearing-only motion analysis. Refined on every hit; only meaningful after a manoeuvre.</summary>
    public RangeEstimate tmaRange;

    /// <summary>Last radar range fix (sweep return or track ping), as measured at radarFixTime. It ages: see
    /// TrackManager.AgedRadarFix. valid = false until the radar has ranged this track at least once.</summary>
    public RangeEstimate radarFix;
    public double radarFixTime;
    public double radarRangeRateKmS; // +opening / -closing, valid when hasRadarRate (track-mode pings only)
    public bool   hasRadarRate;

    /// <summary>Elevation estimate (degrees, +up), measured at elevationFixTime with 1-sigma elevationSigmaDeg.
    /// It ages: see TrackManager.AgedElevationSigma. The bearing comes from the waterfall; elevation has to be
    /// measured separately (waterfall fan: coarse, radar: fan or pencil, imager FIX: fine).</summary>
    public bool   hasElevation;
    public float  elevationDeg;
    public float  elevationSigmaDeg;
    public double elevationFixTime;
    public ElevationSource elevationSource;

    /// <summary>Apparent (angular) radius of the contact when the imager resolved it as a disk, degrees; 0 for a
    /// point source. Lets the spectrometer point anywhere on a big nearby body, not only at its exact center.</summary>
    public float angularRadiusDeg;

    /// <summary>What the passive sensors have worked out about this contact beyond its position.</summary>
    public readonly TrackInfo info = new TrackInfo();

    /// <summary>Until this sim time, waterfall misses can't drop the lock: another sensor (imager FIX, radar echo)
    /// has recently confirmed the contact is there. See TrackManager.ApplySupport.</summary>
    public double supportHoldUntil;
    public ElevationSource lastSupport; // last non-waterfall sensor that confirmed it (None = waterfall only)

    public bool Locked { get { return status == TrackStatus.Confirmed; } }
}

/// <summary>How far a contact has been characterised, lowest to highest. Each level implies the ones below.</summary>
public enum TrackLevel { Bearing, Rate, Ranged, Identified }

/// <summary>
/// Player knowledge about a contact, written ONLY by sensors that measured it (right now the spectrometer,
/// once its dwell completes). Nothing here is copied from the body up front. A fresh track knows nothing,
/// and if contacts swap identity in a merge, this can be wrong, the same way a real track file can.
/// </summary>
public sealed class TrackInfo
{
    /// <summary>Real seconds of spectrometer integration on this track. Kept on the track rather than in the
    /// screen, so a partial dwell survives looking away and coming back.</summary>
    public float specDwellSeconds;

    /// <summary>Opaque key for "the spectral signature being integrated". If the signature changes mid-dwell
    /// (a different body now dominates the slit), the dwell restarts.</summary>
    public string signatureKey;

    public bool   identified;
    public bool   blended;          // more than one source was in the slit when it resolved
    public bool   isStar;
    public string catalogName;      // spectral catalog match; used for manoeuvre targeting, not shown as the track's name
    public string bodyType;
    public string surfaceClass;
    public float  temperatureK;      // equilibrium (planets) / effective (stars)
    public float  surfaceTemperatureK;
    public float  metallicity, ageGyr; // stars only
    public readonly List<ChemicalComposition> composition = new List<ChemicalComposition>();
    public Atmosphere atmosphere;

    public void Reset()
    {
        specDwellSeconds = 0f;
        signatureKey = null;
        identified = false;
        blended = false;
        isStar = false;
        catalogName = bodyType = surfaceClass = null;
        temperatureK = surfaceTemperatureK = 0f;
        metallicity = ageGyr = 0f;
        composition.Clear();
        atmosphere = null;
    }
}

/// <summary>
/// Associates detections with tracks, once per waterfall line. Nearest-first assignment inside a gate around the
/// predicted bearing. Tracks are never created automatically anymore — every one starts from the player marking
/// a bearing (MarkBearing), so faint intermittent contacts or plain noise spikes can no longer spam the track
/// list on their own. A marked track starts "searching" (Tentative) and locks ("Confirmed") once it gathers
/// ConfirmHits real detections; losing lock later drops it back to searching rather than removing it. Crossing
/// or merging contacts are NOT resolved for the player: an unresolved pair coasts and identities may swap.
/// Hardware caps the total number of tracks (see MarkBearing's maxTracks) and how many can be locked at once.
///
/// Other sensors count too (ApplySupport): an imager FIX blob or a radar echo on the track's bearing locks a
/// searching track at once and holds a locked one against waterfall misses for SupportHoldDays, so a contact
/// too faint for the waterfall can still be tracked. Retarget moves an existing track to a new bearing.
///
/// This list is the single source of truth for "what the ship knows is out there". The System view, Imager,
/// Spectrometer and radar TRACK mode all read it. Only the sensor processors (via SensorSight) look at the
/// real bodies. Range is fused from TMA and radar (see BestRange); identity comes from spectrometer dwell
/// (TrackInfo).
/// Pure C#, no Unity dependency.
/// </summary>
public sealed class TrackManager
{
    private const double SecondsPerDay = 86400.0;
    public const int ConfirmHits = 3;      // hits needed (ever, not in a rolling window) to lock a searching track
    /// <summary>The bearing rate is fitted over the hits of the last RateWindowDays (weighted by each sample's
    /// sigma), and only trusted (hasRate) once its 1-sigma is under MaxRateSigmaDegPerDay. Real contacts drift a
    /// fraction of a degree to a few degrees per day, so a fit over a few seconds of hits at low warp is pure
    /// noise (thousands of deg/day): extrapolating it sent the association gate across the sky.</summary>
    public const double RateWindowDays = 5.0;
    public const int MinRateSamples = 4;       // at high warp (days per line) the window is widened to this many hits
    public const float MaxRateSigmaDegPerDay = 0.5f;

    /// <summary>Association gate growth between hits. The gate is the waterfall's base gate plus the prediction's
    /// own uncertainty: 3 x the rate sigma times the time since the last hit, plus CurvatureFraction of the
    /// predicted move (a contact's bearing rate itself changes: nearby or fast ones curve across the image).
    /// With no usable rate, a contact is assumed to drift at up to MaxCoastRateDegPerDay. At low warp (seconds
    /// per line) all of this is negligible; at 30 d/s (15 days per line) it is what lets a fast contact be followed.</summary>
    public const float CurvatureFraction = 0.2f;
    public const float MaxCoastRateDegPerDay = 1f;
    public const float MaxPredictionSigmaDeg = 3f; // a rate this uncertain over this gap is not used to predict
    public const float MaxGateDeg = 12f;
    /// <summary>Beyond the base gate a detection must reach this SNR, and this fraction of the track's running
    /// SNR (Track.snrAvg), to be taken as the contact rather than noise.</summary>
    public const float ExtendedGateMinSnr = 5f;
    public const float ExtendedGateSnrFraction = 0.5f;
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

    /// <summary>How many tracks the hardware can hold locked at once, as last passed to Update() by the waterfall.
    /// Also caps confirmations coming from other sensors (ApplySupport).</summary>
    public int MaxConfirmed { get; private set; } = 4;

    /// <summary>A confirmation from another sensor holds the lock against waterfall misses for this long
    /// (simulated days). A faint contact the waterfall can only see intermittently stays locked as long as the
    /// imager or radar keeps re-confirming it. Tune in playtests.</summary>
    public const double SupportHoldDays = 10.0;

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

    /// <summary>Save/load: replaces every track and the id counters as they were saved.</summary>
    public void Restore(IList<Track> tracks, int nextId, int nextName, int selectedId, int generation, string pendingName)
    {
        _tracks.Clear();
        _tracks.AddRange(tracks);
        _nextId = nextId;
        _nextName = nextName;
        SelectedId = selectedId;
        Generation = generation;
        PendingName = pendingName ?? "";
        if (Changed != null) Changed();
    }

    public int NextIdForSave => _nextId;
    public int NextNameForSave => _nextName;

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

    /// <summary>
    /// Corrects an EXISTING track's bearing (the player re-marking it on the waterfall/DSP) as a new sample
    /// folded into its continuing history - NOT a fresh contact. Everything already known (range, elevation,
    /// radar fix, rate, identity) is kept: this goes through the exact same ApplyHit path a real detection
    /// does, so a correction participates in quality/rate/TMA like any other hit rather than wiping them. A
    /// correction built on bad data just becomes one noisy sample among the others the history-weighted rate
    /// fit and TMA already wash out over time - it doesn't get to overrule everything at once, on purpose. If
    /// corrections keep disagreeing badly (a genuinely different contact, or the player got it wrong), Drop +
    /// mark fresh is still the escape hatch, same as it always was.
    /// </summary>
    public bool CorrectBearing(int id, double time, float worldBearingDeg, float sigmaDeg,
                               double shipX, double shipZ, float headingDeg)
    {
        Track tr = Find(id);
        if (tr == null) return false;
        var d = new Detection
        {
            time = time,
            bearing = BearingMath.Wrap360(worldBearingDeg),
            snr = Math.Max(tr.lastSnr, 1f), // a deliberate mark reads as at least a threshold-strength hit
            sigmaDeg = Math.Max(sigmaDeg, 0.02f),
        };
        ApplyHit(tr, d, time, shipX, shipZ, headingDeg);
        if (Changed != null) Changed();
        return true;
    }

    /// <summary>
    /// A detection of this contact by a sensor other than the waterfall (imager FIX blob, radar sweep return in
    /// the gate, radar TRACK echo). It counts as a hit: a searching track locks immediately (an independent
    /// sensor seeing something exactly where the tracker is looking is stronger evidence than three waterfall
    /// hits), and a locked one is held against waterfall misses for SupportHoldDays.
    /// hasBearing: the sensor measured a bearing (imager, sweep) and it updates the track and its history (which
    /// feeds the rate fit and TMA, weighted by sigmaDeg). A TRACK echo was aimed down the track's own bearing, so
    /// it confirms without re-measuring.
    /// </summary>
    public void ApplySupport(int id, double time, ElevationSource source, bool hasBearing, float bearingDeg, float sigmaDeg,
                             double shipX, double shipZ, float headingDeg)
    {
        Track tr = Find(id);
        if (tr == null) return;

        if (hasBearing)
        {
            var d = new Detection();
            d.time = time; d.bearing = BearingMath.Wrap360(bearingDeg); d.snr = 0f;
            d.sigmaDeg = Math.Max(sigmaDeg, 0.005f);
            // A radar return is stamped with the ping's FIRE time, often hours before the latest waterfall hit:
            // it goes into the history in time order, but only a measurement at least as new as the last hit
            // may move the track's current bearing.
            if (time >= tr.lastTime) tr.bearing = d.bearing;
            AddSample(tr, d, shipX, shipZ, headingDeg);
            FitRate(tr);
            tr.tmaRange = RangeEstimator.Estimate(tr.history);
        }
        tr.lastTime = Math.Max(tr.lastTime, time);
        tr.hits++;
        tr.consecutiveMisses = 0;
        tr.quality += 0.3f * (1f - tr.quality);
        tr.supportHoldUntil = Math.Max(tr.supportHoldUntil, time + SupportHoldDays * SecondsPerDay);
        tr.lastSupport = source;

        if (tr.status == TrackStatus.Tentative)
        {
            int confirmed = 0;
            for (int i = 0; i < _tracks.Count; i++) if (_tracks[i].status == TrackStatus.Confirmed) confirmed++;
            if (confirmed < MaxConfirmed)
            {
                tr.status = TrackStatus.Confirmed;
                tr.lostLock = false;
                if (TrackConfirmed != null) TrackConfirmed(tr);
            }
        }
        tr.range = BestRange(tr, time, shipX, shipZ);
        if (Changed != null) Changed();
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
        MaxConfirmed = maxConfirmed;

        // Candidate pairs inside the gate
        _pairs.Clear();
        for (int ti = 0; ti < nt; ti++)
        {
            Track tk = _tracks[ti];
            float pred, gate;
            PredictGate(tk, time, gateDeg, out pred, out gate);
            // A track that HAD a lock and lost it stays where it was last seen: no extrapolation, base gate, and
            // only a strong detection can pick it back up. Otherwise stray noise hits walk it across the image.
            if (tk.lostLock) pred = tk.bearing;

            // A widened gate is only for FOLLOWING a live contact: a locked track through a few missed lines,
            // or a freshly marked one still acquiring (at high warp the contact may have moved past the base
            // gate by the next line). A contact that faded or vanished must go stale, not
            // be dragged across the image by noise: past that point the gate shrinks back to the base width.
            bool mayExtend = tk.status == TrackStatus.Confirmed
                ? tk.consecutiveMisses <= dropAfterMisses
                : !tk.lostLock && tk.consecutiveMisses <= dropAfterMisses; // fresh mark: still finding its contact
            if (!mayExtend) gate = gateDeg;
            // After a miss, also look around the last bearing (coast gate): if the prediction ran off (the
            // contact slowed or turned), the track can still recover instead of chasing its own extrapolation.
            float coastGate = mayExtend && tk.consecutiveMisses > 0
                ? Math.Min(gateDeg + CoastAllowance(tk, time), Math.Max(gateDeg, MaxGateDeg)) : -1f;
            // Outside the base gate, only a detection that looks like THIS contact counts: well clear of the noise
            // floor, and not much weaker than the contact has been. Threshold-level noise spikes are all over a
            // 12-degree window at every line; they are exactly what used to keep a dead track "alive".
            float strongSnr = Math.Max(ExtendedGateMinSnr, ExtendedGateSnrFraction * tk.snrAvg);
            // Inside the base gate: anything, except that a coasting track (missed the last line) needs a hit
            // at least half as strong as the contact has been, and a lost one a strong hit.
            float baseSnr = tk.lostLock ? strongSnr
                          : tk.consecutiveMisses > 0 ? ExtendedGateSnrFraction * tk.snrAvg : 0f;

            for (int di = 0; di < nd; di++)
            {
                float dPred = Math.Abs(BearingMath.Diff(detections[di].bearing, pred));
                float dLast = Math.Abs(BearingMath.Diff(detections[di].bearing, tk.bearing));
                float dist;
                if (Math.Min(dPred, dLast) <= gateDeg)
                {
                    if (detections[di].snr < baseSnr) continue;
                    dist = Math.Min(dPred, dLast);
                }
                else if (detections[di].snr < strongSnr) continue;
                else if (dPred <= gate) dist = dPred;
                else if (dLast <= coastGate) dist = dLast;
                else continue;
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

        // A radar fix keeps ageing between hits, so re-pick the best range for every track, not just the hit ones.
        for (int ti = 0; ti < _tracks.Count; ti++)
            _tracks[ti].range = BestRange(_tracks[ti], time, shipX, shipZ);

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
                // Lock needs ConfirmHits without a long gap: stray noise hits spread over many lines on a dead
                // bearing must not add up to a lock.
                if (tr.consecutiveMisses > dropAfterMisses) tr.hits = 0;
                if (tr.hits >= ConfirmHits && confirmed < maxConfirmed)
                {
                    tr.status = TrackStatus.Confirmed;
                    tr.lostLock = false;
                    confirmed++;
                    if (TrackConfirmed != null) TrackConfirmed(tr);
                }
                // Still searching: no timeout, no removal. It just stays red.
            }
            else if (tr.consecutiveMisses > dropAfterMisses && time >= tr.supportHoldUntil)
            {
                // Lost lock: back to searching (red) instead of disappearing.
                tr.status = TrackStatus.Tentative;
                tr.hits = 0;
                tr.lostLock = true;
                if (TrackLost != null) TrackLost(tr);
            }
        }

        if (Changed != null) Changed();
    }

    // ---------------------------------------------------------------------
    #region Range fusion (TMA + radar)
    /// <summary>A radar fix loses 1% of range per simulated day in extra sigma, on top of any known range
    /// rate carried forward. That's a stand-in for unmodelled target motion since the ping. Tune in playtests.</summary>
    public const double RadarFixAgeFractionPerDay = 0.01;

    /// <summary>
    /// Records a radar range measurement on a track (sweep return or track-mode ping). rangeUnits and
    /// sigmaUnits are in game units. The fix is fused with TMA immediately, and again on every later line as it ages.
    /// </summary>
    /// <param name="bearingDeg">Bearing the echo came from (the ping's aim or the return's own bearing), NOT the
    /// track's bearing now: the target position (x, z) is placed along the line the radar actually measured.</param>
    public void ApplyRadarFix(int id, double time, double rangeUnits, double sigmaUnits, float bearingDeg,
                              double shipX, double shipZ, bool hasRate, double rangeRateKmS)
    {
        Track tr = Find(id);
        if (tr == null || rangeUnits <= 0.0) return;

        var re = new RangeEstimate();
        re.valid = true;
        re.range = rangeUnits;
        re.rangeSigma = Math.Max(sigmaUnits, 1e-6);
        double b = bearingDeg * Math.PI / 180.0;
        double sinB = Math.Sin(b), cosB = Math.Cos(b);
        re.x = shipX + rangeUnits * Math.Sin(b);
        re.z = shipZ + rangeUnits * Math.Cos(b);
        re.samples = tr.tmaRange.samples;

        // Feeds the orbit solver, not just the range readout: OrbitFit.TryFit converts whatever tr.range
        // (BestRange) currently is into a state vector, position AND velocity - a radar fix with no velocity
        // here used to hand it a wrong "not moving relative to the primary" guess whenever BestRange picked
        // the radar fix over TMA (tighter sigma), silently producing a badly wrong orbit even off a good ping.
        // A TRACK-mode ping's own hasRate measures only the RADIAL (line-of-sight) component - real, but
        // half the story - so the TANGENTIAL component still has to come from TMA's own bearing-history fit
        // when one exists; only the along-LOS part gets replaced with radar's own (near-exact) reading.
        if (hasRate)
        {
            double tmaVx = tr.tmaRange.valid ? tr.tmaRange.vxKmS : 0.0;
            double tmaVz = tr.tmaRange.valid ? tr.tmaRange.vzKmS : 0.0;
            double tmaRadial = tmaVx * sinB + tmaVz * cosB; // TMA's own along-LOS component, to be replaced
            re.vxKmS = tmaVx + (rangeRateKmS - tmaRadial) * sinB;
            re.vzKmS = tmaVz + (rangeRateKmS - tmaRadial) * cosB;
        }

        tr.radarFix = re;
        tr.radarFixTime = time;
        tr.hasRadarRate = hasRate;
        tr.radarRangeRateKmS = hasRate ? rangeRateKmS : 0.0;
        tr.range = BestRange(tr, time, shipX, shipZ);
        if (Changed != null) Changed();
    }

    /// <summary>The radar fix propagated to `time`: range moved along by the measured range rate (if any),
    /// and the sigma grown with age. Invalid if there is no fix.</summary>
    public static RangeEstimate AgedRadarFix(Track tr, double time)
    {
        RangeEstimate f = tr.radarFix;
        if (!f.valid) return f;

        double ageSec = Math.Max(0.0, time - tr.radarFixTime);
        double ageDays = ageSec / SecondsPerDay;
        if (tr.hasRadarRate)
        {
            double unitsPerSec = tr.radarRangeRateKmS / ShipState.KmPerUnit;
            f.range = Math.Max(1e-6, f.range + unitsPerSec * ageSec);
            // x/z must age forward with the same fix, not just range: OrbitFit reads this as "position now",
            // and a stale (x, z) paired with an aged range used to describe two different times at once.
            double kmPerUnit = ShipState.KmPerUnit;
            f.x += f.vxKmS / kmPerUnit * ageSec;
            f.z += f.vzKmS / kmPerUnit * ageSec;
        }
        f.rangeSigma += f.range * RadarFixAgeFractionPerDay * ageDays;
        return f;
    }

    /// <summary>Picks whichever of TMA and the aged radar fix is tighter. TMA only counts once it's actually
    /// observable (post-manoeuvre); before that its "sigma" is just a number from an unconstrained fit.
    /// While the track is ACTIVELY locked (a hit this exact update, not coasting on a miss), the bearing is
    /// essentially exact right now - the returned estimate's (x, z) is pinned onto that ray at whatever range
    /// the fit/fusion above produced, rather than trusting a fitted or coasted position that can drift off the
    /// true bearing over a big time-warp jump between updates (a genuinely orbiting target's own curvature
    /// breaks any straight-line/single-fit extrapolation increasingly with elapsed time). Range and velocity
    /// are left as-is: bearing is the one quantity that's actually measured fresh every line.</summary>
    public static RangeEstimate BestRange(Track tr, double time, double shipX, double shipZ)
    {
        RangeEstimate radar = AgedRadarFix(tr, time);
        RangeEstimate tma = tr.tmaRange;
        bool tmaOk = tma.Observable;

        RangeEstimate best;
        if (radar.valid && tmaOk) best = radar.rangeSigma <= tma.rangeSigma ? radar : tma;
        else if (radar.valid) best = radar;
        else best = tma; // may itself be invalid / unobservable: callers check Observable

        if (best.valid && tr.status == TrackStatus.Confirmed && tr.consecutiveMisses == 0)
        {
            double rad = tr.bearing * Math.PI / 180.0;
            best.x = shipX + best.range * Math.Sin(rad);
            best.z = shipZ + best.range * Math.Cos(rad);
        }
        return best;
    }

    // ---------------------------------------------------------------------
    /// <summary>An elevation estimate loses this many degrees of confidence per simulated day, standing in for
    /// the contact's apparent motion since the fix (about a probe's own motion at ~1 AU). Tune in playtests.</summary>
    public const float ElevationAgeDegPerDay = 0.25f;

    /// <summary>Records an elevation measurement on a track. It replaces the current estimate only when it's at
    /// least as good as that estimate has aged to, so a coarse waterfall fan never overwrites a fresh imager fix.
    /// Returns true if it was taken.</summary>
    public bool ApplyElevationFix(int id, double time, float elevationDeg, float sigmaDeg, ElevationSource source)
    {
        Track tr = Find(id);
        if (tr == null) return false;
        bool taken = ApplyElevation(tr, time, elevationDeg, sigmaDeg, source);
        if (taken && Changed != null) Changed();
        return taken;
    }

    private static bool ApplyElevation(Track tr, double time, float elevationDeg, float sigmaDeg, ElevationSource source)
    {
        sigmaDeg = Math.Max(sigmaDeg, 0.005f);
        if (tr.hasElevation && sigmaDeg > AgedElevationSigma(tr, time)) return false;
        tr.hasElevation = true;
        tr.elevationDeg = Math.Max(-90f, Math.Min(90f, elevationDeg));
        tr.elevationSigmaDeg = sigmaDeg;
        tr.elevationFixTime = time;
        tr.elevationSource = source;
        return true;
    }

    /// <summary>Current 1-sigma of the elevation estimate, grown since the fix. +inf if there is none.</summary>
    public static float AgedElevationSigma(Track tr, double time)
    {
        if (!tr.hasElevation) return float.PositiveInfinity;
        double days = Math.Max(0.0, time - tr.elevationFixTime) / SecondsPerDay;
        return tr.elevationSigmaDeg + (float)(ElevationAgeDegPerDay * days);
    }

    /// <summary>How far this contact has been characterised, for the System view and sensor gating.</summary>
    public static TrackLevel LevelOf(Track tr)
    {
        if (tr.info.identified) return TrackLevel.Identified;
        if (tr.range.Observable) return TrackLevel.Ranged;
        if (tr.hasRate) return TrackLevel.Rate;
        return TrackLevel.Bearing;
    }
    #endregion

    // ---------------------------------------------------------------------
    private void Remove(int index)
    {
        if (_tracks[index].id == SelectedId) SelectedId = 0;
        _tracks.RemoveAt(index);
    }

    /// <summary>Where the track's bearing should be at `time`: the last bearing, carried along by the fitted
    /// rate only when that rate is trustworthy (hasRate).</summary>
    public static float Predict(Track tr, double time)
    {
        float pred, gate;
        PredictGate(tr, time, 0f, out pred, out gate);
        return pred;
    }

    /// <summary>
    /// Predicted bearing at `time` and the association gate around it. Three predictors, best first; each is used
    /// only if its own 1-sigma over the gap since the last hit stays under MaxPredictionSigmaDeg:
    ///  1. a weighted QUADRATIC fit over the last QuadSamples hits: follows a contact whose bearing rate is itself
    ///     changing (a nearby or fast body curving across the image at high warp),
    ///  2. the linear rate fit (FitRate),
    ///  3. coasting on the last bearing, with drift allowed up to MaxCoastRateDegPerDay.
    /// Over a short gap a noisy fit barely moves the prediction; over a long one it is not trusted at all, so a
    /// warp change can't send the gate across the sky. gate = base + 3 sigma + CurvatureFraction of the move.
    /// </summary>
    public static void PredictGate(Track tr, double time, float baseGateDeg, out float pred, out float gate)
    {
        float move, sigma;
        if (!QuadPredict(tr, time, out move, out sigma))
        {
            double days = Math.Max(0.0, (time - tr.lastTime) / SecondsPerDay);
            sigma = float.IsInfinity(tr.rateSigmaDegPerDay) ? float.PositiveInfinity
                  : (float)(tr.rateSigmaDegPerDay * days);
            move = (float)(tr.rateDegPerDay * days);
            if (sigma > MaxPredictionSigmaDeg)
            {
                pred = tr.bearing;
                gate = Math.Min(baseGateDeg + CoastAllowance(tr, time), Math.Max(baseGateDeg, MaxGateDeg));
                return;
            }
        }
        pred = BearingMath.Wrap360(tr.bearing + move);
        gate = baseGateDeg + 3f * sigma + CurvatureFraction * Math.Abs(move);
        gate = Math.Min(gate, Math.Max(baseGateDeg, MaxGateDeg));
    }

    /// <summary>How far a contact with no usable prediction may have drifted since the last hit.</summary>
    private static float CoastAllowance(Track tr, double time)
    {
        return (float)(MaxCoastRateDegPerDay * Math.Max(0.0, (time - tr.lastTime) / SecondsPerDay));
    }

    public const int QuadSamples = 6;

    /// <summary>Weighted least-squares y = c0 + c1 x + c2 x^2 over the last QuadSamples hits (x in days from the
    /// newest sample, y in degrees from its bearing), evaluated at `time`. move is relative to tr.bearing.
    /// sigma is the fit's own prediction uncertainty there (from the sample sigmas). False if too few samples,
    /// degenerate timing, or too uncertain to use.</summary>
    private static bool QuadPredict(Track tr, double time, out float move, out float sigma)
    {
        move = 0f; sigma = float.PositiveInfinity;
        int n = tr.history.Count;
        if (n < QuadSamples - 1) return false;
        int start = Math.Max(0, n - QuadSamples);
        BearingSample last = tr.history[n - 1];

        // Normal equations A c = r, A = sum w [1 x x2]^T [1 x x2].
        double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, r0 = 0, r1 = 0, r2 = 0;
        for (int i = start; i < n; i++)
        {
            BearingSample s = tr.history[i];
            double sig = Math.Max(0.01, s.sigmaDeg);
            double w = 1.0 / (sig * sig);
            double x = (s.time - last.time) / SecondsPerDay;
            double y = BearingMath.Diff(s.bearing, last.bearing);
            double x2 = x * x;
            s0 += w; s1 += w * x; s2 += w * x2; s3 += w * x2 * x; s4 += w * x2 * x2;
            r0 += w * y; r1 += w * y * x; r2 += w * y * x2;
        }
        // Inverse of the symmetric 3x3 [[s0 s1 s2][s1 s2 s3][s2 s3 s4]] by cofactors.
        double c00 = s2 * s4 - s3 * s3, c01 = s2 * s3 - s1 * s4, c02 = s1 * s3 - s2 * s2;
        double c11 = s0 * s4 - s2 * s2, c12 = s1 * s2 - s0 * s3, c22 = s0 * s2 - s1 * s1;
        double det = s0 * c00 + s1 * c01 + s2 * c02;
        if (Math.Abs(det) < 1e-30) return false;
        double inv = 1.0 / det;

        double c0 = (c00 * r0 + c01 * r1 + c02 * r2) * inv;
        double c1 = (c01 * r0 + c11 * r1 + c12 * r2) * inv;
        double c2 = (c02 * r0 + c12 * r1 + c22 * r2) * inv;

        double xq = (time - last.time) / SecondsPerDay;
        double g0 = 1, g1 = xq, g2 = xq * xq;
        // g^T A^-1 g
        double v = (g0 * (c00 * g0 + c01 * g1 + c02 * g2)
                  + g1 * (c01 * g0 + c11 * g1 + c12 * g2)
                  + g2 * (c02 * g0 + c12 * g1 + c22 * g2)) * inv;
        if (!(v >= 0) || double.IsInfinity(v)) return false;
        sigma = (float)Math.Sqrt(v);
        if (sigma > MaxPredictionSigmaDeg) return false;

        double yq = c0 + c1 * xq + c2 * xq * xq;
        move = BearingMath.Diff(BearingMath.Wrap360(last.bearing + (float)yq), tr.bearing);
        return true;
    }

    private static void AddSample(Track tr, Detection d, double shipX, double shipZ, float headingDeg)
    {
        var s = new BearingSample();
        s.time = d.time; s.bearing = d.bearing; s.snr = d.snr;
        s.sigmaDeg = d.sigmaDeg; s.shipX = shipX; s.shipZ = shipZ; s.headingDeg = headingDeg;
        s.elevationDeg = d.elevationDeg; s.elevationSigmaDeg = d.elevationSigmaDeg;
        // Keep the history in time order: late-arriving measurements (radar returns stamped at fire time) are
        // inserted where they belong. The rate fit, TMA and the waterfall's pixel chain all rely on the order.
        int at = tr.history.Count;
        while (at > 0 && tr.history[at - 1].time > s.time) at--;
        tr.history.Insert(at, s);
        if (tr.history.Count > HistoryLimit) tr.history.RemoveAt(0);
    }

    private static void ApplyHit(Track tr, Detection d, double time, double shipX, double shipZ, float headingDeg)
    {
        tr.bearing = d.bearing;
        tr.lastTime = time;
        tr.lastSnr = d.snr;
        tr.snrAvg = tr.snrAvg <= 0f ? d.snr : 0.7f * tr.snrAvg + 0.3f * d.snr;
        tr.hits++;
        tr.updates++;
        tr.consecutiveMisses = 0;
        tr.quality += 0.2f * (1f - tr.quality);
        AddSample(tr, d, shipX, shipZ, headingDeg);
        FitRate(tr);
        if (tr.status == TrackStatus.Confirmed || tr.hits >= ConfirmHits)
            tr.tmaRange = RangeEstimator.Estimate(tr.history);
        tr.range = BestRange(tr, time, shipX, shipZ);
        if (d.elevationSigmaDeg > 0f)
            ApplyElevation(tr, time, d.elevationDeg, d.elevationSigmaDeg, ElevationSource.Waterfall);
    }

    private static void ApplyMiss(Track tr)
    {
        tr.misses++;
        tr.updates++;
        tr.consecutiveMisses++;
        tr.quality = Math.Max(0f, tr.quality - 0.15f);
    }

    /// <summary>
    /// Weighted least-squares bearing rate (deg per simulated day) over the hits of the last RateWindowDays, each
    /// weighted by 1/sigma^2. Also yields the rate's own 1-sigma; hasRate is set only when that is small enough
    /// for the rate to mean something (MaxRateSigmaDegPerDay). Otherwise the track coasts on its last bearing.
    /// </summary>
    private static void FitRate(Track tr)
    {
        int n = tr.history.Count;
        tr.hasRate = false;
        tr.rateSigmaDegPerDay = float.PositiveInfinity;
        if (n < 3) { tr.rateDegPerDay = 0f; return; }

        double tEnd = tr.history[n - 1].time;
        int start = n - 1;
        while (start > 0 && ((tEnd - tr.history[start - 1].time) / SecondsPerDay <= RateWindowDays
                             || n - start < MinRateSamples)) start--;
        int count = n - start;
        if (count < 3) { tr.rateDegPerDay = 0f; return; }

        double t0 = tr.history[start].time;
        float b0 = tr.history[n - 1].bearing;
        double sw = 0, swx = 0, swy = 0;
        for (int i = start; i < n; i++)
        {
            BearingSample s = tr.history[i];
            double sig = Math.Max(0.01, s.sigmaDeg);
            double w = 1.0 / (sig * sig);
            double x = (s.time - t0) / SecondsPerDay;
            double y = BearingMath.Diff(s.bearing, b0);
            sw += w; swx += w * x; swy += w * y;
        }
        double mx = swx / sw, my = swy / sw;
        double sxx = 0, sxy = 0;
        for (int i = start; i < n; i++)
        {
            BearingSample s = tr.history[i];
            double sig = Math.Max(0.01, s.sigmaDeg);
            double w = 1.0 / (sig * sig);
            double dx = (s.time - t0) / SecondsPerDay - mx;
            sxx += w * dx * dx;
            sxy += w * dx * (BearingMath.Diff(s.bearing, b0) - my);
        }
        if (sxx < 1e-12) { tr.rateDegPerDay = 0f; return; }

        tr.rateDegPerDay = (float)(sxy / sxx);
        tr.rateSigmaDegPerDay = (float)Math.Sqrt(1.0 / sxx);
        tr.hasRate = tr.rateSigmaDegPerDay <= MaxRateSigmaDegPerDay;
    }
}
