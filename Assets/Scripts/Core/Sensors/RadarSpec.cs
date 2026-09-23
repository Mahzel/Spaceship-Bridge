using UnityEngine;

/// <summary>
/// Active sensor: fires a ranging ping and waits for the echo, unlike every other sensor here which only
/// listens. Two modes (see RadarProcessor): a wide, coarse SWEEP (free-aim, adjustable beam half-width, no
/// identity — just "something's out there, this far away") and a narrow TRACK-locked Doppler ping (fixed tiny
/// beam, aimed at an existing track's current bearing, returns a near-exact range AND a radial velocity
/// reading — a real alternative to bearing-only TMA, which needs a manoeuvre to collapse its range sigma).
/// </summary>
[CreateAssetMenu(menuName = "Bridge/Sensor Specs/Radar", fileName = "RadarSpec_T4")]
public class RadarSpec : SensorSpec
{
    [Header("Sweep mode — free-aim, adjustable beam half-width (degrees)")]
    [Min(1f)] public float sweepBeamMinDeg = 10f;
    [Min(1f)] public float sweepBeamMaxDeg = 60f;

    [Header("Track mode — fixed narrow beam locked to a track's current bearing (degrees)")]
    [Min(0.01f)] public float trackBeamDeg = 0.1f;

    [Header("Range")]
    [Tooltip("Beyond this, a sweep or track ping simply finds nothing.")]
    [Min(1f)] public float maxRangeAu = 200f;

    [Header("Propagation")]
    [Tooltip("How fast the ping's echo effectively travels, AU per SIMULATED day. Deliberately far slower " +
             "than a true light-speed figure would be in this sim's time — at the game's ~86400x base time " +
             "compression (GameConstants.TIME_MULTIPLIER), a real light-speed delay would resolve in a tiny " +
             "fraction of a real second and never be felt. This value is tuned instead so a ping across a " +
             "system takes a perceptible handful of real seconds at warp x1, and — because the wait is counted " +
             "in simulated seconds — scales down with warp like every other timed event in the sim.")]
    [Min(0.1f)] public float pingSpeedAuPerDay = 10f;

    [Header("Precision (track mode)")]
    [Tooltip("Range sigma reported as this fraction of the measured range — near-exact, but not literally perfect.")]
    [Range(0.001f, 0.2f)] public float rangeSigmaFraction = 0.02f;

    [Header("Power")]
    [Tooltip("Energy spent per ping fired (sweep or track) — a one-off cost like a transmission, not a " +
             "continuous per-day draw.")]
    [Min(0f)] public float energyPerPing = 5f;

    private void OnValidate() { kind = SensorKind.Radar; }

    /// <summary>Round-trip wait, in SIMULATED seconds, for a ping to a target at this range.</summary>
    public double RoundTripSimSeconds(double rangeAu)
    {
        double days = 2.0 * rangeAu / Mathf.Max(0.1f, pingSpeedAuPerDay);
        return days * 86400.0;
    }
}
