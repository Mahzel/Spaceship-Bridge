using System;
using System.Collections.Generic;

/// <summary>A star system in the galaxy catalog: an ID and a position (light-years, galactic plane).</summary>
public struct GalaxySystem
{
    public string id;
    public double x, z;

    public double DistanceTo(double px, double pz)
    {
        double dx = x - px, dz = z - pz;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}

/// <summary>
/// Minimal deterministic galaxy catalog: the plane is cut into square cells, each holding 0 to 2 systems
/// whose IDs and positions come only from hash(worldSeed, cell). Nothing is stored, so any region can be
/// queried at any time and always gives the same answer. The home system sits at the origin.
/// Pure C#, no Unity dependency beyond SystemFactory's ID helpers.
/// </summary>
public static class Galaxy
{
    public const double CellLy = 8.0;

    public static GalaxySystem Home(int worldSeed)
    {
        var s = new GalaxySystem();
        s.id = SystemFactory.HomeSystemID(worldSeed);
        return s;
    }

    /// <summary>Systems within radiusLy of (px, pz), nearest first, excluding excludeId. Home is included when in range.</summary>
    public static void Nearby(int worldSeed, double px, double pz, double radiusLy, string excludeId, List<GalaxySystem> result)
    {
        result.Clear();

        int c0x = (int)Math.Floor((px - radiusLy) / CellLy), c1x = (int)Math.Floor((px + radiusLy) / CellLy);
        int c0z = (int)Math.Floor((pz - radiusLy) / CellLy), c1z = (int)Math.Floor((pz + radiusLy) / CellLy);

        GalaxySystem home = Home(worldSeed);
        for (int cx = c0x; cx <= c1x; cx++)
        {
            for (int cz = c0z; cz <= c1z; cz++)
            {
                var rng = new SeededRandom(CellSeed(worldSeed, cx, cz));
                int count = rng.Range(0, 3);
                for (int k = 0; k < count; k++)
                {
                    var s = new GalaxySystem();
                    s.x = (cx + rng.Value) * CellLy;
                    s.z = (cz + rng.Value) * CellLy;
                    s.id = SystemFactory.GenerateSystemID(rng.Range(0, 640000));

                    double d = s.DistanceTo(px, pz);
                    if (d > radiusLy || d < 0.01) continue;
                    if (s.id == excludeId || s.id == home.id) continue;
                    result.Add(s);
                }
            }
        }

        if (home.id != excludeId && home.DistanceTo(px, pz) <= radiusLy) result.Add(home);

        result.Sort(delegate (GalaxySystem a, GalaxySystem b)
        {
            return a.DistanceTo(px, pz).CompareTo(b.DistanceTo(px, pz));
        });
    }

    private static int CellSeed(int worldSeed, int cx, int cz)
    {
        unchecked
        {
            int h = worldSeed;
            h = h * 486187739 + cx;
            h = h * 1664525 + cz * 1013904223 + 0x5BD1E995;
            return h;
        }
    }
}
