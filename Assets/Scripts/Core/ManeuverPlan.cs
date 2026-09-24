using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-planned burns: a small queue of maneuver nodes (max 2, enough for a Hohmann departure+arrival
/// pair), each a simulated time plus a prograde/retrograde and normal (plane-change) delta-v. Nodes are
/// previewed non-destructively against the ship's current osculating orbit (ShipOrbit.Elements) and, when
/// armed, executed automatically by Tick() (called from RunDriver every real frame) the instant simulated
/// time reaches them - which is what makes "warp to node" meaningful: crank the warp up, and the burn still
/// fires at the right moment instead of overshooting it.
///
/// Prograde/normal, not a raw XYZ vector, because that is what is actually plannable from an ORBIT readout
/// with no 3D camera: prograde/retrograde changes the orbit's size and shape, normal tilts its plane -
/// exactly the two axes SolveHohmann needs (a coplanar transfer plus a folded-in plane change at arrival).
/// </summary>
public sealed class ManeuverPlan
{
    [System.Serializable]
    public struct Node
    {
        public double simSeconds;
        public float  progradeKmS;
        public float  normalKmS;

        public float TotalDvKmS => Mathf.Sqrt(progradeKmS * progradeKmS + normalKmS * normalKmS);
    }

    public struct Preview
    {
        public bool   valid;
        public float  periapsisAu, apoapsisAu, eccentricity, inclinationDeg;
        public double periodDays;
        /// <summary>Full elements, orientation included - NodePanel only ever needed the summary numbers above,
        /// but NavScreen's predicted-path drawing needs the whole conic to redraw it correctly.</summary>
        public OrbitElements elements;
    }

    private const int MaxQueue = 2;
    private readonly List<Node> _queue = new List<Node>();

    public bool   Armed          => _queue.Count > 0;
    public int    QueueCount     => _queue.Count;
    public Node?  Next           => _queue.Count > 0 ? _queue[0] : (Node?)null;
    public bool   WarpingToNode  { get; private set; }

    /// <summary>Clears the queue and cancels any warp-to-node in progress. Call on a system jump/new run,
    /// same as ShipOrbit.Reset() - a node planned in the old system means nothing in the new one.</summary>
    public IList<Node> QueueForSave => _queue;

    /// <summary>Save/load: the queued burns as they were saved.</summary>
    public void Restore(IList<Node> nodes)
    {
        Clear();
        _queue.AddRange(nodes);
    }

    public void Clear()
    {
        _queue.Clear();
        WarpingToNode = false;
    }

    public void SetSingle(double simSeconds, float progradeKmS, float normalKmS)
    {
        _queue.Clear();
        _queue.Add(new Node { simSeconds = simSeconds, progradeKmS = progradeKmS, normalKmS = normalKmS });
    }

    public void SetPair(Node departure, Node arrival)
    {
        _queue.Clear();
        _queue.Add(departure);
        if (_queue.Count < MaxQueue) _queue.Add(arrival);
    }

    public void StartWarpToNode()
    {
        if (_queue.Count > 0) WarpingToNode = true;
    }

    public void CancelWarp()
    {
        WarpingToNode = false;
    }

    /// <summary>Call every real frame (RunDriver). Fires any node whose time has arrived, and - while
    /// warping to a node - keeps stepping the warp rate down as the node approaches so the burn lands near
    /// the right moment instead of blowing straight through it.</summary>
    public void Tick()
    {
        if (Game.Clock == null || Game.State == null) return;
        double now = Game.Clock.SimSeconds;

        while (_queue.Count > 0 && _queue[0].simSeconds <= now)
        {
            Node node = _queue[0];
            _queue.RemoveAt(0);
            Execute(node, now);
        }

        if (!WarpingToNode) return;

        if (_queue.Count == 0)
        {
            Game.Clock.SetWarp(1f, GameClock.WarpUnit.Seconds);
            WarpingToNode = false;
        }
        else
        {
            double timeToNode = _queue[0].simSeconds - now;
            (float mult, GameClock.WarpUnit unit) = PickWarp(timeToNode);
            Game.Clock.SetWarp(mult, unit);
        }
    }

    /// <summary>Staged coarse-to-fine warp rate for the time remaining to the next node, on the same
    /// {1,10,30,60}x{s,m,h,d} grid the player uses manually (GameClock/StatusBar) - fast while the node is
    /// far off, settling to real time for the final approach so the burn fires close to on-target.</summary>
    private static (float, GameClock.WarpUnit) PickWarp(double secondsToNode)
    {
        if (secondsToNode > 30.0 * 86400.0) return (60f, GameClock.WarpUnit.Days);
        if (secondsToNode > 5.0  * 86400.0) return (10f, GameClock.WarpUnit.Days);
        if (secondsToNode > 1.0  * 86400.0) return (1f,  GameClock.WarpUnit.Days);
        if (secondsToNode > 6.0  * 3600.0)  return (60f, GameClock.WarpUnit.Hours);
        if (secondsToNode > 1.0  * 3600.0)  return (10f, GameClock.WarpUnit.Hours);
        if (secondsToNode > 20.0 * 60.0)    return (30f, GameClock.WarpUnit.Minutes);
        if (secondsToNode > 2.0  * 60.0)    return (10f, GameClock.WarpUnit.Minutes);
        if (secondsToNode > 30.0)           return (30f, GameClock.WarpUnit.Seconds);
        return (1f, GameClock.WarpUnit.Seconds);
    }

    private void Execute(Node node, double simSeconds)
    {
        SystemManager sm = SystemManager.Current;
        GameState state = Game.State;
        if (sm == null || sm.CurrentData == null || state == null) return;

        ShipOrbit orbit = state.ShipOrbit;
        // Burn directions come from the ship's own double-precision conic (bound or not), not from the
        // float display elements.
        if (!orbit.RelativeStateAt(simSeconds, out Vector3 relPos, out Vector3 relVel)) return;
        Vector3 progradeDir = ProgradeDir(relVel);
        Vector3 normalDir   = NormalDir(relPos, relVel);

        float pKmS = node.progradeKmS;
        float nKmS = node.normalKmS;
        float totalDv = Mathf.Sqrt(pKmS * pKmS + nKmS * nKmS);
        if (totalDv < 1e-6f) return;

        // Hydrogen-cost-clamped: a burn the tank can't fully afford is scaled down rather than refused
        // outright, so a plan armed slightly beyond what's left still does as much of the job as it can.
        float cost = state.BurnCost(totalDv);
        if (cost > state.Hydrogen + 1e-4f && cost > 0f)
        {
            float scale = Mathf.Clamp01(state.Hydrogen / cost);
            pKmS *= scale;
            nKmS *= scale;
        }
        if (Mathf.Abs(pKmS) < 1e-6f && Mathf.Abs(nKmS) < 1e-6f) return;

        Vector3 dvKmS = progradeDir * pKmS + normalDir * nKmS;
        float actualCost = state.BurnCost(Mathf.Sqrt(pKmS * pKmS + nKmS * nKmS));
        state.ConsumeHydrogen(actualCost);

        state.Ship.vx += dvKmS.x;
        state.Ship.vy += dvKmS.y;
        state.Ship.vz += dvKmS.z;

        orbit.Resolve(sm.CurrentData, state.Ship, simSeconds);
    }

    /// <summary>Non-destructive preview of the orbit a burn of this size, at this time, would produce -
    /// assumes the ship's primary doesn't change between now and simSeconds (a fine approximation for any
    /// burn that stays within the current sphere of influence, which covers ordinary node planning and
    /// Hohmann transfers between bodies sharing the same primary).</summary>
    public static Preview PreviewNode(double simSeconds, float progradeKmS, float normalKmS)
    {
        ShipOrbit orbit = Game.State != null ? Game.State.ShipOrbit : null;
        if (orbit == null) return default;
        if (!orbit.RelativeStateAt(simSeconds, out Vector3 relPos, out Vector3 relVel)) return default;
        Vector3 progradeDir = ProgradeDir(relVel);
        Vector3 normalDir   = NormalDir(relPos, relVel);

        Vector3 dvGameUnitsPerSec = (progradeDir * progradeKmS + normalDir * normalKmS) / (float)ShipState.KmPerUnit;
        Vector3 newRelVel = relVel + dvGameUnitsPerSec;

        (OrbitElements elements, double _) = OrbitalMechanics.StateToElements(relPos, newRelVel, orbit.Mu, simSeconds);
        if (elements.orbitalPeriod <= 0.0) return default;

        float gu = GameConstants.GAME_UNITS_PER_UA;
        return new Preview
        {
            valid          = true,
            periapsisAu    = elements.semiMajorAxis * (1f - elements.eccentricity) / gu,
            apoapsisAu     = elements.semiMajorAxis * (1f + elements.eccentricity) / gu,
            eccentricity   = elements.eccentricity,
            inclinationDeg = elements.inclination,
            periodDays     = elements.orbitalPeriod / 86400.0,
            elements       = elements
        };
    }

    private static Vector3 ProgradeDir(Vector3 relVel)
        => relVel.sqrMagnitude > 1e-12f ? relVel.normalized : Vector3.forward;

    private static Vector3 NormalDir(Vector3 relPos, Vector3 relVel)
    {
        Vector3 h = Vector3.Cross(relPos, relVel);
        return h.sqrMagnitude > 1e-12f ? h.normalized : Vector3.up;
    }

    /// <summary>Core Hohmann-geometry numbers shared by SolveHohmann (which turns them into burns) and
    /// ComputeTransferWindow (which only needs the transfer time and delta-v magnitudes for a preview).
    /// r1, r2, mu in the same units in/out; angles in radians, dv in the same speed unit as mu implies
    /// (game-units/simSecond here, since that's what ShipOrbit.Mu is in).</summary>
    private struct HohmannGeometry
    {
        public double dv1, dv2, transferTimeSeconds;
    }

    private static HohmannGeometry SolveHohmannGeometry(double mu, double r1, double r2)
    {
        double aT = (r1 + r2) / 2.0;
        double v1Circ = Math.Sqrt(mu / r1);
        double v2Circ = Math.Sqrt(mu / r2);
        double vTransferAtR1 = Math.Sqrt(mu * (2.0 / r1 - 1.0 / aT));
        double vTransferAtR2 = Math.Sqrt(mu * (2.0 / r2 - 1.0 / aT));
        return new HohmannGeometry
        {
            dv1 = vTransferAtR1 - v1Circ,               // departure: speed up (or slow, if r2 < r1) onto the transfer ellipse
            dv2 = v2Circ - vTransferAtR2,                // arrival: match the target's circular speed
            transferTimeSeconds = Math.PI * Math.Sqrt(aT * aT * aT / mu)
        };
    }

    /// <summary>
    /// Basic two-burn coplanar-approximation Hohmann transfer from the ship's current orbit (treated as
    /// circular at its current semi-major axis) to a target's orbit around the SAME primary. `target` is an
    /// element set the CALLER already has - deliberately not looked up from NodeData/the catalogue here:
    /// the only legitimate source for another body's orbit is what the player has actually measured
    /// (OrbitFit.TryFit, from a track's own range estimate). A bad fit makes a bad burn; that's the game.
    /// Not an optimal solver: it doesn't time the plane-change component to the true line of nodes, just
    /// folds a rough plane-change delta-v into the arrival burn's normal axis. departureSimSeconds is when
    /// the departure burn fires - pass "now" for an immediate (phase-blind) transfer, or
    /// ComputeTransferWindow's DepartSimSeconds to wait for the window that actually meets the target.
    /// </summary>
    public static bool SolveHohmann(in OrbitElements target, double departureSimSeconds, out Node departure, out Node arrival)
    {
        departure = default;
        arrival   = default;

        ShipOrbit orbit = Game.State != null ? Game.State.ShipOrbit : null;
        if (orbit == null || !orbit.Valid || target.orbitalPeriod <= 0.0) return false;

        double mu = orbit.Mu;
        float  r1 = orbit.Elements.semiMajorAxis;
        float  r2 = target.semiMajorAxis;
        if (r1 <= 0f || r2 <= 0f || mu <= 0.0) return false;

        HohmannGeometry g = SolveHohmannGeometry(mu, r1, r2);

        // Rough plane-change, folded into the arrival burn rather than timed to the true line of nodes.
        double v2Circ = Math.Sqrt(mu / r2);
        float inclDeltaDeg = Mathf.Abs(target.inclination - orbit.Elements.inclination);
        double dvPlane = 2.0 * v2Circ * Math.Sin(inclDeltaDeg * Mathf.Deg2Rad / 2.0);

        float kmPerUnit = (float)ShipState.KmPerUnit; // game-units/simSecond -> km/s
        departure = new Node
        {
            simSeconds  = departureSimSeconds,
            progradeKmS = (float)(g.dv1 * kmPerUnit),
            normalKmS   = 0f
        };
        arrival = new Node
        {
            simSeconds  = departureSimSeconds + g.transferTimeSeconds,
            progradeKmS = (float)(g.dv2 * kmPerUnit),
            normalKmS   = (float)(dvPlane * kmPerUnit)
        };
        return true;
    }

    /// <summary>Preview of when the NEXT Hohmann window to `target` opens - the phase angle the target needs
    /// to lead (or trail) the ship by at departure so it's actually AT the rendezvous point when the transfer
    /// ends, not just "some point on its orbit". SolveHohmann itself is phase-blind (it'll compute correct
    /// burn sizes for a transfer starting right now, but the target usually won't be there yet); this is what
    /// lets a caller wait for DepartSimSeconds before arming those burns. Same coplanar-circular assumptions
    /// as SolveHohmann, and the same "target is an already-fitted OrbitElements, never a NodeData lookup".</summary>
    public struct TransferWindow
    {
        public bool   valid;
        public double phaseNowDeg, phaseIdealDeg;   // target's lead angle over the ship, now vs. at departure
        public double waitSeconds;                  // 0 if the window is already open
        public double departSimSeconds;
        public double transferTimeSeconds;
        public float  departureDvKmS, arrivalDvKmS;
        public float  TotalDvKmS => departureDvKmS + arrivalDvKmS;
    }

    public static TransferWindow ComputeTransferWindow(in OrbitElements target, double nowSimSeconds)
    {
        TransferWindow w = default;

        ShipOrbit orbit = Game.State != null ? Game.State.ShipOrbit : null;
        if (orbit == null || !orbit.Valid || target.orbitalPeriod <= 0.0) return w;
        if (!orbit.RelativeStateAt(nowSimSeconds, out Vector3 shipRel, out Vector3 _)) return w;

        double mu = orbit.Mu;
        float  r1 = orbit.Elements.semiMajorAxis;
        float  r2 = target.semiMajorAxis;
        if (r1 <= 0f || r2 <= 0f || mu <= 0.0) return w;

        HohmannGeometry g = SolveHohmannGeometry(mu, r1, r2);

        // Mean motion of each body around the shared primary (rad/s) - the same circular approximation the
        // burn sizes already use.
        double r1d = r1;
        double n1 = Math.Sqrt(mu / (r1d * r1d * r1d));
        double n2 = target.orbitalPeriod > 0.0 ? 2.0 * Math.PI / target.orbitalPeriod : 0.0;

        Vector3 targetOffsetNow = KeplerOrbit.OffsetAt(target, nowSimSeconds);
        double thetaShip = Math.Atan2(shipRel.z, shipRel.x);
        double thetaTarget = Math.Atan2(targetOffsetNow.z, targetOffsetNow.x);
        double gammaNow = Mod2Pi(thetaTarget - thetaShip);          // target's current lead over the ship
        double gammaIdeal = Mod2Pi(Math.PI - n2 * g.transferTimeSeconds); // lead needed AT departure

        // gamma changes at (n2 - n1) rad/s; find the smallest t >= 0 where it next equals gammaIdeal.
        double omega = n2 - n1;
        double waitSeconds;
        if (Math.Abs(omega) < 1e-15) waitSeconds = 0.0; // r1 == r2: no real transfer, but don't divide by zero
        else if (omega > 0.0) waitSeconds = Mod2Pi(gammaIdeal - gammaNow) / omega;
        else waitSeconds = Mod2Pi(gammaNow - gammaIdeal) / -omega;

        // Same rough plane-change SolveHohmann folds into the arrival burn - included here too so the
        // previewed Δv matches what CREATE NODES will actually arm.
        double v2Circ = Math.Sqrt(mu / r2);
        float inclDeltaDeg = Mathf.Abs(target.inclination - orbit.Elements.inclination);
        double dvPlane = 2.0 * v2Circ * Math.Sin(inclDeltaDeg * Mathf.Deg2Rad / 2.0);
        double arrivalDv = Math.Sqrt(g.dv2 * g.dv2 + dvPlane * dvPlane);

        float kmPerUnit = (float)ShipState.KmPerUnit;
        w.valid = true;
        w.phaseNowDeg = gammaNow * Mathf.Rad2Deg;
        w.phaseIdealDeg = gammaIdeal * Mathf.Rad2Deg;
        w.waitSeconds = waitSeconds;
        w.departSimSeconds = nowSimSeconds + waitSeconds;
        w.transferTimeSeconds = g.transferTimeSeconds;
        w.departureDvKmS = Mathf.Abs((float)(g.dv1 * kmPerUnit));
        w.arrivalDvKmS = (float)(arrivalDv * kmPerUnit);
        return w;
    }

    /// <summary>x mod 2*pi, into [0, 2*pi).</summary>
    private static double Mod2Pi(double x)
    {
        const double twoPi = 2.0 * Math.PI;
        double m = x % twoPi;
        return m < 0.0 ? m + twoPi : m;
    }
}
