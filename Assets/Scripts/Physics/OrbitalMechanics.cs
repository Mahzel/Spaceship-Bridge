using System;
using UnityEngine;

/// <summary>
/// Turns the ship's raw state vector (position, velocity) into a real two-body orbit and back again.
/// Everything here is pure math, double precision internally, no MonoBehaviour and no per-frame state of
/// its own - ShipOrbit is what remembers anything between calls.
///
/// Units: positions in game units (100/AU, same as everywhere else), velocities in game units per
/// SIMULATED second (NOT km/s - convert at the boundary via ShipState.KmPerUnit), mu (gravitational
/// parameter) in game-units^3/simSecond^2.
///
/// Mass scale: every body's `mass` field is in solar masses (see GameConstant.cs / SystemFactory, which
/// already generates orbital periods via the real AU-year-solarMass form of Kepler's third law,
/// T_years = sqrt(a_AU^3 / M_solar) - i.e. mu_sun (AU^3/yr^2) = 4*pi^2 by definition of that unit system.
/// Mu() below just carries that same constant into game-units/simSeconds so a ship's osculating elements,
/// once fed back through the existing KeplerOrbit.OffsetAt, land on exactly the same numbers a scripted
/// body's orbit would.
/// </summary>
public static class OrbitalMechanics
{
    private const double TwoPiSquared = 4.0 * Math.PI * Math.PI;

    /// <summary>Gravitational parameter of a body of this mass (solar masses), in game-units^3/simSecond^2.</summary>
    public static double Mu(float massSolar)
    {
        double gu = GameConstants.GAME_UNITS_PER_UA;
        double secPerYear = GameConstants.SECONDS_PER_YEAR;
        return TwoPiSquared * gu * gu * gu * massSolar / (secPerYear * secPerYear);
    }

    /// <summary>A node's own mass for gravity purposes: its own mass for a star or planet, or the summed
    /// mass of every star descending from it for a barycenter (nested planets around a binary are rare
    /// enough here that lumping the whole system's stellar mass together is a fine approximation).</summary>
    public static float MassOf(SystemData sys, int index)
    {
        if (sys == null || index < 0 || index >= sys.nodes.Count) return 0f;
        NodeData n = sys.nodes[index];
        if (n.kind == NodeKind.Star || n.kind == NodeKind.Planet) return n.mass;

        float total = 0f;
        for (int i = 0; i < sys.nodes.Count; i++)
            if (sys.nodes[i].kind == NodeKind.Star && IsDescendantOf(sys, i, index)) total += sys.nodes[i].mass;
        return total;
    }

    private static bool IsDescendantOf(SystemData sys, int node, int ancestor)
    {
        for (int i = sys.nodes[node].parent; i >= 0; i = sys.nodes[i].parent)
            if (i == ancestor) return true;
        return false;
    }

    public static Vector3 NodePosition(SystemData sys, int index, double simSeconds) => sys.PositionOf(index, simSeconds);

    /// <summary>Central-difference velocity of a scripted body, game-units per simSecond. The bodies move on
    /// closed-form (non-integrated) orbits, so a small epsilon here costs nothing in drift - it's just a
    /// cheap way to get a body's instantaneous velocity without hand-differentiating KeplerOrbit's rotation
    /// matrix.</summary>
    public static Vector3 NodeVelocity(SystemData sys, int index, double simSeconds)
    {
        const double eps = 60.0;
        Vector3 p1 = sys.PositionOf(index, simSeconds - eps);
        Vector3 p2 = sys.PositionOf(index, simSeconds + eps);
        return (p2 - p1) / (float)(2.0 * eps);
    }

    /// <summary>Same central-difference trick for a ship's own osculating orbit.</summary>
    public static Vector3 VelocityAt(in OrbitElements el, double simSeconds)
    {
        const double eps = 60.0;
        Vector3 p1 = KeplerOrbit.OffsetAt(el, simSeconds - eps);
        Vector3 p2 = KeplerOrbit.OffsetAt(el, simSeconds + eps);
        return (p2 - p1) / (float)(2.0 * eps);
    }

    /// <summary>Which body currently dominates the ship gravitationally: the innermost planet whose Hill
    /// sphere contains it AND it's actually moving slowly enough relative to that planet to be bound to
    /// it - otherwise the nearest star. Patched-conics, re-checked every call - no nested moons in this
    /// game yet, so at most one planet match is expected in practice.
    ///
    /// The boundedness check matters a lot in practice: with masses now physically scaled (see
    /// GameConstant.cs), a planet's Hill sphere is properly small, but a ship cruising at "orbiting the
    /// star" speed (several km/s) is almost always moving far faster than any planet's own escape
    /// velocity - geometrically inside the Hill sphere doesn't mean gravitationally captured. Handing it
    /// primary status on containment alone made every close flyby look like a capture into a wildly
    /// hyperbolic "orbit" around a body the ship was really just passing, which is what made orbits look
    /// unstable. Only accept a planet as primary if the ship would actually stay near it.</summary>
    public static int FindPrimary(SystemData sys, Vector3 shipPosGame, Vector3 shipVelGamePerSec, double simSeconds)
    {
        if (sys == null || sys.nodes.Count == 0) return -1;

        var positions = new Vector3[sys.nodes.Count];
        sys.EvaluatePositions(simSeconds, positions);

        int best = -1;
        float bestHill = float.MaxValue;
        for (int i = 0; i < sys.nodes.Count; i++)
        {
            NodeData n = sys.nodes[i];
            if (n.kind != NodeKind.Planet || !n.hasOrbit) continue;

            float parentMass = MassOf(sys, n.parent);
            if (parentMass <= 0f || n.mass <= 0f) continue;

            // The Hill-sphere formula assumes the secondary is much lighter than what it orbits. Physically
            // scaled planet masses keep this true by construction now, but stay defensive in case a future
            // generator change (or a hand-authored system) violates it - a "planet" at or above its star's
            // own mass isn't a planet with a sphere of influence any more, it's a stellar-mass companion.
            if (n.mass >= parentMass) continue;

            float hill = n.orbit.semiMajorAxis * (1f - n.orbit.eccentricity) * Mathf.Pow(n.mass / (3f * parentMass), 1f / 3f);
            hill = Mathf.Min(hill, 0.4f * n.orbit.semiMajorAxis); // real stable Hill spheres never get close to this
            float dist = Vector3.Distance(shipPosGame, positions[i]);
            if (dist > hill || hill >= bestHill) continue;

            Vector3 planetVel = NodeVelocity(sys, i, simSeconds);
            float relSpeed = (shipVelGamePerSec - planetVel).magnitude;
            double mu = Mu(n.mass);
            float escapeSpeed = dist > 1e-6f ? (float)Math.Sqrt(2.0 * mu / dist) : 0f;
            if (relSpeed >= escapeSpeed) continue; // fast flyby, not a capture - leave the star as primary

            best = i;
            bestHill = hill;
        }
        if (best >= 0) return best;

        return NearestStar(sys, shipPosGame, positions);
    }

    /// <summary>The nearest star by current position - used both as FindPrimary's fallback and directly
    /// for placing the ship in its initial parking orbit (which is always around a star, never a planet).</summary>
    public static int NearestStar(SystemData sys, Vector3 shipPosGame, double simSeconds)
    {
        if (sys == null || sys.nodes.Count == 0) return -1;
        var positions = new Vector3[sys.nodes.Count];
        sys.EvaluatePositions(simSeconds, positions);
        return NearestStar(sys, shipPosGame, positions);
    }

    private static int NearestStar(SystemData sys, Vector3 shipPosGame, Vector3[] positions)
    {
        int nearestStar = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < sys.nodes.Count; i++)
        {
            if (sys.nodes[i].kind != NodeKind.Star) continue;
            float d = Vector3.Distance(shipPosGame, positions[i]);
            if (d < bestDist) { bestDist = d; nearestStar = i; }
        }
        return nearestStar;
    }

    /// <summary>A circular, prograde velocity at this position around the nearest star - used to place the
    /// ship in a stable parking orbit on arrival instead of dropping it at rest (which, under real gravity,
    /// would just be a straight fall into the star). Always a star, never a planet: arrival points are
    /// generated outside every planet's orbit, so a stellar parking orbit is always what's meant here.
    /// Returns world-frame km/s, ready to assign straight to ShipState.vx/vy/vz.</summary>
    public static Vector3 CircularVelocityKmS(SystemData sys, Vector3 shipPosGame, double simSeconds)
    {
        int primary = NearestStar(sys, shipPosGame, simSeconds);
        if (primary < 0) return Vector3.zero;

        Vector3 primaryPos = NodePosition(sys, primary, simSeconds);
        Vector3 primaryVel = NodeVelocity(sys, primary, simSeconds);
        Vector3 rel = shipPosGame - primaryPos;
        float r = rel.magnitude;
        if (r < 1e-4f) return Vector3.zero;

        double mu = Mu(MassOf(sys, primary));
        float speed = (float)Math.Sqrt(mu / r);
        Vector3 tangent = Vector3.Cross(Vector3.up, rel).normalized * speed;
        Vector3 worldVelGamePerSec = primaryVel + tangent;
        return worldVelGamePerSec * (float)ShipState.KmPerUnit;
    }

    /// <summary>
    /// Classic rv2coe (Vallado/Curtis), adapted to this engine's axis convention. KeplerOrbit.OffsetAt's
    /// perifocal-to-world matrix is exactly the standard one with standard(X,Y,Z) = game(x,z,y) - i.e. the
    /// game's Y (up) plays the role of the orbit-normal reference axis - so this maps r/v into that frame,
    /// runs the textbook algorithm, and the resulting elements drop straight into KeplerOrbit.OffsetAt with
    /// no further conversion.
    /// meanAnomalyAtEpoch is set so that KeplerOrbit's own convention (M(t) = meanAnomalyAtEpoch +
    /// 360*t/period, evaluated at the ABSOLUTE simSeconds) reproduces the captured state at simSeconds.
    /// </summary>
    public static (OrbitElements elements, double trueAnomalyDeg) StateToElements(
        Vector3 rGame, Vector3 vGamePerSec, double mu, double simSeconds)
    {
        double rx = rGame.x, ry = rGame.z, rz = rGame.y;
        double vx = vGamePerSec.x, vy = vGamePerSec.z, vz = vGamePerSec.y;

        double rMag = Math.Sqrt(rx * rx + ry * ry + rz * rz);
        double vMag = Math.Sqrt(vx * vx + vy * vy + vz * vz);
        if (rMag < 1e-9 || mu <= 0.0) return (default, 0.0);

        double vr = (rx * vx + ry * vy + rz * vz) / rMag;

        double hx = ry * vz - rz * vy;
        double hy = rz * vx - rx * vz;
        double hz = rx * vy - ry * vx;
        double hMag = Math.Sqrt(hx * hx + hy * hy + hz * hz);
        if (hMag < 1e-12) return (default, 0.0); // purely radial - no well-defined plane

        double inc = Math.Acos(Clamp(hz / hMag, -1.0, 1.0));

        double nx = -hy, ny = hx; // N = K x h, K = (0,0,1)
        double nMag = Math.Sqrt(nx * nx + ny * ny);

        double omegaCap; // longitude of ascending node (Omega)
        if (nMag > 1e-9)
        {
            omegaCap = Math.Acos(Clamp(nx / nMag, -1.0, 1.0));
            if (ny < 0.0) omegaCap = 2.0 * Math.PI - omegaCap;
        }
        else omegaCap = 0.0;

        double ef = vMag * vMag - mu / rMag;
        double ex = (ef * rx - rMag * vr * vx) / mu;
        double ey = (ef * ry - rMag * vr * vy) / mu;
        double ez = (ef * rz - rMag * vr * vz) / mu;
        double e = Math.Sqrt(ex * ex + ey * ey + ez * ez);

        double argPeri; // omega
        if (nMag > 1e-9 && e > 1e-8)
        {
            argPeri = Math.Acos(Clamp((nx * ex + ny * ey) / (nMag * e), -1.0, 1.0));
            if (ez < 0.0) argPeri = 2.0 * Math.PI - argPeri;
        }
        else argPeri = 0.0;

        double nu; // true anomaly
        if (e > 1e-8)
        {
            nu = Math.Acos(Clamp((ex * rx + ey * ry + ez * rz) / (e * rMag), -1.0, 1.0));
            if (vr < 0.0) nu = 2.0 * Math.PI - nu;
        }
        else
        {
            double refx = nMag > 1e-9 ? nx : 1.0;
            double refy = nMag > 1e-9 ? ny : 0.0;
            double refMag = nMag > 1e-9 ? nMag : 1.0;
            nu = Math.Acos(Clamp((refx * rx + refy * ry) / (refMag * rMag), -1.0, 1.0));
            if (rz < 0.0 && inc > 1e-6) nu = 2.0 * Math.PI - nu; // near-equatorial/circular: rough but harmless
        }

        double energy = vMag * vMag / 2.0 - mu / rMag;
        double a = Math.Abs(energy) > 1e-12 ? -mu / (2.0 * energy) : rMag;
        double eClamped = Math.Min(e, 0.99); // stay elliptical - matches KeplerOrbit's own clamp

        double E = 2.0 * Math.Atan2(Math.Sqrt(1.0 - eClamped) * Math.Sin(nu / 2.0), Math.Sqrt(1.0 + eClamped) * Math.Cos(nu / 2.0));
        double M = E - eClamped * Math.Sin(E);
        if (M < 0.0) M += 2.0 * Math.PI;

        double period = a > 0.0 ? 2.0 * Math.PI * Math.Sqrt(a * a * a / mu) : 0.0;

        double Mdeg = M * 180.0 / Math.PI;
        double meanAnomalyAtEpoch = period > 0.0 ? Wrap360(Mdeg - 360.0 * simSeconds / period) : Mdeg;

        var elements = new OrbitElements
        {
            semiMajorAxis      = (float)a,
            eccentricity       = (float)eClamped,
            inclination        = (float)(inc * 180.0 / Math.PI),
            longitudeAscNode   = (float)(omegaCap * 180.0 / Math.PI),
            argumentPeriapsis  = (float)(argPeri * 180.0 / Math.PI),
            meanAnomalyAtEpoch = (float)meanAnomalyAtEpoch,
            orbitalPeriod      = period
        };
        return (elements, nu * 180.0 / Math.PI);
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    private static double Wrap360(double deg)
    {
        deg %= 360.0;
        return deg < 0.0 ? deg + 360.0 : deg;
    }
}
