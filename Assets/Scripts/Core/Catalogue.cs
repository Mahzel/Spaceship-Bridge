using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The home catalogue: bodies known before any probe flew (the Solar System's Sun, planets, major moons, Ceres,
/// the main belt). They start in the Atlas (Seed), and the probe carries their ephemerides, so it can tell when
/// a track points at one of them (Match, once the track has a measured elevation): data recorded on a catalogued body is worth nothing, and the Data
/// panel says which known body it is. Unknown bodies (Haumea, Makemake, Eris, Sedna, and every other system)
/// are only identified the usual way, by the spectrometer.
/// Match reads the real body positions: that stands in for the ephemeris the probe would compute them from.
/// </summary>
public static class Catalogue
{
    /// <summary>A track within this angle of a catalogued body's predicted direction is "that body".</summary>
    public const float MatchDeg = 1.0f;

    /// <summary>A match needs a measured elevation this good (aged 1-sigma, degrees): on bearing alone, an
    /// unknown body in line with a known one (behind it, or far above the ecliptic) would be taken for it.</summary>
    public const float MaxElevationSigmaDeg = 2f;

    public static void Seed(Atlas atlas)
    {
        foreach (string name in SolSystem.CataloguedNames())
            atlas.AddCatalogued(SolSystem.Id, name);
    }

    /// <summary>Name of the catalogued body this track points at in the current system, or null.</summary>
    public static string Match(Track tr)
    {
        if (tr == null || Game.State == null || !tr.hasElevation) return null;
        double now = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
        float elSigma = TrackManager.AgedElevationSigma(tr, now);
        if (elSigma > MaxElevationSigmaDeg) return null;
        // A resolved disk (imager FIX) can be pointed at off-center: allow its apparent radius too.
        float limit = Mathf.Max(Mathf.Max(MatchDeg, 2f * elSigma), tr.angularRadiusDeg);
        SystemManager sm = SystemManager.Current;
        if (sm == null || sm.CurrentData == null) return null;
        string sys = sm.CurrentData.id;
        Transform ship = SensorSight.Ship();
        if (ship == null) return null;

        string best = null;
        float bestDeg = limit;
        List<CelestialBody> bodies = SensorSight.AllBodies();
        for (int i = 0; i < bodies.Count; i++)
        {
            CelestialBody b = bodies[i];
            if (Game.State.Atlas.FindCatalogued(sys, b.bodyName) == null) continue;
            float az = SensorSight.WorldAzimuth(b, ship);
            float d = SensorSight.AngularDistance(tr.bearing, tr.elevationDeg, az, SensorSight.WorldElevation(b, ship));
            if (d < bestDeg) { bestDeg = d; best = b.bodyName; }
        }
        return best;
    }
}
