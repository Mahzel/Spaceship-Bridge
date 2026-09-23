using System;
using UnityEngine;

/// <summary>
/// The six Keplerian elements of an orbit. Pure data, no scene dependency.
/// </summary>
[Serializable]
public struct OrbitElements
{
    public float  semiMajorAxis;      // a  — game units
    public float  eccentricity;       // e  — [0, 1[
    public float  inclination;        // i  — degrees
    public float  longitudeAscNode;   // Ω  — degrees
    public float  argumentPeriapsis;  // ω  — degrees
    public float  meanAnomalyAtEpoch; // M0 — degrees, at SimSeconds = 0
    public double orbitalPeriod;      // T  — simulated seconds
}

/// <summary>
/// Position as a pure function of simulated time: no per-frame integration, no drift, exact at any
/// warp factor, and it can answer "where will this body be at time T" (transit windows, radar aiming).
/// </summary>
public static class KeplerOrbit
{
    private const double Deg2Rad = Math.PI / 180.0;

    /// <summary>Mean anomaly in degrees [0, 360) at the given simulated time.</summary>
    public static double MeanAnomalyDeg(in OrbitElements o, double simSeconds)
    {
        double m = o.meanAnomalyAtEpoch;
        if (o.orbitalPeriod > 0.0) m += 360.0 * (simSeconds / o.orbitalPeriod);
        m %= 360.0;
        if (m < 0.0) m += 360.0;
        return m;
    }

    /// <summary>Offset from the orbit's focus, in game units, at the given simulated time.</summary>
    public static Vector3 OffsetAt(in OrbitElements o, double simSeconds)
    {
        double e  = Math.Min(Math.Max(o.eccentricity, 0.0), 0.99);
        double M  = MeanAnomalyDeg(o, simSeconds) * Deg2Rad;
        double E  = SolveEccentricAnomaly(M, e);
        double nu = 2.0 * Math.Atan2(Math.Sqrt(1.0 + e) * Math.Sin(E / 2.0),
                                     Math.Sqrt(1.0 - e) * Math.Cos(E / 2.0));

        double r    = o.semiMajorAxis * (1.0 - e * Math.Cos(E));
        double xOrb = r * Math.Cos(nu);
        double yOrb = r * Math.Sin(nu);

        double cosO = Math.Cos(o.longitudeAscNode  * Deg2Rad), sinO = Math.Sin(o.longitudeAscNode  * Deg2Rad);
        double cosI = Math.Cos(o.inclination       * Deg2Rad), sinI = Math.Sin(o.inclination       * Deg2Rad);
        double cosW = Math.Cos(o.argumentPeriapsis * Deg2Rad), sinW = Math.Sin(o.argumentPeriapsis * Deg2Rad);

        // Same perifocal -> world rotation as the previous OrbitalComponent.
        double x = (cosO * cosW - sinO * sinW * cosI) * xOrb
                 + (-cosO * sinW - sinO * cosW * cosI) * yOrb;
        double y = (sinI * sinW) * xOrb
                 + (sinI * cosW) * yOrb;
        double z = (sinO * cosW + cosO * sinW * cosI) * xOrb
                 + (-sinO * sinW + cosO * cosW * cosI) * yOrb;

        return new Vector3((float)x, (float)y, (float)z);
    }

    // Newton-Raphson on Kepler's equation, radians.
    private static double SolveEccentricAnomaly(double M, double e)
    {
        double E = e < 0.8 ? M : Math.PI;
        for (int i = 0; i < GameConstants.KEPLER_MAX_ITERATIONS; i++)
        {
            double delta = E - e * Math.Sin(E) - M;
            if (Math.Abs(delta) < GameConstants.KEPLER_CONVERGENCE) break;
            E -= delta / (1.0 - e * Math.Cos(E));
        }
        return E;
    }
}
