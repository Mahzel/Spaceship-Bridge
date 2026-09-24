using UnityEngine;

/// <summary>
/// Active sensor: fires a ranging ping and waits for the echo, unlike every other sensor here which only
/// listens. Two modes (see RadarProcessor): a SWEEP over a free-aimed sector that returns a bearing/range blip
/// per contact (no identity; the player can mark a blip as a track) and a narrow TRACK-locked Doppler ping (fixed tiny
/// beam, aimed at an existing track's current bearing, returns a near-exact range AND a radial velocity
/// reading — a real alternative to bearing-only TMA, which needs a manoeuvre to collapse its range sigma).
/// </summary>
[CreateAssetMenu(menuName = "Bridge/Sensor Specs/Radar", fileName = "RadarSpec_T4")]
public class RadarSpec : SensorSpec
{
    [Header("Sweep mode — free-aim, adjustable beam half-width (degrees)")]
    [Min(1f)] public float sweepBeamMinDeg = 10f;
    [Min(1f)] public float sweepBeamMaxDeg = 60f;

    [Header("Elevation (degrees, + = up)")]
    [Tooltip("Half-power half-width of the SWEEP beam in elevation. The sweep fans across the aimed bearing " +
             "sector at the aimed elevation; a target this far off the aim elevation returns half the power.")]
    [Min(0.1f)] public float sweepElevationHalfWidthDeg = 5f;
    [Tooltip("Mechanical elevation limits of the antenna.")]
    [Range(0f, 90f)] public float maxTiltDeg = 85f;

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

    [Header("Sweep processing — bearing x range grid, CFAR, same pattern as the waterfall")]
    [Tooltip("Angular size of one sweep beam cell. A return's bearing is only known to within about this, " +
             "refined by interpolating between neighbouring cells.")]
    [Min(0.1f)] public float sweepCellDeg = 1f;
    [Tooltip("Range bins across the selected display range. Two contacts closer in range than one bin (and in " +
             "the same cell) merge into a single return.")]
    [Min(32)] public int rangeBins = 512;
    [Tooltip("Selectable instrumented ranges, AU (a -/+ stepper on the scope, not one button per tier, so this " +
             "can hold as many as make sense). Shorter = finer range bins and a shorter listen window. The " +
             "bottom few are sized for ranging a moon from a close planetary orbit (roughly 1,000 km to a few " +
             "hundred thousand km) - the old AU-only spread bottomed out at 5 AU, useless at lunar distance " +
             "(~0.0026 AU): the return sat dead-center on the scope with no usable spatial or range-bin " +
             "resolution.")]
    public float[] rangeScalesAu =
    {
        0.0000067f,  // ~1,000 km
        0.000067f,   // ~10,000 km
        0.00067f,    // ~100,000 km
        0.0033f,     // ~500,000 km
        0.0067f,     // ~1,000,000 km
        0.05f,       // ~7,500,000 km
        1f, 5f, 20f, 60f, 200f,
    };
    [Tooltip("CFAR threshold, in noise sigmas (the noise estimate comes from a short window, so its tails are " +
             "fatter than a pure Gaussian). At 5.0 a 60-degree sweep gives about one false return every three " +
             "pings; 4.2 gives about three per ping.")]
    [Min(1f)] public float detectionThresholdSigma = 5f;
    [Tooltip("SNR of an Earth-sized, albedo-0.3 body at referenceRangeAu. Echo power falls as 1/R^4 and " +
             "scales with cross-section (radius^2 x albedo).")]
    [Min(0.1f)] public float referenceSnr = 40f;
    [Min(0.01f)] public float referenceRangeAu = 10f;
    [Tooltip("How long a sweep return stays on the scope, in SIMULATED days, fading as it ages.")]
    [Min(0.1f)] public float returnPersistenceDays = 20f;

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
