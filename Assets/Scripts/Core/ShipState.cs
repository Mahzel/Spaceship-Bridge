using System;

/// <summary>
/// The probe's motion: position (game units, relative to the system origin), velocity (km/s) and heading.
/// Plain C#, kept in double precision because per-frame movements are far below float resolution at 50 AU.
///
/// Discrete burns (ApplyDeltaV) change velocity along the current heading; heading is a world yaw in
/// degrees, 0 = +Z, increasing clockwise seen from above (same as bearings) and never affects motion on
/// its own. Real gravity now governs how the ship actually moves: see ShipOrbit, which advances position
/// and velocity every frame along the ship's true osculating orbit around whatever currently dominates it.
/// Advance() below is only ShipOrbit's fallback for the rare degenerate case (a purely radial trajectory,
/// or an escape orbit outside its model) - normal flight never calls it directly.
/// </summary>
public sealed class ShipState
{
    /// <summary>Kilometres per game unit (1 AU = 100 units).</summary>
    public const double KmPerUnit = 1.495978707e8 / 100.0;

    public double x, y, z;               // game units
    public double vx, vy, vz;            // km/s
    public double headingDeg;            // world yaw, [0, 360)

    public double Speed { get { return Math.Sqrt(vx * vx + vy * vy + vz * vz); } }

    /// <summary>Bearing of the velocity vector (horizontal), degrees [0, 360).</summary>
    public double VelocityBearingDeg
    {
        get
        {
            double b = Math.Atan2(vx, vz) * 180.0 / Math.PI;
            return b < 0 ? b + 360.0 : b;
        }
    }

    /// <summary>Places the probe at rest.</summary>
    public void Place(double px, double py, double pz, double heading)
    {
        x = px; y = py; z = pz;
        vx = vy = vz = 0.0;
        headingDeg = Wrap(heading);
    }

    public void Advance(double simSeconds)
    {
        if (simSeconds <= 0.0) return;
        double k = simSeconds / KmPerUnit;
        x += vx * k;
        y += vy * k;
        z += vz * k;
    }

    public void Turn(double deltaDeg)
    {
        headingDeg = Wrap(headingDeg + deltaDeg);
    }

    /// <summary>Instant velocity change along the heading (negative = retro burn).</summary>
    public void ApplyDeltaV(double dvKmS)
    {
        double h = headingDeg * Math.PI / 180.0;
        vx += dvKmS * Math.Sin(h);
        vz += dvKmS * Math.Cos(h);
    }

    private static double Wrap(double a)
    {
        a %= 360.0;
        return a < 0 ? a + 360.0 : a;
    }
}
