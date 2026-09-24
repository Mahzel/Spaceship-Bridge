using UnityEngine;

[CreateAssetMenu(menuName = "Bridge/Sensor Specs/Waterfall", fileName = "WaterfallSpec_T1")]
public class WaterfallSpec : SensorSpec
{
    [Header("Array (sets beamwidth)")]
    [Min(0.1f)] public float apertureMeters   = 70f;
    [Min(0.001f)] public float wavelengthMeters = 1f;

    [Header("Elevation fan")]
    [Tooltip("Half-power half-width of the receive fan in elevation, degrees. The array hears all 360 degrees " +
             "of bearing but only a band of elevation around where it is tilted: a source this far off the tilt " +
             "arrives at half power, twice as far at 1/16.")]
    [Range(1f, 90f)] public float fanHalfWidthElDeg = 15f;
    [Tooltip("Mechanical tilt limits of the fan, degrees (+ = up).")]
    [Range(0f, 90f)] public float maxTiltDeg = 80f;

    [Header("Processing")]
    [Min(8)] public int maxBins = 2048;
    [Min(1)] public int maxIntegration = 5;
    [Tooltip("Baseline real seconds between lines at warp x1. Scales down with warp; never faster than minUpdateInterval.")]
    [Min(0.05f)] public float lineIntervalSeconds = 2f;
    [Min(0.05f)] public float minUpdateInterval = 0.5f;
    [Min(0f)] public float noiseSigma = GameConstants.NOISE_STDDEV;
    [Min(0.5f)] public float detectionThresholdSigma = 3f;

    [Header("Tracking (hardware limits)")]
    [Tooltip("How many confirmed bearing tracks the tracker can hold.")]
    [Min(1)] public int maxTracks = 4;
    [Tooltip("Association gate half-width, in display bins.")]
    [Min(1)] public int gateBins = 3;
    [Tooltip("A confirmed track is dropped after this many consecutive missed lines.")]
    [Min(1)] public int dropAfterMisses = 8;

    private void OnValidate() => kind = SensorKind.Waterfall;

    // ---- Derived: change the physics, these follow -----------------------

    /// <summary>Diffraction-limited beamwidth, theta ~ 1.22 * lambda / D, in degrees.</summary>
    public float BeamwidthDeg => 1.22f * wavelengthMeters / apertureMeters * Mathf.Rad2Deg;

    /// <summary>Gaussian sigma of the point-spread function, in degrees (FWHM = beamwidth).</summary>
    public float PsfSigmaDeg => BeamwidthDeg / 2.355f;

    /// <summary>Bins finer than half a beamwidth add no information.</summary>
    public int UsefulBins => Mathf.Clamp(Mathf.CeilToInt(720f / BeamwidthDeg), 8, maxBins);

    /// <summary>Integrating N lines improves SNR by sqrt(N).</summary>
    public float SnrGain(int n) => Mathf.Sqrt(Mathf.Max(1, n));

    /// <summary>1-sigma elevation a detection implies: the fan says "somewhere in here", nothing finer.</summary>
    public float ElevationSigmaDeg => 0.6f * fanHalfWidthElDeg;

    /// <summary>Weakest signal that crosses the detection threshold after integrating N lines.</summary>
    public float MinDetectableSignal(int n) => detectionThresholdSigma * noiseSigma / SnrGain(n);
}
