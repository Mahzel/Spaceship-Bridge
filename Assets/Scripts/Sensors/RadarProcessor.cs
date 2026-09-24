using System;
using System.Collections.Generic;
using UnityEngine;

public enum RadarPingMode { Sweep, Track }

/// <summary>
/// One sweep echo: "something is at this bearing and this range". No identity. It sits on the scope, fading,
/// until returnPersistenceDays have passed. The player can click it to mark a track (seeded with this range).
/// </summary>
public sealed class RadarReturn
{
    public int    pingId;
    public float  bearingDeg;     // world bearing, degrees [0, 360)
    public float  bearingSigmaDeg;
    public float  elevationDeg;       // where the beam was pointed: the sweep doesn't resolve inside its fan
    public float  elevationSigmaDeg;
    public double rangeAu;
    public double rangeSigmaAu;
    public float  snr;            // CFAR score, in noise sigmas
    public double fireSimTime;    // when the ping went out: the geometry the range describes
    public double echoSimTime;    // when the echo reached the ship (it appears on the scope then, not at fire time)
    public int    markedTrackId;  // non-zero once the player has marked a track from this return
}

/// <summary>
/// Snapshot of what the last ping found, once it resolves. Sweep: how many returns came back. Track: a precise
/// range and range-rate (also written into the track as a radar fix, see TrackManager.ApplyRadarFix).
/// </summary>
public sealed class RadarPingResult
{
    public RadarPingMode mode;
    public bool hit;
    public int returnCount;          // sweep only
    public float bearingDeg;
    public double rangeAu;
    public double radialVelocityKmS; // track mode only; +away / -closing. 0 for sweep.
    public double resolvedSimTime;
    public bool trackMoved;          // track mode: the echo came back, but the track now points elsewhere, so it wasn't filed
}

/// <summary>
/// The radar sensor's actual work: fire a ping, wait out its propagation delay, resolve. Owned by GameState
/// and ticked every real frame from RunDriver (like WaterfallProcessor), so a ping keeps counting down even
/// while the player is looking at a different console tab. Only one ping can be in flight at a time: firing
/// again cancels the one in flight (CancelPending) and starts over.
///
/// SWEEP builds a bearing x range power grid for the aimed sector at fire time, the same pattern as the
/// waterfall line: unit Gaussian noise, each body's echo spread by a beam/range PSF, CFAR along range, then
/// local-maximum peak picking with sub-cell interpolation. Every peak is a RadarReturn. The returns are held
/// back and appear one by one as their round-trip time elapses, so near echoes come in first. A return that
/// falls inside an existing track's gate also refreshes that track's range. New tracks only come from the
/// player marking a return.
///
/// TRACK fires a hair-thin beam down the selected track's bearing (and elevation, see FireTrack) and returns a
/// near-exact range and range rate for whatever is there.
///
/// Elevation: every return and every hit also yields an elevation fix for the track, as good as the beam that
/// produced it (sweep fan: coarse; pencil: fine).
/// Plain C#, no MonoBehaviour.
/// </summary>
public sealed class RadarProcessor
{
    private const double SecondsPerDay = 86400.0;
    private const float EarthRadiusKm = 6371f;
    private const int CfarWindow = 24;  // range bins each side used for the noise estimate
    private const int CfarGuard = 3;    // bins each side excluded around the cell under test

    public readonly RadarSpec spec;
    public bool Enabled = true;

    // --- pending ping ---
    private bool _pending;
    private RadarPingMode _pendingMode;
    private float _pendingBearingDeg, _pendingHalfWidthDeg;
    private float _pendingElevationDeg, _pendingElHalfDeg;
    private double _pendingScaleAu;
    private int _pendingTrackId;
    private CelestialBody _pendingBody;      // track mode: the body physically in the beam at fire time
    private bool _pendingHitAtFire;
    private double _pendingFireRangeAu;
    private double _pendingFireSimTime, _pendingDueSimTime;
    private readonly List<RadarReturn> _inFlight = new List<RadarReturn>(); // sweep echoes not yet arrived
    private readonly Dictionary<int, float> _fixDiff = new Dictionary<int, float>(); // trackId -> best bearing diff this ping

    private readonly List<RadarReturn> _returns = new List<RadarReturn>();
    private int _pingCounter;
    private int _trackGeneration = -1;

    public bool Pinging { get { return _pending; } }
    public RadarPingMode PendingMode { get { return _pendingMode; } }
    public float PendingBearingDeg { get { return _pendingBearingDeg; } }
    public float PendingHalfWidthDeg { get { return _pendingHalfWidthDeg; } }
    public float PendingElevationDeg { get { return _pendingElevationDeg; } }

    /// <summary>Clamps an elevation to the antenna's mechanical limits.</summary>
    public float ClampTilt(float elevationDeg) { return Mathf.Clamp(elevationDeg, -spec.maxTiltDeg, spec.maxTiltDeg); }
    public RadarPingResult LastResult { get; private set; }

    /// <summary>Sweep returns currently on the scope (already arrived, not yet faded out). Oldest first.</summary>
    public IList<RadarReturn> Returns { get { return _returns; } }

    public event Action Changed;

    public RadarProcessor(RadarSpec spec) { this.spec = spec; }

    /// <summary>Resets for a new run (or a spec swap). Called from GameState.ResetProbe(), same as Waterfall.</summary>
    public void Clear()
    {
        _pending = false;
        _inFlight.Clear();
        _returns.Clear();
        LastResult = null;
        if (Changed != null) Changed();
    }

    /// <summary>Real seconds until the pending ping finishes, at the CURRENT warp. That's an estimate, since
    /// warp can change while it's in flight. 0 when nothing is pending.</summary>
    public double EtaRealSeconds
    {
        get
        {
            if (!_pending || Game.Clock == null) return 0.0;
            double simLeft = Math.Max(0.0, _pendingDueSimTime - Game.Clock.SimSeconds);
            double rate = Game.Clock.BaseRate * Math.Max(0.0001, (double)Game.Clock.WarpFactor);
            return simLeft / rate;
        }
    }

    /// <summary>How far out echoes have had time to come back from, AU. The scope draws this as the growing
    /// "listening" ring. 0 when idle.</summary>
    public double EchoHorizonAu
    {
        get
        {
            if (!_pending || Game.Clock == null) return 0.0;
            double elapsedDays = (Game.Clock.SimSeconds - _pendingFireSimTime) / SecondsPerDay;
            return Math.Max(0.0, 0.5 * elapsedDays * Mathf.Max(0.1f, spec.pingSpeedAuPerDay));
        }
    }

    /// <summary>0..1 fade of a return: 1 when it just arrived, 0 when it's about to be removed.</summary>
    public float Freshness(RadarReturn r)
    {
        if (Game.Clock == null) return 1f;
        double ageDays = (Game.Clock.SimSeconds - r.echoSimTime) / SecondsPerDay;
        return Mathf.Clamp01(1f - (float)(ageDays / Mathf.Max(0.1f, spec.returnPersistenceDays)));
    }

    // ---------------------------------------------------------------------
    #region Firing
    /// <summary>
    /// Fires a sweep over [centre - halfWidth, centre + halfWidth] in bearing, at elevationDeg (fan of
    /// sweepElevationHalfWidthDeg), listening out to scaleAu. Echoes arrive progressively over the round-trip
    /// time to scaleAu.
    /// </summary>
    public bool FireSweep(float centerBearingDeg, float halfWidthDeg, double scaleAu, float elevationDeg, out string whyNot)
    {
        if (!CanFire(out whyNot)) return false;

        float half = Mathf.Clamp(halfWidthDeg, spec.sweepBeamMinDeg, spec.sweepBeamMaxDeg);
        float centre = BearingMath.Wrap360(centerBearingDeg);
        double scale = Math.Max(0.1, Math.Min(scaleAu, spec.maxRangeAu));

        Spend();
        _pingCounter++;
        _pending = true;
        _pendingMode = RadarPingMode.Sweep;
        _pendingBearingDeg = centre;
        _pendingHalfWidthDeg = half;
        _pendingElevationDeg = ClampTilt(elevationDeg);
        _pendingElHalfDeg = spec.sweepElevationHalfWidthDeg;
        _pendingScaleAu = scale;
        _pendingTrackId = 0;
        _pendingBody = null;
        _pendingFireSimTime = Game.Clock.SimSeconds;
        _pendingDueSimTime = _pendingFireSimTime + spec.RoundTripSimSeconds(scale);
        _fixDiff.Clear();

        _inFlight.Clear();
        ProcessSweep(centre, half, _pendingElevationDeg, scale, _pendingFireSimTime, _pingCounter, _inFlight);
        _inFlight.Sort((a, b) => a.echoSimTime.CompareTo(b.echoSimTime));

        if (Changed != null) Changed();
        return true;
    }

    /// <summary>
    /// Fires a narrow ping locked to a track's current bearing. In elevation it points at the track's elevation
    /// estimate and covers its uncertainty: a pencil beam when that's tight, a small nod over +/-2 sigma when
    /// it isn't. With no elevation on the track at all, it uses fallbackElevationDeg (the operator's manual aim)
    /// with the sweep fan. On resolve, a hit is written into the track as a radar fix (range + range rate), and
    /// an elevation fix as good as the beam that found it.
    /// </summary>
    public bool FireTrack(int trackId, float fallbackElevationDeg, out string whyNot)
    {
        if (!CanFire(out whyNot)) return false;

        Track tr = Game.State.Tracks.Find(trackId);
        if (tr == null) { whyNot = "radar.notrack"; return false; }

        double now = Game.Clock.SimSeconds;
        float elCentre, elHalf;
        if (tr.hasElevation)
        {
            elCentre = tr.elevationDeg;
            elHalf = Mathf.Max(spec.trackBeamDeg, 2f * TrackManager.AgedElevationSigma(tr, now));
        }
        else
        {
            elCentre = fallbackElevationDeg;
            elHalf = spec.sweepElevationHalfWidthDeg;
        }
        elCentre = ClampTilt(elCentre);

        Transform ship = SensorSight.Ship();
        bool found = FindNearestInBeam(ship, tr.bearing, spec.trackBeamDeg, elCentre, elHalf, spec.maxRangeAu,
                                       out CelestialBody body, out double rangeAu);
        _pendingElevationDeg = elCentre;
        _pendingElHalfDeg = elHalf;

        Spend();
        _pingCounter++;
        _pending = true;
        _pendingMode = RadarPingMode.Track;
        _pendingBearingDeg = tr.bearing;
        _pendingHalfWidthDeg = spec.trackBeamDeg;
        _pendingScaleAu = spec.maxRangeAu;
        _pendingTrackId = trackId;
        _pendingBody = found ? body : null;
        _pendingHitAtFire = found;
        _pendingFireRangeAu = found ? rangeAu : 0.0;
        _pendingFireSimTime = Game.Clock.SimSeconds;
        _pendingDueSimTime = _pendingFireSimTime + spec.RoundTripSimSeconds(found ? rangeAu : spec.maxRangeAu);
        if (Changed != null) Changed();
        return true;
    }

    private bool CanFire(out string whyNot)
    {
        whyNot = "";
        if (!Enabled) { whyNot = "radar.off"; return false; }
        if (Game.State == null || Game.Clock == null) { whyNot = "radar.no"; return false; }
        if (Game.Run == null || Game.Run.Phase != RunPhase.Flight) { whyNot = "radar.no"; return false; }
        if (spec.energyPerPing > Game.State.PowerStored) { whyNot = "radar.power"; return false; }
        return true;
    }

    /// <summary>Abandons the ping in flight: echoes that haven't reached the ship yet are dropped (returns
    /// already on the scope stay). Firing again while a ping is out does this automatically: the receiver can
    /// only time one ping's echoes against one transmit time.</summary>
    public void CancelPending()
    {
        if (!_pending) return;
        _pending = false;
        _inFlight.Clear();
        _pendingBody = null;
        if (Changed != null) Changed();
    }

    private void Spend()
    {
        CancelPending(); // a new ping replaces the one in flight
        Game.State.ConsumePower(spec.energyPerPing);
    }
    #endregion

    // ---------------------------------------------------------------------
    #region Tick
    /// <summary>Call every real frame (RunDriver), regardless of what's on screen.</summary>
    public void Tick(float realDeltaSeconds)
    {
        if (Game.Clock == null || Game.State == null) return;

        // Tracks were wiped (system jump / new run): anything on the scope described the old system.
        if (_trackGeneration != Game.State.Tracks.Generation)
        {
            _trackGeneration = Game.State.Tracks.Generation;
            if (_returns.Count > 0 || _pending) Clear();
        }

        double now = Game.Clock.SimSeconds;
        bool changed = false;

        // Fade out old returns.
        double maxAgeSec = Mathf.Max(0.1f, spec.returnPersistenceDays) * SecondsPerDay;
        for (int i = _returns.Count - 1; i >= 0; i--)
            if (now - _returns[i].echoSimTime > maxAgeSec) { _returns.RemoveAt(i); changed = true; }

        if (_pending && _pendingMode == RadarPingMode.Sweep)
        {
            // Echoes arrive in order of range.
            while (_inFlight.Count > 0 && _inFlight[0].echoSimTime <= now)
            {
                RadarReturn r = _inFlight[0];
                _inFlight.RemoveAt(0);
                _returns.Add(r);
                RangeTrackFromReturn(r);
                changed = true;
            }
        }

        if (_pending && now >= _pendingDueSimTime)
        {
            Resolve(now);
            changed = true;
        }

        if (changed && Changed != null) Changed();
    }

    private void Resolve(double now)
    {
        var result = new RadarPingResult();
        result.mode = _pendingMode;
        result.bearingDeg = _pendingBearingDeg;
        result.resolvedSimTime = now;

        if (_pendingMode == RadarPingMode.Sweep)
        {
            // Anything still in flight is past the listen window by definition; flush it.
            for (int i = 0; i < _inFlight.Count; i++) { _returns.Add(_inFlight[i]); RangeTrackFromReturn(_inFlight[i]); }
            _inFlight.Clear();

            int count = 0;
            for (int i = 0; i < _returns.Count; i++) if (_returns[i].pingId == _pingCounter) count++;
            result.returnCount = count;
            result.hit = count > 0;
        }
        else
        {
            Track tr = Game.State.Tracks.Find(_pendingTrackId);
            bool bodyAlive = _pendingBody != null; // Unity's null check catches a destroyed/pooled body
            // The echo describes whatever sat on the bearing the ping was FIRED at. If the track has since been
            // moved elsewhere (re-marked, or re-associated), the range is not its range: report it, don't file it.
            bool trackStillThere = tr != null &&
                Mathf.Abs(BearingMath.Diff(TrackManager.Predict(tr, now), _pendingBearingDeg)) <= Mathf.Max(2f * spec.trackBeamDeg, 1f);
            if (_pendingHitAtFire && bodyAlive && tr != null)
            {
                Transform ship = SensorSight.Ship();
                double rangeNowAu = ship != null ? SensorSight.RangeAu(_pendingBody, ship) : _pendingFireRangeAu;

                // Average range rate over the ping's own flight time: "how much closer/further did it get while
                // the ping was out". Not an instantaneous Doppler shift, but for this game's slowed-down
                // propagation the secant rate over the trip is the more defensible number anyway.
                double elapsedSimSeconds = Math.Max(1.0, now - _pendingFireSimTime);
                double auPerSecond = (rangeNowAu - _pendingFireRangeAu) / elapsedSimSeconds;
                result.radialVelocityKmS = auPerSecond * GameConstants.AU_IN_METERS / 1000.0;

                result.hit = true;
                result.rangeAu = _pendingFireRangeAu; // as of the moment the ping actually reflected

                double units = _pendingFireRangeAu * GameConstants.GAME_UNITS_PER_UA;
                ShipState s = Game.State.Ship;
                result.trackMoved = !trackStillThere;
                if (trackStillThere)
                {
                    // The fix is stamped at fire time; AgedRadarFix carries it forward with the measured rate.
                    Game.State.Tracks.ApplyRadarFix(tr.id, _pendingFireSimTime, units, units * spec.rangeSigmaFraction, _pendingBearingDeg,
                                                    s.x, s.z, true, result.radialVelocityKmS);
                    // The echo proves the target sits inside the beam we pointed: that's the elevation fix.
                    Game.State.Tracks.ApplyElevationFix(tr.id, _pendingFireSimTime, _pendingElevationDeg,
                                                        0.6f * _pendingElHalfDeg, ElevationSource.Radar);
                    // An echo down the track's bearing confirms the contact (no new bearing: the beam was aimed there).
                    Game.State.Tracks.ApplySupport(tr.id, _pendingFireSimTime, ElevationSource.Radar, false, 0f, 0f,
                                                   s.x, s.z, (float)s.headingDeg);
                }
            }
            else
            {
                result.hit = false;
            }
        }

        LastResult = result;
        _pending = false;
    }

    /// <summary>A sweep return inside an existing track's gate refreshes that track's range. It never creates
    /// a track. If several returns from one ping fall in the gate, the closest in bearing wins.</summary>
    private void RangeTrackFromReturn(RadarReturn r)
    {
        TrackManager tm = Game.State.Tracks;
        float gate = Mathf.Max(spec.sweepCellDeg, 3f * r.bearingSigmaDeg);
        Track best = null;
        float bestDiff = float.MaxValue;
        IList<Track> all = tm.All;
        for (int i = 0; i < all.Count; i++)
        {
            float d = Mathf.Abs(BearingMath.Diff(all[i].bearing, r.bearingDeg));
            if (d <= gate && d < bestDiff) { bestDiff = d; best = all[i]; }
        }
        if (best == null) return;

        float prev;
        if (_fixDiff.TryGetValue(best.id, out prev) && prev <= bestDiff) return;
        _fixDiff[best.id] = bestDiff;

        ShipState s = Game.State.Ship;
        double u = GameConstants.GAME_UNITS_PER_UA;
        tm.ApplyRadarFix(best.id, r.fireSimTime, r.rangeAu * u, r.rangeSigmaAu * u, r.bearingDeg, s.x, s.z, false, 0.0);
        tm.ApplyElevationFix(best.id, r.fireSimTime, r.elevationDeg, r.elevationSigmaDeg, ElevationSource.Radar);
        tm.ApplySupport(best.id, r.fireSimTime, ElevationSource.Radar, true, r.bearingDeg, r.bearingSigmaDeg,
                        s.x, s.z, (float)s.headingDeg);
    }
    #endregion

    // ---------------------------------------------------------------------
    #region Sweep signal processing
    private float[] _grid;       // [cell * bins + bin], power in noise-sigma units
    private float[] _score;      // CFAR output, same layout
    private double[] _prefix, _prefixSq;

    private struct Echo { public float cell, bin, snr; }
    private readonly List<Echo> _echoes = new List<Echo>();

    private void ProcessSweep(float centre, float half, float elAim, double scaleAu, double fireTime, int pingId, List<RadarReturn> output)
    {
        output.Clear();
        float cellDeg = Mathf.Max(0.1f, spec.sweepCellDeg);
        int cells = Mathf.Max(1, Mathf.CeilToInt(2f * half / cellDeg));
        int bins = Mathf.Max(32, spec.rangeBins);
        float start = centre - half;                          // bearing of the sector's left edge
        double binAu = scaleAu / bins;

        int n = cells * bins;
        if (_grid == null || _grid.Length < n) { _grid = new float[n]; _score = new float[n]; }

        // 1) Noise floor: unit Gaussian in every cell.
        for (int i = 0; i < n; i++) _grid[i] = Gaussian();

        // 2) Echoes: radar equation SNR = ref * (sigma / sigmaRef) * (Rref / R)^4, spread by the beam and range PSF.
        Transform ship = SensorSight.Ship();
        List<CelestialBody> bodies = SensorSight.AllBodies();
        float earthRadiusUnits = (float)(EarthRadiusKm / ShipState.KmPerUnit);
        _echoes.Clear();
        for (int i = 0; i < bodies.Count; i++)
        {
            CelestialBody b = bodies[i];
            double rAu = SensorSight.RangeAu(b, ship);
            if (rAu <= 0.0 || rAu >= scaleAu) continue;
            float rel = BearingMath.Diff(SensorSight.WorldAzimuth(b, ship), centre);
            if (Mathf.Abs(rel) > half + cellDeg) continue; // a little beyond the edge still bleeds into the edge cell
            float elGain = SensorSight.BeamGain(SensorSight.WorldElevation(b, ship) - elAim, spec.sweepElevationHalfWidthDeg);
            if (elGain < 1e-4f) continue;

            float sizeRatio = Mathf.Max(b.radius, 1e-9f) / earthRadiusUnits;
            float albedo = Mathf.Max(b.albedo, 0.05f);
            double snr = spec.referenceSnr * sizeRatio * sizeRatio * (albedo / 0.3f)
                       * Math.Pow(spec.referenceRangeAu / Math.Max(rAu, 1e-3), 4.0)
                       * elGain * elGain; // two-way: the fan shapes both transmit and receive
            snr = Math.Min(snr, 1e6); // keeps a nearby star from overflowing anything downstream

            var e = new Echo();
            e.cell = (rel + half) / cellDeg;       // fractional cell index
            e.bin = (float)(rAu / binAu);          // fractional range bin
            e.snr = (float)snr;
            _echoes.Add(e);
        }
        for (int i = 0; i < _echoes.Count; i++) Spread(_echoes[i], cells, bins);

        // 3) CFAR along range, per cell (range profiles are not circular, unlike the waterfall's bearing line).
        for (int c = 0; c < cells; c++) Cfar(c * bins, bins);

        // 4) Peaks: local max over the 3x3 (cell, bin) neighbourhood, above threshold.
        float thr = spec.detectionThresholdSigma;
        for (int c = 0; c < cells; c++)
        {
            for (int k = 0; k < bins; k++)
            {
                float v = _score[c * bins + k];
                if (v < thr) continue;
                if (!IsLocalMax(c, k, v, cells, bins)) continue;

                float dc = SubBin(c > 0 ? _grid[(c - 1) * bins + k] : float.NaN, _grid[c * bins + k],
                                  c < cells - 1 ? _grid[(c + 1) * bins + k] : float.NaN);
                float dk = SubBin(k > 0 ? _grid[c * bins + k - 1] : float.NaN, _grid[c * bins + k],
                                  k < bins - 1 ? _grid[c * bins + k + 1] : float.NaN);

                var r = new RadarReturn();
                r.pingId = pingId;
                r.bearingDeg = BearingMath.Wrap360(start + (c + 0.5f + dc) * cellDeg);
                r.bearingSigmaDeg = Mathf.Max(0.05f * cellDeg, 0.5f * cellDeg / Mathf.Max(v, 1f));
                r.elevationDeg = elAim;
                r.elevationSigmaDeg = 0.6f * spec.sweepElevationHalfWidthDeg;
                r.rangeAu = Math.Max(0.0, (k + 0.5 + dk) * binAu);
                r.rangeSigmaAu = binAu * Math.Max(0.1, 0.5 / Math.Max(v, 1f));
                r.snr = v;
                r.fireSimTime = fireTime;
                r.echoSimTime = fireTime + spec.RoundTripSimSeconds(r.rangeAu);
                output.Add(r);
            }
        }
    }

    private void Spread(Echo e, int cells, int bins)
    {
        const float sigmaCell = 0.5f; // beam pattern: FWHM ~ 1.2 cells
        const float sigmaBin = 0.7f;  // range PSF (pulse width)
        int hc = Mathf.CeilToInt(3f * sigmaCell), hb = Mathf.CeilToInt(3f * sigmaBin);
        int c0 = Mathf.FloorToInt(e.cell), b0 = Mathf.FloorToInt(e.bin);
        for (int dc = -hc; dc <= hc; dc++)
        {
            int c = c0 + dc;
            if (c < 0 || c >= cells) continue;
            float xc = (c + 0.5f) - e.cell;
            float wc = Mathf.Exp(-0.5f * xc * xc / (sigmaCell * sigmaCell));
            for (int db = -hb; db <= hb; db++)
            {
                int k = b0 + db;
                if (k < 0 || k >= bins) continue;
                float xb = (k + 0.5f) - e.bin;
                float wb = Mathf.Exp(-0.5f * xb * xb / (sigmaBin * sigmaBin));
                _grid[c * bins + k] += e.snr * wc * wb;
            }
        }
    }

    /// <summary>Cell-averaging CFAR with guard cells, via prefix sums. The output is in noise sigmas, like the
    /// waterfall's cfarNoise line.</summary>
    private void Cfar(int offset, int bins)
    {
        if (_prefix == null || _prefix.Length < bins + 1) { _prefix = new double[bins + 1]; _prefixSq = new double[bins + 1]; }
        _prefix[0] = 0; _prefixSq[0] = 0;
        for (int k = 0; k < bins; k++)
        {
            double x = _grid[offset + k];
            _prefix[k + 1] = _prefix[k] + x;
            _prefixSq[k + 1] = _prefixSq[k] + x * x;
        }

        for (int k = 0; k < bins; k++)
        {
            double sum = 0, sq = 0; int cnt = 0;
            int lo0 = Math.Max(0, k - CfarGuard - CfarWindow), lo1 = Math.Max(0, k - CfarGuard);
            int hi0 = Math.Min(bins, k + CfarGuard + 1), hi1 = Math.Min(bins, k + CfarGuard + 1 + CfarWindow);
            if (lo1 > lo0) { sum += _prefix[lo1] - _prefix[lo0]; sq += _prefixSq[lo1] - _prefixSq[lo0]; cnt += lo1 - lo0; }
            if (hi1 > hi0) { sum += _prefix[hi1] - _prefix[hi0]; sq += _prefixSq[hi1] - _prefixSq[hi0]; cnt += hi1 - hi0; }

            float score;
            if (cnt < 4) score = 0f;
            else
            {
                double mean = sum / cnt;
                double var = Math.Max(sq / cnt - mean * mean, 1e-12);
                score = (float)((_grid[offset + k] - mean) / Math.Sqrt(var));
            }
            _score[offset + k] = score;
        }
    }

    private bool IsLocalMax(int c, int k, float v, int cells, int bins)
    {
        for (int dc = -1; dc <= 1; dc++)
        {
            int cc = c + dc;
            if (cc < 0 || cc >= cells) continue;
            for (int dk = -1; dk <= 1; dk++)
            {
                if (dc == 0 && dk == 0) continue;
                int kk = k + dk;
                if (kk < 0 || kk >= bins) continue;
                float w = _score[cc * bins + kk];
                // Ties go to the earlier cell/bin, so a flat-topped peak yields exactly one return.
                if (w > v || (w == v && (dc < 0 || (dc == 0 && dk < 0)))) return false;
            }
        }
        return true;
    }

    /// <summary>Parabolic peak offset in [-0.5, 0.5]; 0 when a neighbour is missing (grid edge).</summary>
    private static float SubBin(float y0, float y1, float y2)
    {
        if (float.IsNaN(y0) || float.IsNaN(y2)) return 0f;
        float denom = y0 - 2f * y1 + y2;
        if (denom > -1e-9f) return 0f;
        return Mathf.Clamp(0.5f * (y0 - y2) / denom, -0.5f, 0.5f);
    }

    private static float Gaussian()
    {
        float u1 = Mathf.Max(1e-6f, 1f - UnityEngine.Random.value);
        float u2 = 1f - UnityEngine.Random.value;
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
    }
    #endregion

    // ---------------------------------------------------------------------
    private static bool FindNearestInBeam(Transform ship, float centreDeg, float halfWidthDeg,
                                          float elCentreDeg, float elHalfDeg, double maxRangeAu,
                                          out CelestialBody nearest, out double rangeAu)
    {
        nearest = null;
        rangeAu = 0.0;
        if (ship == null) return false;

        List<CelestialBody> bodies = SensorSight.AllBodies();
        double best = double.MaxValue;
        for (int i = 0; i < bodies.Count; i++)
        {
            CelestialBody body = bodies[i];
            float az = SensorSight.WorldAzimuth(body, ship);
            if (Mathf.Abs(BearingMath.Diff(az, centreDeg)) > halfWidthDeg) continue;
            if (Mathf.Abs(SensorSight.WorldElevation(body, ship) - elCentreDeg) > elHalfDeg) continue;

            double d = SensorSight.RangeAu(body, ship);
            if (d > maxRangeAu) continue;
            if (d < best) { best = d; nearest = body; rangeAu = d; }
        }
        return nearest != null;
    }
}
