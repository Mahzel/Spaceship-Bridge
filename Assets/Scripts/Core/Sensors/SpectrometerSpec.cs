using UnityEngine;

[CreateAssetMenu(menuName = "Bridge/Sensor Specs/Spectrometer", fileName = "SpectrometerSpec_T3")]
public class SpectrometerSpec : SensorSpec
{
    [Header("Band (nm)")]
    public float wavelengthMin = GameConstants.SPECTRUM_WAVELENGTH_MIN;
    public float wavelengthMax = GameConstants.SPECTRUM_WAVELENGTH_MAX;

    [Header("Resolving power")]
    [Tooltip("R = lambda / delta-lambda. Higher R separates closer line pairs and Doppler-shifted lines.")]
    [Min(1f)] public float resolvingPower = 200f;

    /// <summary>Smallest resolvable wavelength difference at the given wavelength (nm).</summary>
    public float DeltaLambdaAt(float wavelengthNm) => wavelengthNm / resolvingPower;

    [Header("Pointing")]
    [Tooltip("Half-width of the slit's acceptance cone around a track's bearing, degrees. Every body inside " +
             "it contributes light; two sources in the slit together give a blended spectrum.")]
    [Min(0.01f)] public float slitHalfWidthDeg = 0.5f;
    [Tooltip("The slit follows the track in bearing AND elevation. It won't integrate unless the track's " +
             "elevation 1-sigma (aged) is at most this: a coarse waterfall/radar-fan elevation isn't good enough " +
             "to put a half-degree slit on the contact. Refine it with the imager's FIX or a radar TRACK ping.")]
    [Min(0.01f)] public float maxPointingSigmaDeg = 1f;

    [Header("Line identification")]
    [Tooltip("Seconds locked on a target before its spectral lines resolve from an unlabeled provisional " +
             "tick to a confirmed, labeled species ID - mirrors integration time on a real spectrograph.")]
    [Min(0f)] public float identifyDwellSeconds = 3f;

    private void OnValidate()
    {
        kind = SensorKind.Spectrometer;
        if (tier < 3) tier = 3;
    }
}
