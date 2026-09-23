using UnityEngine;

public enum SensorKind { Waterfall, Imager, Spectrometer, Radar, PreScan }

/// <summary>
/// Base class for per-tier hardware definitions. Hardware sets the ceiling; the player picks
/// the operating point below it. Upgrade physical parameters and let derived numbers follow.
///
/// Create assets with Assets > Create > Bridge > Sensor Specs, and put them in Assets/Resources/Specs/
/// so Game can load them.
/// </summary>
public abstract class SensorSpec : ScriptableObject
{
    [Header("Identity")]
    public SensorKind kind;
    [Min(1)] public int tier = 1;

    [Tooltip("Localization key for the display name (used by the UI).")]
    public string locKey;

    [Header("Power")]
    [Tooltip("Units per SIMULATED day while in use (see ProbeSpec).")]
    [Min(0f)] public float powerDraw = 1f;
}
