using System;
using UnityEngine;

/// <summary>
/// The ship's actual orbital state: which body currently dominates it gravitationally (patched conics) and
/// the two-body conic it's on around that body, ellipse or hyperbola.
///
/// How it stays stable: the conic is solved ONCE from a state vector (Resolve) and then propagated exactly,
/// in double precision, as a pure function of time (Kepler's equation). It is NOT re-solved every frame. It is
/// re-solved only when something actually changes the motion:
///  - a burn or a teleport: the ship's velocity or position differs from what this class last wrote;
///  - a change of primary: FindPrimary, checked every tick, names a different body;
///  - Reset(): a system jump or a new run.
/// The old version re-solved every frame from a finite-difference velocity of single-precision positions.
/// At ~15 AU a float step is ~180 km, so each re-solve injected roughly 1 km/s of velocity error, and the
/// orbit random-walked (e drifting to 0.4-0.9 within minutes of play, then a position jump once the 0.99
/// eccentricity clamp kicked in). Everything here is double; floats only appear at the UI boundary.
///
/// Hyperbolic trajectories (e > 1) are propagated as real hyperbolas, not a straight-line coast.
/// Valid means "bound orbit" (what the UI calls a stable orbit); HasTrajectory means "has a conic at all".
/// Only a purely radial state (zero angular momentum) has no conic, and then the ship coasts straight.
/// </summary>
public sealed class ShipOrbit
{
    // --- public read-outs (UI, maneuver planning) ---------------------------------------------------------
    /// <summary>A bound (elliptical) orbit around the primary.</summary>
    public bool   Valid        { get { return HasTrajectory && _e < 1.0; } }
    /// <summary>A conic exists (ellipse or hyperbola) and the ship is being propagated along it.</summary>
    public bool   HasTrajectory { get; private set; }
    public bool   Hyperbolic   { get { return HasTrajectory && _e >= 1.0; } }
    public int    PrimaryIndex { get; private set; } = -1;
    public string PrimaryName  { get; private set; } = "";
    public double Mu           { get; private set; }
    /// <summary>Float element set, for display and the maneuver planner's previews. Refreshed on every
    /// Resolve (not every frame). Its eccentricity is clamped to 0.99 by StateToElements; read Eccentricity
    /// for the true value.</summary>
    public OrbitElements Elements { get; private set; }
    public double CaptureSimSeconds { get; private set; }
    public double TrueAnomalyDeg    { get; private set; }

    public double Eccentricity  { get { return _e; } }
    public double SemiMajorAxis { get { return _a; } } // game units; negative for a hyperbola

    public float PeriapsisGame => (float)(_a * (1.0 - _e));
    public float ApoapsisGame  => _e < 1.0 ? (float)(_a * (1.0 + _e)) : float.PositiveInfinity;

    // --- the conic, double precision ------------------------------------------------------------------------
    private double _mu, _e, _a, _n, _tPeri; // mean motion rad/s; time of periapsis passage
    private Vec3d  _P, _Q;                 // perifocal unit vectors: to periapsis, and 90 deg ahead in-plane

    // --- what this class last wrote into ShipState, to detect burns/teleports -------------------------------
    private bool   _hasLast;
    private double _lastT;
    private double _lx, _ly, _lz, _lvx, _lvy, _lvz;

    private const double DirtyPosEps = 1e-7; // game units (~15 m)
    private const double DirtyVelEps = 1e-7; // km/s

    /// <summary>Forgets the current orbit; the next Advance() resolves fresh from the ship's state. Call right
    /// after teleporting the ship (a system jump).</summary>
    public void Reset()
    {
        HasTrajectory = false;
        PrimaryIndex = -1;
        PrimaryName = "";
        _hasLast = false;
    }

    /// <summary>
    /// Call every real frame (SystemManager.MoveShip). Picks up any burn applied since the last call,
    /// propagates the ship to simSecondsNow, writes its position/velocity, and switches primary if needed.
    /// </summary>
    public void Advance(SystemData sys, ShipState ship, double simSecondsNow)
    {
        if (sys == null) { HasTrajectory = false; _hasLast = false; return; }

        if (!_hasLast) { Resolve(sys, ship, simSecondsNow); return; }

        // Something else moved the ship since we last wrote it (burn, maneuver node, placement):
        // that state is valid at the time we last wrote, so solve from there.
        if (ExternallyChanged(ship)) Resolve(sys, ship, _lastT);

        if (HasTrajectory)
        {
            RelativeState(simSecondsNow, out Vec3d rel, out Vec3d relV, out double nu);
            OrbitalMechanics.NodeState(sys, PrimaryIndex, simSecondsNow, out Vec3d pp, out Vec3d pv);
            Vec3d pos = pp + rel;
            Vec3d velKmS = (pv + relV) * ShipState.KmPerUnit;
            ship.x = pos.x; ship.y = pos.y; ship.z = pos.z;
            ship.vx = velKmS.x; ship.vy = velKmS.y; ship.vz = velKmS.z;
            TrueAnomalyDeg = Wrap360(nu * 180.0 / Math.PI);
        }
        else
        {
            ship.Advance(simSecondsNow - _lastT); // radial: straight-line coast for this tick
        }
        Record(ship, simSecondsNow);

        // Patched conics: hand over when another body now dominates (or retry if we had no conic).
        Vector3 shipPos = new Vector3((float)ship.x, (float)ship.y, (float)ship.z);
        float k = (float)ShipState.KmPerUnit;
        Vector3 shipVel = new Vector3((float)(ship.vx / k), (float)(ship.vy / k), (float)(ship.vz / k));
        int primary = OrbitalMechanics.FindPrimary(sys, shipPos, shipVel, simSecondsNow);
        if (!HasTrajectory || primary != PrimaryIndex) Resolve(sys, ship, simSecondsNow);
    }

    /// <summary>Solves the conic fresh from the ship's current state vector, taken to be valid at simSeconds.
    /// Called by Advance when needed, and directly by the maneuver planner right after it applies a burn.</summary>
    public void Resolve(SystemData sys, ShipState ship, double simSeconds)
    {
        Record(ship, simSeconds);
        HasTrajectory = false;
        if (sys == null || sys.nodes.Count == 0) return;

        Vector3 shipPosF = new Vector3((float)ship.x, (float)ship.y, (float)ship.z);
        float kf = (float)ShipState.KmPerUnit;
        Vector3 shipVelF = new Vector3((float)(ship.vx / kf), (float)(ship.vy / kf), (float)(ship.vz / kf));
        int primary = OrbitalMechanics.FindPrimary(sys, shipPosF, shipVelF, simSeconds);
        if (primary < 0) return;

        OrbitalMechanics.NodeState(sys, primary, simSeconds, out Vec3d pp, out Vec3d pv);
        Vec3d r = new Vec3d(ship.x, ship.y, ship.z) - pp;
        Vec3d v = new Vec3d(ship.vx, ship.vy, ship.vz) / ShipState.KmPerUnit - pv;
        double mu = OrbitalMechanics.Mu(OrbitalMechanics.MassOf(sys, primary));

        PrimaryIndex = primary;
        PrimaryName  = sys.nodes[primary].name;
        Mu = mu;
        CaptureSimSeconds = simSeconds;

        if (!SolveConic(r, v, mu, simSeconds, out double nu)) return;
        HasTrajectory = true;
        TrueAnomalyDeg = Wrap360(nu * 180.0 / Math.PI);

        (OrbitElements elements, double _) = OrbitalMechanics.StateToElements(r.ToVector3(), v.ToVector3(), mu, simSeconds);
        Elements = elements;
    }

    /// <summary>Ship position/velocity relative to its primary at simSeconds, from the current conic (game
    /// units, game units per simSecond). False when there is no conic.</summary>
    public bool RelativeStateAt(double simSeconds, out Vector3 relPos, out Vector3 relVel)
    {
        relPos = relVel = Vector3.zero;
        if (!HasTrajectory) return false;
        RelativeState(simSeconds, out Vec3d p, out Vec3d v, out double _);
        relPos = p.ToVector3();
        relVel = v.ToVector3();
        return true;
    }

    /// <summary>Offset from the primary at a given true anomaly (radians), from the current conic - a pure
    /// function of shape, not of time. Lets a map trace the whole ellipse (or the in-range branch of a
    /// hyperbola) by sampling anomaly directly instead of walking simulated time, so periapsis can be sampled
    /// as densely as apoapsis without hundreds of time-steps. False when there is no conic, or (hyperbolic
    /// only) nu is beyond the asymptote and the trajectory doesn't reach that direction at all.</summary>
    public bool OffsetAtTrueAnomaly(double nu, out Vector3 offset)
    {
        offset = Vector3.zero;
        if (!HasTrajectory) return false;
        double p = _a * (1.0 - _e * _e);
        double denom = 1.0 + _e * Math.Cos(nu);
        if (denom <= 1e-9) return false; // hyperbola: nu outside the asymptotic range
        double r = p / denom;
        Vec3d pos = _P * (r * Math.Cos(nu)) + _Q * (r * Math.Sin(nu));
        offset = pos.ToVector3();
        return true;
    }

    /// <summary>Where this conic crosses the reference plane (world Y = 0): ascending (Y increasing through
    /// zero) and descending. False (with both left at the origin) for an equatorial orbit, where the whole
    /// conic already lies in the plane and no single crossing point means anything.</summary>
    public bool NodeCrossings(out Vector3 ascending, out Vector3 descending)
    {
        ascending = descending = Vector3.zero;
        if (!HasTrajectory) return false;
        if (Math.Abs(_P.y) < 1e-9 && Math.Abs(_Q.y) < 1e-9) return false; // equatorial: P, Q already lie in Y=0

        double nuAsc = Math.Atan2(-_P.y, _Q.y);
        bool ok1 = OffsetAtTrueAnomaly(nuAsc, out ascending);
        bool ok2 = OffsetAtTrueAnomaly(nuAsc + Math.PI, out descending);
        return ok1 && ok2;
    }

    // ---------------------------------------------------------------------------------------------------------
    #region Conic maths (double)
    private bool SolveConic(Vec3d r, Vec3d v, double mu, double t, out double nu)
    {
        nu = 0.0;
        double rMag = r.Magnitude;
        if (rMag < 1e-12 || mu <= 0.0) return false;

        Vec3d h = Vec3d.Cross(r, v);
        double hMag = h.Magnitude;
        if (hMag < 1e-14 * rMag * Math.Max(v.Magnitude, 1e-20)) return false; // radial: no plane

        double v2 = v.SqrMagnitude;
        Vec3d eVec = ((v2 - mu / rMag) * r - Vec3d.Dot(r, v) * v) / mu;
        double e = eVec.Magnitude;
        // A parabola has no finite a; nudge off it (the difference is far below anything observable).
        if (Math.Abs(e - 1.0) < 1e-9) e = e < 1.0 ? 1.0 - 1e-9 : 1.0 + 1e-9;

        double energy = v2 / 2.0 - mu / rMag;
        double a = Math.Abs(energy) > 1e-30 ? -mu / (2.0 * energy) : (e < 1.0 ? 1e30 : -1e30);
        if ((e < 1.0) != (a > 0.0)) a = (hMag * hMag / mu) / (1.0 - e * e); // keep a consistent with e

        Vec3d hHat = h / hMag;
        Vec3d P = e > 1e-10 ? eVec / eVec.Magnitude : r / rMag; // circular: measure from the current point
        Vec3d Q = Vec3d.Cross(hHat, P);

        nu = Math.Atan2(Vec3d.Dot(r, Q), Vec3d.Dot(r, P));

        double n, M;
        if (e < 1.0)
        {
            double E = 2.0 * Math.Atan2(Math.Sqrt(1.0 - e) * Math.Sin(nu / 2.0), Math.Sqrt(1.0 + e) * Math.Cos(nu / 2.0));
            M = E - e * Math.Sin(E);
            n = Math.Sqrt(mu / (a * a * a));
        }
        else
        {
            double th = Math.Sqrt((e - 1.0) / (e + 1.0)) * Math.Tan(nu / 2.0);
            th = Math.Max(-0.999999999999, Math.Min(0.999999999999, th));
            double H = 2.0 * Atanh(th);
            M = e * Math.Sinh(H) - H;
            n = Math.Sqrt(mu / (-a * -a * -a));
        }

        _mu = mu; _e = e; _a = a; _n = n; _P = P; _Q = Q;
        _tPeri = t - M / n;
        return true;
    }

    private void RelativeState(double t, out Vec3d pos, out Vec3d vel, out double nu)
    {
        double e = _e, a = _a;
        double M = _n * (t - _tPeri);
        double xo, yo, vxo, vyo;
        if (e < 1.0)
        {
            M = Math.IEEERemainder(M, 2.0 * Math.PI);
            double E = SolveElliptic(M, e);
            double cosE = Math.Cos(E), sinE = Math.Sin(E), sq = Math.Sqrt(1.0 - e * e);
            double Edot = _n / (1.0 - e * cosE);
            xo = a * (cosE - e);          yo = a * sq * sinE;
            vxo = -a * sinE * Edot;        vyo = a * sq * cosE * Edot;
        }
        else
        {
            double H = SolveHyperbolic(M, e);
            double ch = Math.Cosh(H), sh = Math.Sinh(H), sq = Math.Sqrt(e * e - 1.0);
            double Hdot = _n / (e * ch - 1.0);
            xo = a * (ch - e);             yo = -a * sq * sh;
            vxo = a * sh * Hdot;           vyo = -a * sq * ch * Hdot;
        }
        pos = _P * xo + _Q * yo;
        vel = _P * vxo + _Q * vyo;
        nu = Math.Atan2(yo, xo);
    }

    private static double SolveElliptic(double M, double e)
    {
        double E = e < 0.8 ? M : Math.PI * Math.Sign(M == 0.0 ? 1.0 : M);
        for (int i = 0; i < 50; i++)
        {
            double f = E - e * Math.Sin(E) - M;
            double d = f / (1.0 - e * Math.Cos(E));
            E -= d;
            if (Math.Abs(d) < 1e-13) break;
        }
        return E;
    }

    private static double SolveHyperbolic(double M, double e)
    {
        // Good start for both small and large |M|, then Newton.
        double H = Math.Abs(M) < 6.0 * e ? Asinh(M / e) : Math.Sign(M) * Math.Log(2.0 * Math.Abs(M) / e + 1.8);
        for (int i = 0; i < 60; i++)
        {
            double f = e * Math.Sinh(H) - H - M;
            double d = f / (e * Math.Cosh(H) - 1.0);
            H -= d;
            if (Math.Abs(d) < 1e-13 * Math.Max(1.0, Math.Abs(H))) break;
        }
        return H;
    }

    private static double Atanh(double x) => 0.5 * Math.Log((1.0 + x) / (1.0 - x));
    private static double Asinh(double x) => Math.Log(x + Math.Sqrt(x * x + 1.0));
    #endregion

    // ---------------------------------------------------------------------------------------------------------
    private void Record(ShipState s, double t)
    {
        _hasLast = true;
        _lastT = t;
        _lx = s.x; _ly = s.y; _lz = s.z;
        _lvx = s.vx; _lvy = s.vy; _lvz = s.vz;
    }

    private bool ExternallyChanged(ShipState s)
    {
        return Math.Abs(s.x - _lx) > DirtyPosEps || Math.Abs(s.y - _ly) > DirtyPosEps || Math.Abs(s.z - _lz) > DirtyPosEps
            || Math.Abs(s.vx - _lvx) > DirtyVelEps || Math.Abs(s.vy - _lvy) > DirtyVelEps || Math.Abs(s.vz - _lvz) > DirtyVelEps;
    }

    private static double Wrap360(double deg)
    {
        deg %= 360.0;
        return deg < 0.0 ? deg + 360.0 : deg;
    }
}
