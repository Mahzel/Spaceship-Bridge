/// <summary>
/// Single source of truth for every tunable constant in the simulation.
/// All other scripts reference this class instead of defining their own magic numbers.
///
/// Sections mirror the scripts they originated from so you can find a constant quickly.
/// </summary>
public static class GameConstants
{
    // =========================================================================
    #region Time  (OrbitalComponent · SystemGenerator)
    // =========================================================================

    /// <summary>
    /// Master time multiplier.
    ///   1  = real-time  (1 real second = 1 simulated second)
    ///   2  = twice real-time
    ///   3.156e7 = 1 real second = 1 simulated year
    /// </summary>
    public const float TIME_MULTIPLIER = 86400f;

    /// <summary>Seconds in a Julian year (365.25 d × 86 400 s).</summary>
    public const float SECONDS_PER_YEAR = 365.25f * 24f * 3600f; // 31 557 600 s

    #endregion

    // =========================================================================
    #region Units & Scale  (SystemGenerator · CelestialBody)
    // =========================================================================

    /// <summary>Game-units per Astronomical Unit. 1 AU = 100 game units.</summary>
    public const float GAME_UNITS_PER_UA      = 100f;

    /// <summary>Solar radius in metres (IAU 2015).</summary>
    public const float SOLAR_RADIUS_IN_METERS = 6.957e8f;

    /// <summary>1 AU in metres.</summary>
    public const float AU_IN_METERS           = 1.496e11f;

    /// <summary>Solar luminosity in watts (IAU 2015).</summary>
    public const float SOLAR_LUMINOSITY       = 3.828e26f;

    #endregion

    // =========================================================================
    #region System Generation  (SystemGenerator)
    // =========================================================================

    /// <summary>Default RNG seed used when no saved seed exists.</summary>
    public const int DEFAULT_BASE_SEED = 645865465;

    /// <summary>Size of the celestial-body object pool.</summary>
    public const int POOL_SIZE = 20;

    /// <summary>Star multiplicity probabilities: single / binary / trinary thresholds.</summary>
    public const float STAR_SINGLE_THRESHOLD = 0.50f;
    public const float STAR_BINARY_THRESHOLD = 0.90f; // rolls above this → trinary

    /// <summary>Binary star separation range (AU).</summary>
    public const float BINARY_SEPARATION_MIN_UA = 5f;
    public const float BINARY_SEPARATION_MAX_UA = 80f;

    /// <summary>Trinary inner-pair (AB) separation range (AU).</summary>
    public const float TRINARY_INNER_SEPARATION_MIN_UA = 2f;
    public const float TRINARY_INNER_SEPARATION_MAX_UA = 20f;

    /// <summary>Trinary outer-star (C) separation range (AU).</summary>
    public const float TRINARY_OUTER_SEPARATION_MIN_UA = 100f;
    public const float TRINARY_OUTER_SEPARATION_MAX_UA = 500f;

    /// <summary>
    /// P-type (circumbinary) stability multiplier.
    /// Minimum planet orbit = this × binary separation.
    /// </summary>
    public const float CIRCUMBINARY_MIN_ORBIT_FACTOR = 3.5f;

    /// <summary>
    /// S-type orbit threshold: planet orbit ≤ this × star's own orbit → parented to that star.
    /// Derived from Hill sphere approximation.
    /// </summary>
    public const float STYPE_ORBIT_THRESHOLD = 0.3f;

    /// <summary>Planet count per system.</summary>
    public const int PLANET_COUNT_MIN = 3;
    public const int PLANET_COUNT_MAX = 10;

    /// <summary>Planet orbit generation range above the minimum safe orbit (AU).</summary>
    public const float PLANET_ORBIT_SPREAD_UA = 50f;

    /// <summary>Max attempts to find a non-overlapping orbit before skipping a planet.</summary>
    public const int PLANET_ORBIT_MAX_TRIES = 100;

    /// <summary>Planet mass range (Solar masses).</summary>
    public const float PLANET_MASS_MIN = 0.1f;
    public const float PLANET_MASS_MAX = 5f;

    /// <summary>Planet orbital eccentricity range.</summary>
    public const float PLANET_ECCENTRICITY_MAX = 0.3f;

    /// <summary>Planet orbital inclination range (degrees).</summary>
    public const float PLANET_INCLINATION_MAX = 15f;

    /// <summary>Star binary orbital eccentricity range.</summary>
    public const float STAR_ECCENTRICITY_MAX = 0.5f;

    /// <summary>Star binary orbital inclination range (degrees).</summary>
    public const float STAR_INCLINATION_MAX = 10f;

    /// <summary>Planet type thresholds by orbital radius (AU).</summary>
    public const float ROCKY_ORBIT_MAX_UA    = 0.72f;
    public const float HABITABLE_ORBIT_MAX_UA = 1.52f;
    public const float GAS_ORBIT_MAX_UA      = 5.2f;
    // beyond GAS_ORBIT_MAX_UA → Icy

    #endregion

    // =========================================================================
    #region Orbital Mechanics  (OrbitalComponent)
    // =========================================================================

    /// <summary>Newton-Raphson iteration cap for eccentric anomaly solver.</summary>
    public const int KEPLER_MAX_ITERATIONS = 15;

    /// <summary>Newton-Raphson convergence threshold (radians).</summary>
    public const float KEPLER_CONVERGENCE = 1e-6f;

    #endregion

    // =========================================================================
    #region Sensor / Imager  (Imager)
    // =========================================================================

    /// <summary>Default maximum scan distance (game units).</summary>
    public const float IMAGER_MAX_SCAN_DISTANCE = 50000f;

    /// <summary>Default field of view (degrees).</summary>
    public const float IMAGER_DEFAULT_FOV = 60f;

    /// <summary>Default pixel block size.</summary>
    public const int IMAGER_DEFAULT_BLOCK_SIZE = 10;

    /// <summary>Scan texture update interval (seconds).</summary>
    public const float IMAGER_UPDATE_INTERVAL = 0.05f;

    /// <summary>Default sensor background noise floor.</summary>
    public const float IMAGER_BACKGROUND_NOISE = 0.1f;

    /// <summary>Zoom factor applied when zoom mode is active.</summary>
    public const float IMAGER_ZOOM_FACTOR = 10f;

    /// <summary>Number of frames held in the rolling integrator buffer.</summary>
    public const int IMAGER_INTEGRATOR_FRAMES = 5;

    /// <summary>Width of the scan-line indicator bar (pixels).</summary>
    public const int IMAGER_SCANLINE_BAR_WIDTH = 5;

    /// <summary>
    /// Fixed sensor gain applied to planet apparent luminosity before display.
    /// Simulates the per-sector sensitivity boost used in real astronomical imagers
    /// to compensate for the ~8-10 order-of-magnitude gap between stellar and
    /// planetary reflected flux. Tune this if planets are still too dim or too bright.
    /// </summary>
    public const float PLANET_LUMINOSITY_SENSOR_GAIN = 1e9f;

    /// <summary>
    /// Minimum value for auto-exposure reference when the FOV contains no bright body.
    /// Prevents division by zero.
    /// </summary>
    public const float IMAGER_AUTO_EXPOSURE_FLOOR = 1e-6f;

    /// <summary>
    /// Lerp factor for smooth auto-exposure adaptation each scan pass (0-1).
    /// Lower = slower/smoother. Higher = snappier.
    /// </summary>
    public const float IMAGER_AUTO_EXPOSURE_LERP = 0.05f;

    #endregion

    // =========================================================================
    #region Signal Processing  (WaterfallScreen · Spectrometer · Imager)
    // =========================================================================

    /// <summary>
    /// Gaussian PSF sigma for angular spread of a point source (bins / pixels).
    /// Shared by WaterfallScreen.SpreadSignal and Spectrometer.SpreadSpectralSignal.
    /// </summary>
    public const float PSF_SIGMA = 1.2f;

    /// <summary>PSF kernel half-width in bins (signal spread ±SPREAD_HALF_WIDTH).</summary>
    public const int PSF_SPREAD_HALF_WIDTH = 2;

    /// <summary>Gaussian noise standard deviation for the sensor baseline.</summary>
    public const float NOISE_STDDEV = 0.002f;

    /// <summary>CFAR sliding-window width (bins).</summary>
    public const int CFAR_WINDOW = 51;

    /// <summary>Number of peak bins excluded from CFAR background estimate.</summary>
    public const int CFAR_GUARD = 5;

    /// <summary>Maximum frames held in the waterfall integrator.</summary>
    public const int WATERFALL_INTEGRATION_MAX = 5;

    #endregion

    // =========================================================================
    #region Spectrometer  (Spectrometer)
    // =========================================================================

    /// <summary>Visible spectrum minimum wavelength (nm).</summary>
    public const float SPECTRUM_WAVELENGTH_MIN = 380f;

    /// <summary>Visible spectrum maximum wavelength (nm).</summary>
    public const float SPECTRUM_WAVELENGTH_MAX = 780f;

    #endregion
}