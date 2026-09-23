using UnityEngine;

/// <summary>
/// Baseline numbers for the probe itself (not its sensors). Units are abstract for now.
/// All loads and outputs are expressed per SIMULATED DAY regardless of warp setting: with the
/// default real-time pacing (warp "1s"), "1 unit/day" drains about one unit per simulated day, which
/// takes 86400 real seconds (a full day) to elapse - warp up (e.g. "60d") to make that meaningful on
/// a human timescale.
///
/// Create one with Assets > Create > Bridge > Probe Spec and put it in Assets/Resources/Specs/.
/// Without an asset, these defaults are used.
/// </summary>
[CreateAssetMenu(menuName = "Bridge/Probe Spec", fileName = "ProbeSpec")]
public class ProbeSpec : ScriptableObject
{
    [Header("Power (units; loads and outputs are per simulated day)")]
    [Min(1f)] public float powerCapacity = 1000f;

    [Tooltip("Always-on draw of the probe's own computer.")]
    [Min(0f)] public float coreDraw = 0.5f;

    [Tooltip("Draw of the baseline waterfall when no WaterfallSpec asset is installed.")]
    [Min(0f)] public float defaultWaterfallDraw = 1f;

    [Tooltip("Solar panel output at 1 solar constant (1 AU from a 1 solar-luminosity star). Falls as 1/d^2.")]
    [Min(0f)] public float solarOutput = 3f;

    [Header("Consumables")]
    [Min(1f)] public float hydrogenCapacity = 100f;
    [Min(1f)] public float storageCapacity  = 100f;

    [Tooltip("Hydrogen spent per km/s of delta-v. With 100 hydrogen and 1.0 here the probe has 100 km/s in total.")]
    [Min(0.01f)] public float hydrogenPerKmS = 1f;

    [Header("Recording")]
    [Tooltip("Storage used by a stub (position + range claim).")]
    [Min(0.01f)] public float stubSize = 1f;
    [Tooltip("Storage used per simulated day by a raw recording of one track.")]
    [Min(0.01f)] public float rawSizePerDay = 0.25f;
    [Tooltip("Information value gained per simulated day of raw recording.")]
    [Min(0f)] public float rawValuePerDay = 0.1f;
    [Tooltip("Lossy compression: size multiplier / value multiplier.")]
    [Range(0.05f, 1f)] public float compressedSizeFactor = 0.4f;
    [Range(0.05f, 1f)] public float compressedValueFactor = 0.6f;

    [Header("Reactor")]
    [Tooltip("Reactor output at full level, power units per simulated day.")]
    [Min(0f)] public float reactorOutput = 6f;
    [Tooltip("Hydrogen burned per simulated day at full level. Burn grows faster than output (level^1.5).")]
    [Min(0f)] public float reactorHydrogenPerDay = 0.15f;

    [Header("Jump drive")]
    [Tooltip("Hydrogen per light-year jumped. With 100 hydrogen and 5 here, the probe can cover 20 ly in total (out and back).")]
    [Min(0.1f)] public float hydrogenPerLy = 5f;
    [Tooltip("Simulated days a jump takes per light-year. The probe runs on batteries meanwhile.")]
    [Min(0f)] public float jumpDaysPerLy = 30f;
    [Tooltip("How far the jump drive can see candidate systems, in light-years.")]
    [Min(1f)] public float jumpScanLy = 25f;

    [Header("Transmitter")]
    [Tooltip("Distance in light-years at which full power just closes the link (0 dB margin, standard coding).")]
    [Min(0.01f)] public float txRangeLy = 6f;
    [Tooltip("Transmitter power at full level, power units per simulated day of transmitting.")]
    [Min(0.1f)] public float txPeakPower = 20f;
    [Tooltip("Data units sent per simulated day of transmitting. Energy per unit sent = power / this.")]
    [Min(0.1f)] public float txRate = 5f;
    [Tooltip("Energy held apart for the dying probe's last transmission.")]
    [Min(0f)] public float lastGaspEnergy = 30f;
}
