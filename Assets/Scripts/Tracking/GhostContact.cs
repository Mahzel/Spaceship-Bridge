using System;
using UnityEngine;

/// <summary>
/// Recalls a body the player already knows about (Atlas) as a fresh, aimable Tentative track in the CURRENT
/// run - "I know roughly where this should be, let me go check." Two very different confidence levels,
/// matching what's actually on file:
///  - CATALOGUED bodies (AtlasPanel's "RECALL" on a home-system body): a full orbit (NodeData.orbit) is known,
///    so the predicted position is the same analytic Kepler propagation ShipOrbit/OrbitFit already use
///    (OrbitalMechanics.NodeState) - exact, including elevation, not a guess.
///  - Any other survey entry (AtlasEntry.recordedRange): only ever a single old position+velocity fix, taken
///    from whatever the source DataRecord's own range estimate was (TMA or a prior radar fix) when it was
///    filed. Coasted forward at that same velocity to now - the identical straight-line model TMA itself
///    assumes - so it stays honest about being a guess: no elevation (never measured), and the seeded fix's
///    sigma widens with how long ago it was recorded.
///
/// Never touches SensorSight: player-facing code must not (see its own doc comment). The geometry here is
/// plain vector math against the ship's own known position (Game.State.Ship) - the same small formula
/// SensorSight.WorldAzimuth/WorldElevation use, just against a computed world position instead of a live
/// CelestialBody transform.
///
/// Either kind seeds a plain Tentative track (TrackManager.MarkBearing) with a range fix (ApplyRadarFix) and,
/// for the catalogued case, an elevation fix - but never ApplySupport: that would artificially "confirm" a
/// lock the player hasn't actually re-acquired themselves, which is exactly the omniscience this is supposed
/// to avoid. The track stays red/searching until the player's own sensors find something there for real.
/// </summary>
public static class GhostContact
{
    /// <summary>Seeds a track from a catalogued body's true current state - known data, not a guess. Null if
    /// there's nothing to aim from (no ship/clock) or the track list is already full.</summary>
    public static Track FromCatalogued(SystemData data, int nodeIndex, string displayName)
    {
        if (data == null || nodeIndex < 0 || nodeIndex >= data.nodes.Count) return null;
        if (!TryShipPos(out Vec3d shipPos)) return null;

        double now = Game.Clock.SimSeconds;
        OrbitalMechanics.NodeState(data, nodeIndex, now, out Vec3d pos, out Vec3d vel);
        Vec3d rel = pos - shipPos;
        ToBearingElevationRange(rel, out float bearingDeg, out float elevationDeg, out double rangeAu);

        // vel is game units/simSecond (OrbitalMechanics' own convention); ship velocity is km/s against that
        // same sim-second clock (see ManeuverPlan.Execute - burns add straight to Ship.vx/vy/vz with no extra
        // time-rate scaling) - so only the SPATIAL unit needs converting, by KmPerUnit, same as OrbitFit does.
        ShipState s = Game.State.Ship;
        Vec3d bodyVelKmS = vel * ShipState.KmPerUnit;
        Vec3d shipVelKmS = new Vec3d(s.vx, s.vy, s.vz);
        double radialRateKmS = rel.Magnitude > 1e-9
            ? Vec3d.Dot(bodyVelKmS - shipVelKmS, rel.Normalized) : 0.0;

        return Seed(displayName, now, bearingDeg, true, elevationDeg, rangeAu, rangeAu * 0.001, true, radialRateKmS);
    }

    /// <summary>Seeds a track by coasting an old survey entry's recorded fix forward to now. Null if the entry
    /// never got a usable range fix (recordedRange.valid false - a bearing-only claim, nothing to coast).</summary>
    public static Track FromEntry(AtlasEntry e, string displayName)
    {
        if (e == null || !e.recordedRange.valid || Game.Clock == null) return null;
        if (!TryShipPos(out Vec3d shipPos)) return null;

        double now = Game.Clock.SimSeconds;
        double dtSeconds = now - e.recordedTime;
        double kmPerUnit = ShipState.KmPerUnit;
        RangeEstimate re = e.recordedRange;
        double x = re.x + re.vxKmS / kmPerUnit * dtSeconds;
        double z = re.z + re.vzKmS / kmPerUnit * dtSeconds;

        Vec3d rel = new Vec3d(x - shipPos.x, 0.0, z - shipPos.z);
        ToBearingElevationRange(rel, out float bearingDeg, out float _, out double rangeAu);

        // A stale coast is honest about not knowing whether the target has since manoeuvred or (if genuinely
        // orbiting something) curved away from a straight line - the sigma grows with elapsed time on top of
        // whatever the original fix already carried.
        double staleFactor = 1.0 + Math.Abs(dtSeconds) / 86400.0 * 0.05; // +5%/day, uncapped
        double sigmaAu = (re.rangeSigma / GameConstants.GAME_UNITS_PER_UA) * staleFactor;

        return Seed(displayName, now, bearingDeg, false, 0f, rangeAu, sigmaAu, false, 0.0);
    }

    private static Track Seed(string displayName, double now, float bearingDeg, bool hasElevation, float elevationDeg,
                              double rangeAu, double rangeSigmaAu, bool hasRate, double rangeRateKmS)
    {
        if (Game.State == null) return null;
        TrackManager tm = Game.State.Tracks;
        int maxTracks = Game.State.Waterfall != null ? Game.State.Waterfall.spec.maxTracks : 4;

        if (!string.IsNullOrEmpty(displayName)) tm.PendingName = displayName;
        Track tr = tm.MarkBearing(now, bearingDeg, maxTracks);
        if (tr == null) return null;

        ShipState s = Game.State.Ship;
        double units = rangeAu * GameConstants.GAME_UNITS_PER_UA;
        double sigmaUnits = Math.Max(rangeSigmaAu, rangeAu * 0.001) * GameConstants.GAME_UNITS_PER_UA;
        tm.ApplyRadarFix(tr.id, now, units, sigmaUnits, bearingDeg, s.x, s.z, hasRate, rangeRateKmS);
        if (hasElevation) tm.ApplyElevationFix(tr.id, now, elevationDeg, 1f, ElevationSource.Atlas);

        tm.SelectedId = tr.id;
        return tr;
    }

    private static bool TryShipPos(out Vec3d shipPos)
    {
        shipPos = Vec3d.zero;
        if (Game.State == null || Game.Clock == null) return false;
        ShipState s = Game.State.Ship;
        shipPos = new Vec3d(s.x, s.y, s.z);
        return true;
    }

    /// <summary>Same convention as SensorSight.WorldAzimuth/WorldElevation (0 = +Z, clockwise-positive
    /// bearing; elevation +up from the ship's horizontal plane) - duplicated rather than reused because
    /// player-facing code must not call SensorSight.</summary>
    private static void ToBearingElevationRange(Vec3d rel, out float bearingDeg, out float elevationDeg, out double rangeAu)
    {
        bearingDeg = BearingMath.Wrap360((float)(Math.Atan2(rel.x, rel.z) * Mathf.Rad2Deg));
        double horiz = Math.Sqrt(rel.x * rel.x + rel.z * rel.z);
        elevationDeg = (float)(Math.Atan2(rel.y, horiz) * Mathf.Rad2Deg);
        rangeAu = rel.Magnitude / GameConstants.GAME_UNITS_PER_UA;
    }
}
