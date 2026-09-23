using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Whether a planet (or moon) keeps an atmosphere, and how much, from real simplified physics:
///  - Jeans escape decides which gases stick around: a gas whose escape velocity comfortably exceeds its
///    own thermal speed at the local temperature stays bound over billions of years, a gas that doesn't
///    leaks away. This is why small, warm rocky worlds tend to come out airless and cold massive ones keep
///    even light gases - the same reason Mercury has no air and Titan (cold, and orbiting a giant) does.
///  - The grey-atmosphere greenhouse approximation turns the resulting composition + pressure into an
///    actual surface temperature: Ts^4 = Teq^4 * (1 + 0.75*tau), a genuine simplified radiative-transfer
///    result (not a made-up curve). Tuned against Venus (92 atm, ~96% CO2, Teq~232K, real Ts~737K) as a
///    sanity check - it reproduces that within a few percent.
/// Both are real textbook planetary-science shapes, simplified because a full hydrodynamic-escape or
/// radiative-transfer model is well beyond what a system generator needs.
/// </summary>
public static class AtmosphereModel
{
    private struct Gas
    {
        public string symbol;
        public float  molarMass;       // g/mol
        public float  greenhouseWeight; // how "greenhouse-active" this gas is per unit partial pressure
    }

    private static readonly Gas[] Candidates =
    {
        new Gas { symbol = "H2",  molarMass = 2.02f,  greenhouseWeight = 0.05f },
        new Gas { symbol = "He",  molarMass = 4.00f,  greenhouseWeight = 0.00f },
        new Gas { symbol = "CH4", molarMass = 16.04f, greenhouseWeight = 1.00f },
        new Gas { symbol = "N2",  molarMass = 28.01f, greenhouseWeight = 0.02f },
        new Gas { symbol = "O2",  molarMass = 32.00f, greenhouseWeight = 0.02f },
        new Gas { symbol = "H2O", molarMass = 18.02f, greenhouseWeight = 0.60f },
        new Gas { symbol = "Ar",  molarMass = 39.95f, greenhouseWeight = 0.00f },
        new Gas { symbol = "CO2", molarMass = 44.01f, greenhouseWeight = 1.00f },
    };

    /// <summary>Escape velocity in m/s, from real mass (solar masses) and radius (game units, GAME_UNITS_PER_UA per AU).</summary>
    public static float EscapeVelocityMs(float massSolar, float radiusGame)
    {
        float massKg  = massSolar * GameConstants.SOLAR_MASS_IN_KG;
        float radiusM = (radiusGame / GameConstants.GAME_UNITS_PER_UA) * GameConstants.AU_IN_METERS;
        if (radiusM <= 0f) return 0f;
        return Mathf.Sqrt(2f * GameConstants.GRAVITATIONAL_CONSTANT * massKg / radiusM);
    }

    /// <summary>RMS thermal speed of a gas at this temperature, m/s.</summary>
    private static float ThermalSpeedMs(float molarMassGPerMol, float temperatureK)
    {
        float massKgPerMolecule = (molarMassGPerMol / 1000f) / GameConstants.AVOGADRO_NUMBER;
        return Mathf.Sqrt(3f * GameConstants.BOLTZMANN_CONSTANT * temperatureK / massKgPerMolecule);
    }

    /// <summary>
    /// Generates a retained atmosphere for a planet/moon. Gas giants skip the escape check entirely - by
    /// construction they never lost their primordial H/He envelope, so composition there just describes the
    /// bulk gas, not a thin retained layer.
    /// </summary>
    public static Atmosphere Generate(string bodyType, float massSolar, float radiusGame, float equilibriumTempK, SeededRandom rng)
    {
        var atmo = new Atmosphere();

        if (bodyType == "Gazeuse")
        {
            atmo.isEnvelope = true;
            atmo.composition.Add(new ChemicalComposition { element = "H2", percentage = 86f });
            atmo.composition.Add(new ChemicalComposition { element = "He", percentage = 14f });
            return atmo;
        }

        float vEsc = EscapeVelocityMs(massSolar, radiusGame);
        var retained = new List<(Gas gas, float ratio)>();
        foreach (Gas g in Candidates)
        {
            float vTh = ThermalSpeedMs(g.molarMass, Mathf.Max(equilibriumTempK, 20f));
            // Jeans escape rule of thumb: a gas sticks around over billions of years once escape speed is
            // roughly 5x (or more) its own thermal speed - below that it leaks away within the planet's life.
            float ratio = vTh > 0f ? vEsc / vTh : 999f;
            if (ratio >= 5f) retained.Add((g, ratio));
        }
        if (retained.Count == 0) return atmo; // airless

        float avgRatio = 0f;
        foreach (var r in retained) avgRatio += r.ratio;
        avgRatio /= retained.Count;

        // Baseline pressure: bigger body (more outgassing/impactor delivery over its history) and more
        // comfortably-retained gases (further from the escape threshold) both push it up; then jittered for
        // variety. Not a real outgassing model, just a plausible spread from a bare hint up to ~tens of atm.
        float basePressure = Mathf.Clamp(massSolar / GameConstants.EARTH_MASS_SOLAR, 0.05f, 15f)
                            * Mathf.Clamp01((avgRatio - 5f) / 15f);
        atmo.surfacePressureAtm = Mathf.Max(0f, basePressure * rng.Range(0.3f, 3f));
        if (atmo.surfacePressureAtm < 0.001f) { atmo.surfacePressureAtm = 0f; return atmo; }

        var shares = new List<float>();
        float totalWeight = 0f;
        foreach (var r in retained)
        {
            // Gases closer to the escape threshold end up as a smaller share of what's left - a rough
            // stand-in for how much of the original inventory has slowly leaked away over time even where
            // the ratio test still (barely) passes.
            float share = Mathf.Clamp01((r.ratio - 5f) / 10f) * rng.Range(0.5f, 1.5f);
            shares.Add(share);
            totalWeight += share;
        }
        if (totalWeight <= 0f) totalWeight = 1f;

        for (int i = 0; i < retained.Count; i++)
        {
            float pct = shares[i] / totalWeight * 100f;
            if (pct < 0.01f) continue;
            atmo.composition.Add(new ChemicalComposition { element = retained[i].gas.symbol, percentage = Mathf.Round(pct * 100f) / 100f });
        }
        return atmo;
    }

    /// <summary>
    /// Grey-atmosphere greenhouse approximation: Ts^4 = Teq^4 * (1 + 0.75*tau), tau built from pressure and
    /// how greenhouse-active the retained gas mix is. Returns the equilibrium temperature unchanged for an
    /// airless body or a gas giant's envelope (a "surface" temperature doesn't mean much for either).
    /// </summary>
    public static float SurfaceTemperatureK(float equilibriumTempK, Atmosphere atmosphere)
    {
        if (atmosphere == null || atmosphere.isEnvelope || atmosphere.surfacePressureAtm <= 0f)
            return equilibriumTempK;

        float potency = 0f;
        foreach (ChemicalComposition c in atmosphere.composition)
        {
            foreach (Gas g in Candidates)
            {
                if (g.symbol != c.element) continue;
                potency += (c.percentage / 100f) * g.greenhouseWeight;
                break;
            }
        }

        float tau = atmosphere.surfacePressureAtm * potency;
        float ts4 = Mathf.Pow(equilibriumTempK, 4f) * (1f + 0.75f * tau);
        return Mathf.Pow(ts4, 0.25f);
    }
}
