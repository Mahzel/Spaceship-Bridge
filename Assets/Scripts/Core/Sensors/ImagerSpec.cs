using UnityEngine;

[CreateAssetMenu(menuName = "Bridge/Sensor Specs/Imager", fileName = "ImagerSpec_T2")]
public class ImagerSpec : SensorSpec
{
    [Header("Field of view (degrees)")]
    [Min(0.1f)] public float fovMin = 1f;
    [Min(0.1f)] public float fovMax = 180f;

    [Header("Resolution")]
    [Tooltip("Smallest pixel block the hardware can resolve. Lower = finer angular resolution. " +
             "1 = no limit (matches the pre-spec behaviour). Raise it for the baseline imager.")]
    [Min(1)] public int minBlockSize = 1;

    [Header("Processing")]
    [Tooltip("Frames averaged by the integrator. 20 matches the pre-spec behaviour.")]
    [Min(1)] public int integratorDepth = 20;
    [Tooltip("Ratio between brightest and faintest usable signal, in dB.")]
    [Min(1f)] public float dynamicRangeDb = 60f;
    [Tooltip("Real seconds per scan row.")]
    [Min(0.005f)] public float scanRowIntervalSeconds = GameConstants.IMAGER_UPDATE_INTERVAL;

    private void OnValidate()
    {
        kind = SensorKind.Imager;
        if (tier < 2) tier = 2;
    }
}
