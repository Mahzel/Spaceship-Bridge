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
    public const float TIME_MULTIPLIER = 600f;

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
    #region Mass scale  (SystemFactory)
    // =========================================================================
    // Every body's `mass` field is in SOLAR MASSES, including planets - that's what lets the generator use
    // the real AU/year/solar-mass form of Kepler's third law directly (see SystemFactory, ShipOrbit /
    // OrbitalMechanics). These reference masses convert the usual "Jupiter masses" / "Earth masses" figures
    // real planet science uses into that same unit, so the ranges below can be picked in familiar terms.

    /// <summary>1 Jupiter mass, in solar masses.</summary>
    public const float JUPITER_MASS_SOLAR = 9.545e-4f;

    /// <summary>1 Earth mass, in solar masses.</summary>
    public const float EARTH_MASS_SOLAR = 3.003e-6f;

    /// <summary>
    /// Deuterium-fusion threshold, ~13 Jupiter masses. A body above this line is a brown dwarf, not a
    /// planet - it fuses deuterium in its core, however it formed. Gas giants are generated below this,
    /// and separately clamped below their own parent star's mass (a "planet" heavier than its star isn't
    /// a planet - see RandomPlanetMass).
    /// </summary>
    public const float GAS_GIANT_IGNITION_MASS_SOLAR = 13f * JUPITER_MASS_SOLAR; // ~0.0124 Msun

    /// <summary>Gas giant mass range: a small gas/ice giant up to just under the ignition threshold.</summary>
    public const float GAS_GIANT_MASS_MIN = 0.03f * JUPITER_MASS_SOLAR;  // ~9.5 Earth masses
    public const float GAS_GIANT_MASS_MAX = GAS_GIANT_IGNITION_MASS_SOLAR;

    /// <summary>Icy planet mass range: sub-Earth up to a small ice giant (Neptune is ~17 Earth masses).</summary>
    public const float ICY_MASS_MIN = 0.3f * EARTH_MASS_SOLAR;
    public const float ICY_MASS_MAX = 15f  * EARTH_MASS_SOLAR;

    /// <summary>Rocky planet mass range: Mercury-ish up to a big super-Earth.</summary>
    public const float ROCKY_MASS_MIN = 0.05f * EARTH_MASS_SOLAR;
    public const float ROCKY_MASS_MAX = 8f    * EARTH_MASS_SOLAR;

    /// <summary>Asteroid-belt "planet" mass range: a token mass, just enough to feed the orbital-spacing
    /// math sensibly - not meant to represent a real single body.</summary>
    public const float BELT_MASS_MIN = 0.0001f * EARTH_MASS_SOLAR;
    public const float BELT_MASS_MAX = 0.01f   * EARTH_MASS_SOLAR;

    /// <summary>Moon mass range: a bit below Phobos-scale up to a bit above Ganymede/Titan
    /// (the Moon itself is ~0.0123 Earth masses, Ganymede ~0.025).</summary>
    public const float MOON_MASS_MIN = 0.00005f * EARTH_MASS_SOLAR;
    public const float MOON_MASS_MAX = 0.03f    * EARTH_MASS_SOLAR;

    /// <summary>A moon's orbit stays within this fraction of its planet's Hill radius - deep enough inside
    /// that the star's own gravity can't perturb it loose over the long run (real stable moons sit well
    /// inside ~0.3-0.5 of the Hill radius; this keeps a comfortable margin).</summary>
    public const float MOON_HILL_FRACTION_MAX = 0.25f;

    #endregion

    // =========================================================================
    #region Physical constants  (AtmosphereModel)
    // =========================================================================

    /// <summary>Newtonian gravitational constant G, m^3 kg^-1 s^-2.</summary>
    public const float GRAVITATIONAL_CONSTANT = 6.674e-11f;

    /// <summary>Boltzmann constant, J/K.</summary>
    public const float BOLTZMANN_CONSTANT = 1.380649e-23f;

    /// <summary>Avogadro's number, mol^-1.</summary>
    public const float AVOGADRO_NUMBER = 6.02214076e23f;

    /// <summary>Solar mass in kilograms (IAU nominal).</summary>
    public const float SOLAR_MASS_IN_KG = 1.98847e30f;

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
