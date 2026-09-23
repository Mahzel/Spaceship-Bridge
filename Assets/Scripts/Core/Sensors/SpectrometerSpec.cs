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
