using UnityEngine;

/// <summary>
/// Turns a track's best current range estimate (TrackManager.BestRange - TMA or radar, already fused) into a
/// full Kepler orbit around the ship's current primary. Not a separate fitting pass: the range estimate
/// already carries a state vector (TMA's constant-velocity model gives a position AND a velocity), so this
/// is just a frame change (RangeEstimate's system frame -> primary-relative) plus
/// OrbitalMechanics.StateToElements, the exact conversion ShipOrbit itself uses on its own state.
///
/// It refines "for free" as the ship's own orbital arc builds parallax: RangeEstimate.rangeSigma already
/// shrinks with more (and more varied) baseline - see RangeEstimator's own doc for why a genuinely orbiting
/// (not coasting) ship makes range observable at all without needing a burn. No separate blending/averaging
/// across samples is done here; TryFit just re-reads whatever the tracker's current best range is, so it
/// sharpens on its own as that does, and goes stale the same way a long-uncorrected TMA fit would (the
/// straight-line target model stops being a good approximation once the observation span is a meaningful
/// fraction of the target's own orbital period).
///
/// Deliberately never touches NodeData or the catalogue: only what the track itself measured. A bad TMA fit
/// makes a bad orbit and a bad planned burn - that's the point, not a bug (see handoff-navigation-ui.md's
/// two-knowledge-tiers principle: everything but the ship's own state comes from tracks, uncertainty and
/// all). Only valid while the target shares the ship's CURRENT primary (the same restriction the transfer
/// planner already had): this is a one-hop frame change, not a general multi-body solver.
/// </summary>
public static class OrbitFit
{
    /// <summary>True with `elements` filled in if the track's current range estimate is good enough to
    /// convert into an orbit (RangeEstimate.Observable). Cheap - reuses tr.range, no new fitting work.</summary>
    public static bool TryFit(Track tr, out OrbitElements elements)
    {
        elements = default;
        if (tr == null || !tr.range.Observable) return false;

        ShipOrbit orbit = Game.State != null ? Game.State.ShipOrbit : null;
        if (orbit == null || !orbit.Valid || orbit.PrimaryIndex < 0) return false;

        SystemManager sm = SystemManager.Current;
        if (sm == null || sm.CurrentData == null) return false;
        double now = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;

        OrbitalMechanics.NodeState(sm.CurrentData, orbit.PrimaryIndex, now, out Vec3d primaryPos, out Vec3d primaryVel);

        RangeEstimate re = tr.range;
        float kmPerUnit = (float)ShipState.KmPerUnit;
        Vector3 relPos = new Vector3((float)(re.x - primaryPos.x), 0f, (float)(re.z - primaryPos.z));
        Vector3 relVel = new Vector3(
            (float)(re.vxKmS / kmPerUnit - primaryVel.x),
            0f,
            (float)(re.vzKmS / kmPerUnit - primaryVel.z));

        (OrbitElements el, double _) = OrbitalMechanics.StateToElements(relPos, relVel, orbit.Mu, now);
        if (el.orbitalPeriod <= 0.0) return false; // degenerate fit (purely radial, or invalid input)

        elements = el;
        return true;
    }
}
