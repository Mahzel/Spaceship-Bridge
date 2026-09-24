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
        return Rotate(o, r * Math.Cos(nu), r * Math.Sin(nu));
    }

    /// <summary>Offset from the focus at a given true anomaly (radians) - a pure function of the ellipse's shape,
    /// not of time. Lets a map trace the whole orbit by sampling anomaly directly (so periapsis can be sampled
    /// as densely as apoapsis) instead of walking simulated time. Mirrors ShipOrbit.OffsetAtTrueAnomaly, for the
    /// catalogue layer's own bodies.</summary>
    public static Vector3 OffsetAtTrueAnomaly(in OrbitElements o, double nu)
    {
        double e = Math.Min(Math.Max(o.eccentricity, 0.0), 0.99);
        double p = o.semiMajorAxis * (1.0 - e * e);
        double r = p / (1.0 + e * Math.Cos(nu));
        return Rotate(o, r * Math.Cos(nu), r * Math.Sin(nu));
    }

    // Perifocal (xOrb, yOrb) -> world rotation, shared by OffsetAt/OffsetAtTrueAnomaly/StateAt.
    private static Vector3 Rotate(in OrbitElements o, double xOrb, double yOrb)
    {
        double cosO = Math.Cos(o.longitudeAscNode  * Deg2Rad), sinO = Math.Sin(o.longitudeAscNode  * Deg2Rad);
        double cosI = Math.Cos(o.inclination       * Deg2Rad), sinI = Math.Sin(o.inclination       * Deg2Rad);
        double cosW = Math.Cos(o.argumentPeriapsis * Deg2Rad), sinW = Math.Sin(o.argumentPeriapsis * Deg2Rad);

        double x = (cosO * cosW - sinO * sinW * cosI) * xOrb
                 + (-cosO * sinW - sinO * cosW * cosI) * yOrb;
        double y = (sinI * sinW) * xOrb
                 + (sinI * cosW) * yOrb;
        double z = (sinO * cosW + cosO * sinW * cosI) * xOrb
                 + (-sinO * sinW + cosO * cosW * cosI) * yOrb;

        return new Vector3((float)x, (float)y, (float)z);
    }

    /// <summary>
    /// Offset from the focus AND velocity (game units per simulated second), in double precision, analytic.
    /// Same orbit, same 0.99 eccentricity clamp and same perifocal-to-world rotation as OffsetAt, so the
    /// position matches what's rendered. Use this (not a finite difference of OffsetAt) wherever a velocity
    /// feeds into physics.
    /// </summary>
    public static void StateAt(in OrbitElements o, double simSeconds, out Vec3d pos, out Vec3d vel)
    {
        double e  = Math.Min(Math.Max(o.eccentricity, 0.0), 0.99);
        double a  = o.semiMajorAxis;
        double M  = MeanAnomalyDeg(o, simSeconds) * Deg2Rad;
        double E  = SolveEccentricAnomaly(M, e);
        double cosE = Math.Cos(E), sinE = Math.Sin(E);
        double sq = Math.Sqrt(1.0 - e * e);

        double xOrb = a * (cosE - e);
        double yOrb = a * sq * sinE;
        double n    = o.orbitalPeriod > 0.0 ? 2.0 * Math.PI / o.orbitalPeriod : 0.0; // rad per sim second
        double Edot = n / (1.0 - e * cosE);
        double vxOrb = -a * sinE * Edot;
        double vyOrb =  a * sq * cosE * Edot;

        double cosO = Math.Cos(o.longitudeAscNode  * Deg2Rad), sinO = Math.Sin(o.longitudeAscNode  * Deg2Rad);
        double cosI = Math.Cos(o.inclination       * Deg2Rad), sinI = Math.Sin(o.inclination       * Deg2Rad);
        double cosW = Math.Cos(o.argumentPeriapsis * Deg2Rad), sinW = Math.Sin(o.argumentPeriapsis * Deg2Rad);

        double m00 = cosO * cosW - sinO * sinW * cosI, m01 = -cosO * sinW - sinO * cosW * cosI;
        double m10 = sinI * sinW,                      m11 = sinI * cosW;
        double m20 = sinO * cosW + cosO * sinW * cosI, m21 = -sinO * sinW + cosO * cosW * cosI;

        pos = new Vec3d(m00 * xOrb + m01 * yOrb, m10 * xOrb + m11 * yOrb, m20 * xOrb + m21 * yOrb);
        vel = new Vec3d(m00 * vxOrb + m01 * vyOrb, m10 * vxOrb + m11 * vyOrb, m20 * vxOrb + m21 * vyOrb);
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
