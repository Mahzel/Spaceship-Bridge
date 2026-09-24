using System;
using System.Collections.Generic;

/// <summary>
/// Real spectral features in the spectrometer's band (GameConstants.SPECTRUM_WAVELENGTH_MIN..MAX, 380-800 nm),
/// and the two physical models that turn them into a body's spectrum:
///
/// STARS (BuildStellar): a photosphere is seen in ABSORPTION. Which lines show, and how deep, is set mainly by
/// temperature, not abundance: a species' lines peak where its absorbing state is most populated (Boltzmann
/// excitation x Saha ionisation) and fade either side. Modelled as a bell curve in log T around each species'
/// peak temperature - the real reason for the spectral sequence:
///   He I  peaks ~20 000 K (B stars)          H Balmer ~9 500 K (A stars)
///   Ca II H&amp;K, Mg I b, Fe I ~4 800-5 000 K (G-K)   Na I D ~4 000 K (K)     TiO bands below ~3 800 K (M)
/// Abundance enters through the curve of growth (saturated lines deepen ~sqrt(abundance)): metallicity [Fe/H]
/// scales the metal lines by sqrt(10^[Fe/H]).
///
/// PLANETS and MOONS (BuildReflected): they shine by reflected starlight, so their spectrum is their star's
/// absorption lines, plus the molecular bands of their own atmosphere (AddAtmosphereBands). An airless rock
/// shows only its star's lines: its bulk composition has no visible-band signature (mineral bands sit in the
/// near-IR), exactly as in reality.
///
/// All wavelengths below are real (air, nm). The spectrometer's resolving power decides what blends together.
/// </summary>
public static class SpectralLineTable
{
    public struct Line
    {
        public float wavelength; // nm
        public bool  emission;   // true = emission, false = absorption
        public float weight;     // relative strength within its species' own set (bands: also the species' intrinsic strength)
        public bool  real;       // a genuine cataloged transition (all current entries are)
    }

    // -----------------------------------------------------------------------------------------------------
    #region Stellar absorption
    private sealed class Species
    {
        public Line[] lines;
        public float peakK;      // temperature of maximum line strength
        public float widthLn;    // bell width in ln(T)
        public float scale;      // strength at the peak, solar abundance (intensity units)
        public bool metal;       // scales with metallicity
        public float coolCutK;   // >0: only below this temperature (molecules dissociate above)
    }

    private static readonly Dictionary<string, Species> Stellar = new Dictionary<string, Species>
    {
        ["H"] = new Species
        {
            peakK = 9500f, widthLn = 0.35f, scale = 1000f,
            lines = new[]
            {
                new Line { wavelength = 656.28f, weight = 1.00f, real = true }, // H-alpha
                new Line { wavelength = 486.13f, weight = 0.80f, real = true }, // H-beta
                new Line { wavelength = 434.05f, weight = 0.60f, real = true }, // H-gamma
                new Line { wavelength = 410.17f, weight = 0.45f, real = true }, // H-delta
                new Line { wavelength = 397.01f, weight = 0.35f, real = true }, // H-epsilon (blends with Ca II H)
            },
        },
        ["He"] = new Species
        {
            peakK = 20000f, widthLn = 0.25f, scale = 350f,
            lines = new[]
            {
                new Line { wavelength = 447.15f, weight = 1.00f, real = true },
                new Line { wavelength = 587.56f, weight = 0.90f, real = true }, // D3
                new Line { wavelength = 667.82f, weight = 0.50f, real = true },
                new Line { wavelength = 402.62f, weight = 0.60f, real = true },
            },
        },
        ["Ca"] = new Species // Ca II H & K, plus the Ca I g line
        {
            peakK = 5000f, widthLn = 0.45f, scale = 1000f, metal = true,
            lines = new[]
            {
                new Line { wavelength = 393.37f, weight = 1.00f, real = true }, // K
                new Line { wavelength = 396.85f, weight = 0.90f, real = true }, // H
                new Line { wavelength = 422.67f, weight = 0.35f, real = true }, // Ca I "g"
            },
        },
        ["Na"] = new Species
        {
            peakK = 4000f, widthLn = 0.35f, scale = 350f, metal = true,
            lines = new[]
            {
                new Line { wavelength = 589.00f, weight = 1.00f, real = true }, // D2
                new Line { wavelength = 589.59f, weight = 0.95f, real = true }, // D1 (blends at low resolving power)
            },
        },
        ["Mg"] = new Species
        {
            peakK = 5000f, widthLn = 0.40f, scale = 350f, metal = true,
            lines = new[]
            {
                new Line { wavelength = 516.73f, weight = 0.70f, real = true }, // b4
                new Line { wavelength = 517.27f, weight = 1.00f, real = true }, // b2
                new Line { wavelength = 518.36f, weight = 0.85f, real = true }, // b1
            },
        },
        ["Fe"] = new Species
        {
            peakK = 4800f, widthLn = 0.40f, scale = 250f, metal = true,
            lines = new[]
            {
                new Line { wavelength = 404.58f, weight = 0.60f, real = true },
                new Line { wavelength = 438.35f, weight = 0.70f, real = true },
                new Line { wavelength = 495.76f, weight = 0.50f, real = true },
                new Line { wavelength = 527.04f, weight = 1.00f, real = true }, // Fraunhofer "E"
            },
        },
        ["TiO"] = new Species // band heads: the signature of M stars
        {
            peakK = 3000f, widthLn = 0.20f, scale = 900f, metal = true, coolCutK = 4200f,
            lines = new[]
            {
                new Line { wavelength = 476.1f, weight = 0.50f, real = true },
                new Line { wavelength = 516.7f, weight = 0.70f, real = true },
                new Line { wavelength = 544.8f, weight = 0.80f, real = true },
                new Line { wavelength = 615.8f, weight = 0.90f, real = true },
                new Line { wavelength = 705.4f, weight = 1.00f, real = true },
            },
        },
    };

    /// <summary>A star's photospheric absorption spectrum from its effective temperature and metallicity.</summary>
    public static Spectrum BuildStellar(float effectiveTempK, float metallicityDex)
    {
        var s = NewSpectrum();
        float lnT = (float)Math.Log(Math.Max(1000f, effectiveTempK));
        float metal = (float)Math.Sqrt(Math.Pow(10.0, metallicityDex));
        foreach (var kv in Stellar)
        {
            Species sp = kv.Value;
            if (sp.coolCutK > 0f && effectiveTempK >= sp.coolCutK) continue;
            float d = (lnT - (float)Math.Log(sp.peakK)) / sp.widthLn;
            float strength = sp.scale * (float)Math.Exp(-0.5 * d * d) * (sp.metal ? metal : 1f);
            if (strength < 1f) continue;
            foreach (Line line in sp.lines)
                s.absorptionLines.Add(new SpectralLine { wavelength = line.wavelength, intensity = strength * line.weight, species = kv.Key });
        }
        return s;
    }

    /// <summary>What a planet or moon shows: its star's absorption lines (reflected light), plus the molecular
    /// bands of its own atmosphere. star may be null (no star to reflect): bands only.</summary>
    public static Spectrum BuildReflected(Spectrum star, Atmosphere atmosphere)
    {
        var s = NewSpectrum();
        if (star != null)
        {
            if (star.absorptionLines != null)
                foreach (SpectralLine l in star.absorptionLines)
                    s.absorptionLines.Add(new SpectralLine { wavelength = l.wavelength, intensity = l.intensity, species = l.species });
            if (star.emissionLines != null)
                foreach (SpectralLine l in star.emissionLines)
                    s.emissionLines.Add(new SpectralLine { wavelength = l.wavelength, intensity = l.intensity, species = l.species });
        }
        AddAtmosphereBands(s, atmosphere);
        return s;
    }

    private static Spectrum NewSpectrum()
    {
        return new Spectrum { emissionLines = new List<SpectralLine>(), absorptionLines = new List<SpectralLine>() };
    }
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region Molecular bands (atmospheres)
    // Real visible / near-IR band heads, the ones planetary astronomers actually use:
    //  CH4  543, 619, 727 nm   - the bands that make Uranus/Neptune blue and give away Titan's methane
    //  NH3  552, 645 nm        - Jupiter and Saturn
    //  H2O  592, 651, 694, 723 nm - telluric water bands
    //  O2   628 (gamma), 687 (B), 761 (A) nm - the classic Fraunhofer A and B bands, a biosignature
    //  CO2  782, 788 nm        - the weak overtone bands Adams & Dunham first found CO2 on Venus by (1932)
    // Gases with no visible-band signature (N2, H2, He, Ar, CO, SO2) are dark here, as in reality: Titan's
    // nitrogen is invisible, its methane isn't. Band depth grows with the gas's share and with the column
    // (surface pressure; a giant's envelope counts as a very deep column), see AddAtmosphereBands.
    private static readonly Dictionary<string, Line[]> Bands = new Dictionary<string, Line[]>
    {
        ["CH4"] = new[]
        {
            new Line { wavelength = 543.0f, weight = 0.15f, real = true },
            new Line { wavelength = 619.0f, weight = 0.45f, real = true },
            new Line { wavelength = 727.0f, weight = 1.00f, real = true },
        },
        ["NH3"] = new[]
        {
            new Line { wavelength = 552.0f, weight = 0.40f, real = true },
            new Line { wavelength = 645.0f, weight = 1.00f, real = true },
        },
        ["H2O"] = new[]
        {
            new Line { wavelength = 592.0f, weight = 0.10f, real = true },
            new Line { wavelength = 651.0f, weight = 0.20f, real = true },
            new Line { wavelength = 694.0f, weight = 0.40f, real = true },
            new Line { wavelength = 723.0f, weight = 1.00f, real = true },
        },
        ["O2"] = new[]
        {
            new Line { wavelength = 628.0f, weight = 0.15f, real = true }, // gamma
            new Line { wavelength = 687.0f, weight = 0.45f, real = true }, // B
            new Line { wavelength = 760.8f, weight = 1.00f, real = true }, // A
        },
        ["CO2"] = new[]
        {
            // Intrinsically weak overtone bands (x0.1): even Venus' 92 atm of CO2 shows them only modestly.
            new Line { wavelength = 782.0f, weight = 0.10f, real = true },
            new Line { wavelength = 788.3f, weight = 0.08f, real = true },
        },
    };

    /// <summary>Scale of molecular bands, set against the stellar lines: Earth's O2 A band comes out about as deep
    /// as the Sun's Ca II K, Neptune's methane deeper than any solar line.</summary>
    private const float MolecularScale = 200f;
    private const float EnvelopeColumn = 4f;

    /// <summary>
    /// Relative absorbing column of an atmosphere: 0 for none, 1 at Earth's 1 atm, growing slowly (log) above,
    /// fading fast below (Mars' 0.006 atm ~ 0.28, Pluto's 1e-5 atm ~ 0.004). A giant's envelope is a deep column.
    /// </summary>
    public static float Column(Atmosphere atmo)
    {
        if (atmo == null) return 0f;
        if (atmo.isEnvelope) return EnvelopeColumn;
        if (atmo.surfacePressureAtm <= 0f) return 0f;
        return Math.Min(2f, (float)Math.Log10(1.0 + 1000.0 * atmo.surfacePressureAtm) / 3f);
    }

    /// <summary>Adds the atmosphere's molecular bands to a spectrum as absorption lines. Depth ~ sqrt(share)
    /// (bands saturate: a trace gas already shows, a dominant one isn't proportionally deeper) x column.</summary>
    public static void AddAtmosphereBands(Spectrum spectrum, Atmosphere atmo)
    {
        if (spectrum == null || atmo == null || atmo.composition == null) return;
        float column = Column(atmo);
        if (column <= 0f) return;
        foreach (ChemicalComposition gas in atmo.composition)
        {
            if (gas.percentage <= 0f || !Bands.TryGetValue(gas.element, out Line[] lines)) continue;
            float depth = MolecularScale * (float)Math.Sqrt(gas.percentage) * column;
            foreach (Line line in lines)
                spectrum.absorptionLines.Add(new SpectralLine
                {
                    wavelength = line.wavelength, intensity = depth * line.weight, species = gas.element,
                });
        }
    }
    #endregion
}
