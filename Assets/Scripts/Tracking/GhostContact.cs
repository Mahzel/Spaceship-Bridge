using System;
using UnityEngine;

/// <summary>
/// Recalls a body the player already knows about (Atlas) as a fresh, aimable Tentative track in the CURRENT
/// run - "I know roughly where this should be, let me go check." Three confidence levels, matching what's
/// actually on file, from best to worst:
///  - CATALOGUED bodies (AtlasPanel's "RECALL" on a home-system body): a full orbit (NodeData.orbit) is known,
///    so the predicted state is the same analytic Kepler propagation ShipOrbit/OrbitFit already use
///    (OrbitalMechanics.NodeState) - exact, including elevation, not a guess.
///  - A survey entry with a DETERMINED ORBIT (AtlasEntry.hasOrbit - OrbitFit.TryFit succeeded at some point
///    while the source record was being captured, see DataStore.OnSensorLine/Atlas.Log): propagated with
///    KeplerOrbit.StateAt exactly like a catalogued body, just off a fitted (not known-true) orbit - good at
///    ANY future time, not just briefly after the fix, because it's real orbital mechanics, not a straight
///    line. Confident but not exact: a small nonzero sigma, since it's still a fit off measured data.
///  - Any other survey entry (AtlasEntry.recordedRange only): a single old position+velocity fix, coasted
///    forward at constant velocity - the identical straight-line model TMA itself assumes - so it stays
///    honest about being a guess: no elevation (never measured), and the sigma widens with how stale it is.
///
/// Never touches SensorSight: player-facing code must not (see its own doc comment). The geometry here is
/// plain vector math against the ship's own known position (Game.State.Ship) - the same small formula
/// SensorSight.WorldAzimuth/WorldElevation use, just against a computed world position instead of a live
/// CelestialBody transform.
///
/// Every path seeds a plain Tentative track (TrackManager.MarkBearing) with a range fix (ApplyRadarFix) and,
/// when a real elevation is known, an elevation fix - but never ApplySupport: that would artificially
/// "confirm" a lock the player hasn't actually re-acquired themselves, which is exactly the omniscience this
/// is supposed to avoid. The track stays red/searching until the player's own sensors find something there.
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
        return SeedFromWorldState(displayName, now, shipPos, pos, vel, 0.001, true);
    }

    /// <summary>Seeds a track from a survey entry: prefers a determined orbit (propagated to now, valid at any
    /// future time) when the entry has one, otherwise falls back to coasting the entry's last position+
    /// velocity fix forward at constant velocity. Null if the entry has neither.</summary>
    public static Track FromEntry(AtlasEntry e, string displayName)
    {
        if (e == null) return null;
        if (e.hasOrbit)
        {
            Track viaOrbit = FromEntryOrbit(e, displayName);
            if (viaOrbit != null) return viaOrbit;
        }
        return FromEntryCoast(e, displayName);
    }

    /// <summary>Propagates a determined orbit (AtlasEntry.orbit, relative to orbitPrimaryName) to now with
    /// KeplerOrbit.StateAt, same as a catalogued body - just off a FITTED orbit, not a known-true one, so it
    /// gets a small nonzero sigma rather than the catalogued case's near-zero. Null if the named primary isn't
    /// in the CURRENT system (a stale name from a different generation, or the ship's moved systems since).</summary>
    private static Track FromEntryOrbit(AtlasEntry e, string displayName)
    {
        if (Game.Clock == null) return null;
        SystemManager sm = SystemManager.Current;
        SystemData data = sm != null ? sm.CurrentData : null;
        int primaryIndex = FindNodeIndex(data, e.orbitPrimaryName);
        if (primaryIndex < 0) return null;
        if (!TryShipPos(out Vec3d shipPos)) return null;

        double now = Game.Clock.SimSeconds;
        OrbitalMechanics.NodeState(data, primaryIndex, now, out Vec3d primaryPos, out Vec3d primaryVel);
        KeplerOrbit.StateAt(e.orbit, now, out Vec3d relPos, out Vec3d relVel);
        return SeedFromWorldState(displayName, now, shipPos, primaryPos + relPos, primaryVel + relVel, 0.02, true);
    }

    /// <summary>Coasts an old survey entry's recorded fix forward to now at constant velocity. Null if the
    /// entry never got a usable range fix at all (recordedRange.valid false - a bearing-only claim).</summary>
    private static Track FromEntryCoast(AtlasEntry e, string displayName)
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

    /// <summary>Shared tail for the two "we know a real world position and velocity" paths (catalogued,
    /// determined-orbit): converts to bearing/elevation/range/range-rate from the ship's own position and
    /// seeds the track. sigmaFraction is the range sigma as a fraction of range - tight for a catalogued
    /// body, a little looser for a fitted orbit.</summary>
    private static Track SeedFromWorldState(string displayName, double now, Vec3d shipPos, Vec3d pos, Vec3d vel,
                                            double sigmaFraction, bool hasVelocity)
    {
        Vec3d rel = pos - shipPos;
        ToBearingElevationRange(rel, out float bearingDeg, out float elevationDeg, out double rangeAu);

        double radialRateKmS = 0.0;
        if (hasVelocity)
        {
            // vel is game units/simSecond (OrbitalMechanics' own convention); ship velocity is km/s against
            // that same sim-second clock (ManeuverPlan.Execute adds burns straight to Ship.vx/vy/vz with no
            // extra time-rate scaling) - only the SPATIAL unit needs converting, by KmPerUnit, same as
            // OrbitFit does the other way.
            ShipState s = Game.State.Ship;
            Vec3d bodyVelKmS = vel * ShipState.KmPerUnit;
            Vec3d shipVelKmS = new Vec3d(s.vx, s.vy, s.vz);
            if (rel.Magnitude > 1e-9) radialRateKmS = Vec3d.Dot(bodyVelKmS - shipVelKmS, rel.Normalized);
        }

        return Seed(displayName, now, bearingDeg, true, elevationDeg, rangeAu, rangeAu * sigmaFraction, hasVelocity, radialRateKmS);
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

    private static int FindNodeIndex(SystemData data, string name)
    {
        if (data == null || string.IsNullOrEmpty(name)) return -1;
        for (int i = 0; i < data.nodes.Count; i++) if (data.nodes[i].name == name) return i;
        return -1;
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
