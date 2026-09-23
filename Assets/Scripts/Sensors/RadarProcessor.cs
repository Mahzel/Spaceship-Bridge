using System;
using UnityEngine;

public enum RadarPingMode { Sweep, Track }

/// <summary>
/// Snapshot of what the last ping found, once it resolves. Sweep results never reveal identity (that's what
/// the passive sensors are for) — only a range, or nothing. Track results also write straight into the
/// track's own RangeEstimate on resolve (see RadarProcessor.Tick), since a landed active ping is strictly
/// better range information than bearing-only TMA.
/// </summary>
public sealed class RadarPingResult
{
    public RadarPingMode mode;
    public bool hit;
    public float bearingDeg;
    public double rangeAu;
    public double radialVelocityKmS; // track mode only; +away / -closing. 0 for sweep.
    public double resolvedSimTime;
}

/// <summary>
/// The radar sensor's actual work: fire a ping, wait out its propagation delay, resolve. Owned by GameState
/// and ticked every real frame from RunDriver — the same way WaterfallProcessor is — so a ping keeps counting
/// down even while the player is looking at a different console tab. Only one ping can be in flight at a
/// time; firing again while one is pending is refused. Plain C#, no MonoBehaviour.
/// </summary>
public sealed class RadarProcessor
{
    public readonly RadarSpec spec;
    public bool Enabled = true;

    private bool _pending;
    private bool _pendingHitAtFire;
    private RadarPingMode _pendingMode;
    private float _pendingBearingDeg;
    private int _pendingTrackId;
    private CelestialBody _pendingBody;      // track mode: the body found near the track's bearing at fire time
    private double _pendingFireRangeAu;
    private double _pendingFireSimTime, _pendingDueSimTime;

    public bool Pinging { get { return _pending; } }
    public RadarPingResult LastResult { get; private set; }

    public event Action Changed;

    public RadarProcessor(RadarSpec spec) { this.spec = spec; }

    /// <summary>Resets for a new run (or a spec swap). Called from GameState.ResetProbe(), same as Waterfall.</summary>
    public void Clear()
    {
        _pending = false;
        LastResult = null;
        if (Changed != null) Changed();
    }

    /// <summary>Real seconds until the pending ping resolves, at the CURRENT warp — an estimate, since warp
    /// can change while it's in flight. 0 when nothing is pending.</summary>
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

    /// <summary>Fires a wide, free-aimed ping. Never associates with a track — the result only ever says
    /// "something at range X" or nothing, no identity.</summary>
    public bool FireSweep(float centerBearingDeg, float halfWidthDeg, out string whyNot)
    {
        if (!CanFire(out whyNot)) return false;

        Transform ship = ShipTransform();
        float half = Mathf.Clamp(halfWidthDeg, spec.sweepBeamMinDeg, spec.sweepBeamMaxDeg);
        float centre = BearingMath.Wrap360(centerBearingDeg);
        bool found = FindNearestInBeam(ship, centre, half, out CelestialBody body, out double rangeAu);

        Spend();
        _pending = true;
        _pendingMode = RadarPingMode.Sweep;
        _pendingBearingDeg = centre;
        _pendingTrackId = 0;
        _pendingBody = null; // identity never reported for a sweep
        _pendingHitAtFire = found;
        _pendingFireRangeAu = found ? rangeAu : spec.maxRangeAu;
        _pendingFireSimTime = Game.Clock.SimSeconds;
        _pendingDueSimTime = _pendingFireSimTime + spec.RoundTripSimSeconds(_pendingFireRangeAu);
        if (Changed != null) Changed();
        return true;
    }

    /// <summary>Fires a narrow ping locked to a track's current bearing. On resolve, a hit overwrites that
    /// track's RangeEstimate with a near-exact one (see Tick).</summary>
    public bool FireTrack(int trackId, out string whyNot)
    {
        if (!CanFire(out whyNot)) return false;

        Track tr = Game.State.Tracks.Find(trackId);
        if (tr == null) { whyNot = "radar.notrack"; return false; }

        Transform ship = ShipTransform();
        bool found = FindNearestInBeam(ship, tr.bearing, spec.trackBeamDeg, out CelestialBody body, out double rangeAu);

        Spend();
        _pending = true;
        _pendingMode = RadarPingMode.Track;
        _pendingBearingDeg = tr.bearing;
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
        if (_pending) { whyNot = "radar.busy"; return false; }
        if (Game.State == null || Game.Clock == null) { whyNot = "radar.no"; return false; }
        if (Game.Run == null || Game.Run.Phase != RunPhase.Flight) { whyNot = "radar.no"; return false; }
        if (spec.energyPerPing > Game.State.PowerStored) { whyNot = "radar.power"; return false; }
        return true;
    }

    private void Spend()
    {
        Game.State.ConsumePower(spec.energyPerPing);
    }

    /// <summary>Call every real frame (RunDriver), regardless of what's on screen.</summary>
    public void Tick(float realDeltaSeconds)
    {
        if (!_pending || Game.Clock == null) return;
        if (Game.Clock.SimSeconds < _pendingDueSimTime) return;

        var result = new RadarPingResult();
        result.mode = _pendingMode;
        result.bearingDeg = _pendingBearingDeg;
        result.resolvedSimTime = Game.Clock.SimSeconds;

        if (_pendingMode == RadarPingMode.Sweep)
        {
            result.hit = _pendingHitAtFire;
            result.rangeAu = _pendingFireRangeAu;
        }
        else
        {
            Track tr = Game.State.Tracks != null ? Game.State.Tracks.Find(_pendingTrackId) : null;
            bool bodyAlive = _pendingBody != null; // Unity's null check catches a destroyed/pooled body
            if (_pendingHitAtFire && bodyAlive && tr != null)
            {
                Transform ship = ShipTransform();
                double rangeNowAu = ship != null
                    ? Vector3.Distance(ship.position, _pendingBody.transform.position) / GameConstants.GAME_UNITS_PER_UA
                    : _pendingFireRangeAu;

                // Average range-rate across the ping's own flight time — an honest "how much closer/further
                // did it get while the ping was out", not an instantaneous Doppler shift (this sim has no
                // hook into a body's true instantaneous velocity vector, and for the exaggerated propagation
                // speeds this game uses, the secant rate over the trip is the more defensible number anyway).
                double elapsedSimSeconds = Math.Max(1.0, result.resolvedSimTime - _pendingFireSimTime);
                double auPerSecond = (rangeNowAu - _pendingFireRangeAu) / elapsedSimSeconds;
                result.radialVelocityKmS = auPerSecond * GameConstants.AU_IN_METERS / 1000.0;

                result.hit = true;
                result.rangeAu = _pendingFireRangeAu; // reported as of the moment the ping actually reflected

                double rangeGameUnits = _pendingFireRangeAu * GameConstants.GAME_UNITS_PER_UA;
                var re = new RangeEstimate();
                re.valid = true;
                re.range = rangeGameUnits;
                re.rangeSigma = rangeGameUnits * spec.rangeSigmaFraction;
                re.samples = tr.range.samples;
                tr.range = re;
            }
            else
            {
                result.hit = false;
            }
        }

        LastResult = result;
        _pending = false;
        if (Changed != null) Changed();
    }

    private static Transform ShipTransform()
    {
        return SystemManager.Current != null && SystemManager.Current.PlayerShip != null
             ? SystemManager.Current.PlayerShip.transform : null;
    }

    // World bearing, mirroring WaterfallProcessor's own ComputeAzimuth exactly (0 = +Z, clockwise-positive) —
    // radar aims in the same fixed world frame the waterfall display and track bearings already use.
    private static float WorldAzimuth(CelestialBody body, Transform ship)
    {
        if (ship == null) return body.GetData().Az;
        Vector3 rel = body.transform.position - ship.position;
        return Vector3.SignedAngle(Vector3.forward, new Vector3(rel.x, 0f, rel.z), Vector3.up);
    }

    private bool FindNearestInBeam(Transform ship, float centreDeg, float halfWidthDeg,
                                   out CelestialBody nearest, out double rangeAu)
    {
        nearest = null;
        rangeAu = 0.0;
        if (ship == null) return false;

        CelestialBody[] bodies = UnityEngine.Object.FindObjectsByType<CelestialBody>(FindObjectsSortMode.None);
        double best = double.MaxValue;
        foreach (CelestialBody body in bodies)
        {
            if (body == null) continue;
            float az = WorldAzimuth(body, ship);
            if (Mathf.Abs(BearingMath.Diff(az, centreDeg)) > halfWidthDeg) continue;

            double d = Vector3.Distance(ship.position, body.transform.position) / GameConstants.GAME_UNITS_PER_UA;
            if (d > spec.maxRangeAu) continue;
            if (d < best) { best = d; nearest = body; rangeAu = d; }
        }
        return nearest != null;
    }
}
