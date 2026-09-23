using UnityEngine;

/// <summary>
/// The ship's actual orbital state: which body currently dominates it gravitationally (patched conics,
/// re-resolved every tick) and its osculating Keplerian elements around that body. Where ShipState is just
/// the raw motion state (position/velocity), this is what makes that motion real Newtonian free-fall
/// instead of a straight-line coast - the ship is always "in orbit" around whatever it's closest to,
/// exactly like every other body in the system, and a burn just changes which conic it's currently on.
///
/// Advance() propagates analytically from the currently-known orbit (exact at any warp, no drift, same
/// principle as KeplerOrbit for the scripted bodies) then re-resolves fresh elements from the resulting
/// state vector - so an instantaneous burn (ApplyDeltaV) or a change of primary (crossing into or out of a
/// planet's sphere of influence) is picked up automatically on the very next tick, at most one frame late.
/// </summary>
public sealed class ShipOrbit
{
    public bool   Valid        { get; private set; }
    public int    PrimaryIndex { get; private set; } = -1;
    public string PrimaryName  { get; private set; } = "";
    public double Mu           { get; private set; }
    public OrbitElements Elements { get; private set; }
    public double CaptureSimSeconds { get; private set; }
    public double TrueAnomalyDeg    { get; private set; }

    public float PeriapsisGame => Elements.semiMajorAxis * (1f - Elements.eccentricity);
    public float ApoapsisGame  => Elements.semiMajorAxis * (1f + Elements.eccentricity);

    private double _lastSimSeconds;
    private bool   _hasLast;

    /// <summary>Forgets the current orbit; the next Advance() resolves fresh instead of propagating -
    /// call this right after teleporting the ship (a system jump), since the old elements describe a
    /// system that no longer applies.</summary>
    public void Reset()
    {
        Valid = false;
        PrimaryIndex = -1;
        PrimaryName = "";
        _hasLast = false;
    }

    /// <summary>Call every real frame (SystemManager.MoveShip), after any burns for that frame have
    /// already been applied to ship.vx/vy/vz. Propagates the ship along its current conic up to
    /// simSecondsNow, writes the result into the ship's position/velocity, then re-resolves fresh
    /// elements so the next call (or a UI readout right now) has the current orbit.</summary>
    public void Advance(SystemData sys, ShipState ship, double simSecondsNow)
    {
        if (sys == null) { Valid = false; _hasLast = false; return; }

        if (Valid)
        {
            Vector3 primaryPos = OrbitalMechanics.NodePosition(sys, PrimaryIndex, simSecondsNow);
            Vector3 primaryVel = OrbitalMechanics.NodeVelocity(sys, PrimaryIndex, simSecondsNow);
            Vector3 relPos = KeplerOrbit.OffsetAt(Elements, simSecondsNow);
            Vector3 relVel = OrbitalMechanics.VelocityAt(Elements, simSecondsNow);

            Vector3 worldPos    = primaryPos + relPos;
            Vector3 worldVelKmS = (primaryVel + relVel) * (float)ShipState.KmPerUnit;

            ship.x = worldPos.x; ship.y = worldPos.y; ship.z = worldPos.z;
            ship.vx = worldVelKmS.x; ship.vy = worldVelKmS.y; ship.vz = worldVelKmS.z;
        }
        else if (_hasLast)
        {
            // Degenerate osculating orbit (purely radial fall, or an escape trajectory beyond this
            // model's fidelity) - fall back to a plain straight-line coast for this one tick rather than
            // freezing the ship in place.
            ship.Advance(simSecondsNow - _lastSimSeconds);
        }

        _lastSimSeconds = simSecondsNow;
        _hasLast = true;
        Resolve(sys, ship, simSecondsNow);
    }

    /// <summary>Recomputes the current osculating orbit fresh from the ship's state vector.</summary>
    public void Resolve(SystemData sys, ShipState ship, double simSeconds)
    {
        if (sys == null || sys.nodes.Count == 0) { Valid = false; return; }

        Vector3 shipPos = new Vector3((float)ship.x, (float)ship.y, (float)ship.z);
        float kmPerUnit = (float)ShipState.KmPerUnit;
        Vector3 shipVelGamePerSec = new Vector3((float)(ship.vx / kmPerUnit), (float)(ship.vy / kmPerUnit), (float)(ship.vz / kmPerUnit));

        int primary = OrbitalMechanics.FindPrimary(sys, shipPos, shipVelGamePerSec, simSeconds);
        if (primary < 0) { Valid = false; return; }

        Vector3 primaryPos = OrbitalMechanics.NodePosition(sys, primary, simSeconds);
        Vector3 primaryVel = OrbitalMechanics.NodeVelocity(sys, primary, simSeconds);
        Vector3 relPos = shipPos - primaryPos;
        Vector3 relVel = shipVelGamePerSec - primaryVel;

        double mu = OrbitalMechanics.Mu(OrbitalMechanics.MassOf(sys, primary));
        (OrbitElements elements, double nuDeg) = OrbitalMechanics.StateToElements(relPos, relVel, mu, simSeconds);

        PrimaryIndex = primary;
        PrimaryName  = sys.nodes[primary].name;
        Mu = mu;
        Elements = elements;
        CaptureSimSeconds = simSeconds;
        TrueAnomalyDeg = nuDeg;
        Valid = elements.orbitalPeriod > 0.0;
    }
}
