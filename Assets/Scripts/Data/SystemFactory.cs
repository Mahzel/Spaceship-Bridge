using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Generates a SystemData from (world seed, system ID). Pure and deterministic: it uses a local
/// SeededRandom, creates no GameObjects and never touches UnityEngine.Random.
/// Ported from the old SystemManager generation code, with these fixes:
///  - the two members of a pair now share e, i, Omega and M0 (only omega differs by 180 deg), so their
///    centre of mass sits exactly on the barycenter at all times;
///  - composition varies per body (abundance jitter) instead of being identical for every rocky planet.
/// </summary>
public static class SystemFactory
{
    private const float GU = GameConstants.GAME_UNITS_PER_UA;
    private static readonly char[] LETTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    // =========================================================================
    #region IDs
    public static bool IsValidSystemID(string id)
        => id != null && Regex.IsMatch(id, @"^[A-Z]{2}-\d-\d{2}-\d{5}$");

    public static string GenerateSystemID(int seed)
    {
        var r = new SeededRandom(seed);
        return $"{LETTERS[r.Range(0, 26)]}{LETTERS[r.Range(0, 26)]}"
             + $"-{r.Range(0, 10)}-{r.Range(0, 100):D2}-{r.Range(0, 100000):D5}";
    }

    /// <summary>The home system every probe launches from: fixed by the world seed.</summary>
    public static string HomeSystemID(int worldSeed) => GenerateSystemID(unchecked(worldSeed ^ 0x48304D45));

    public static int HashIDToSeed(string id)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(id));
            return BitConverter.ToInt32(h, 0) & 0x7FFFFFFF;
        }
    }
    #endregion

    // =========================================================================
    #region Entry point
    public static SystemData Generate(int baseSeed, string systemID)
    {
        int seed = unchecked(baseSeed + HashIDToSeed(systemID));
        var rng  = new SeededRandom(seed);
        var sys  = new SystemData(systemID, seed);

        // Multiplicity: 50% single | 40% binary | 10% trinary
        float roll = rng.Value;
        int starCount = roll < GameConstants.STAR_SINGLE_THRESHOLD ? 1
                      : roll < GameConstants.STAR_BINARY_THRESHOLD ? 2 : 3;

        float totalStarMass;
        float minOrbitUA;

        if (starCount == 1)
        {
            StarData d = RandomStarData(rng, $"{systemID} A");
            AddStar(sys, rng, d, -1, null);
            totalStarMass = d.mass;
            minOrbitUA    = CalculateMinimumOrbitalDistance(d.radiusGame) / GU;
        }
        else if (starCount == 2)
        {
            float sepUA = rng.Range(GameConstants.BINARY_SEPARATION_MIN_UA, GameConstants.BINARY_SEPARATION_MAX_UA);
            StarData dA = RandomStarData(rng, $"{systemID} A");
            StarData dB = RandomStarData(rng, $"{systemID} B");

            int root = AddBarycenter(sys, $"{systemID}_BC", -1, null);
            MakePairOrbits(rng, sepUA, dA.mass, dB.mass,
                GameConstants.STAR_ECCENTRICITY_MAX, GameConstants.STAR_INCLINATION_MAX,
                out OrbitElements oA, out OrbitElements oB);
            AddStar(sys, rng, dA, root, oA);
            AddStar(sys, rng, dB, root, oB);

            totalStarMass = dA.mass + dB.mass;
            minOrbitUA = Mathf.Max(
                CalculateMinimumOrbitalDistance(Mathf.Max(dA.radiusGame, dB.radiusGame)) / GU,
                GameConstants.CIRCUMBINARY_MIN_ORBIT_FACTOR);
        }
        else
        {
            // Close pair AB plus a distant star C, all around a fixed root barycenter.
            float sepAB = rng.Range(GameConstants.TRINARY_INNER_SEPARATION_MIN_UA, GameConstants.TRINARY_INNER_SEPARATION_MAX_UA);
            float sepC  = rng.Range(GameConstants.TRINARY_OUTER_SEPARATION_MIN_UA, GameConstants.TRINARY_OUTER_SEPARATION_MAX_UA);
            StarData dA = RandomStarData(rng, $"{systemID} A");
            StarData dB = RandomStarData(rng, $"{systemID} B");
            StarData dC = RandomStarData(rng, $"{systemID} C");
            float massAB = dA.mass + dB.mass;

            MakePairOrbits(rng, sepAB, dA.mass, dB.mass,
                GameConstants.STAR_ECCENTRICITY_MAX, GameConstants.STAR_INCLINATION_MAX,
                out OrbitElements oA, out OrbitElements oB);
            MakePairOrbits(rng, sepC, massAB, dC.mass,
                GameConstants.STAR_ECCENTRICITY_MAX, GameConstants.STAR_INCLINATION_MAX,
                out OrbitElements oAB, out OrbitElements oC);

            int root = AddBarycenter(sys, $"{systemID}_BC", -1, null);
            int ab   = AddBarycenter(sys, $"{systemID}_BC_AB", root, oAB);
            AddStar(sys, rng, dA, ab, oA);
            AddStar(sys, rng, dB, ab, oB);
            AddStar(sys, rng, dC, root, oC);

            totalStarMass = massAB + dC.mass;
            minOrbitUA = Mathf.Max(
                CalculateMinimumOrbitalDistance(Mathf.Max(dA.radiusGame, Mathf.Max(dB.radiusGame, dC.radiusGame))) / GU,
                GameConstants.CIRCUMBINARY_MIN_ORBIT_FACTOR);
        }

        float systemMetallicity = 0f;
        int starCountForAvg = 0;
        foreach (NodeData n in sys.nodes)
            if (n.kind == NodeKind.Star) { systemMetallicity += n.metallicity; starCountForAvg++; }
        if (starCountForAvg > 0) systemMetallicity /= starCountForAvg;

        GeneratePlanets(sys, rng, totalStarMass, minOrbitUA, systemMetallicity);

        // Arrival point: outside the outermost planet, random bearing.
        float lastOrbit = 0f;
        foreach (NodeData n in sys.nodes)
            if (n.kind == NodeKind.Planet) lastOrbit = Mathf.Max(lastOrbit, n.orbit.semiMajorAxis);

        sys.shipDistance   = Mathf.Max(lastOrbit * 1.1f, minOrbitUA * GU * 2f);
        sys.shipAzimuthRad = rng.Range(0f, 360f) * Mathf.Deg2Rad;
        return sys;
    }
    #endregion

    // =========================================================================
    #region Nodes and pair orbits
    private static int AddBarycenter(SystemData sys, string name, int parent, OrbitElements? orbit)
    {
        var n = new NodeData { index = sys.nodes.Count, parent = parent, kind = NodeKind.Barycenter, name = name };
        if (orbit.HasValue) { n.hasOrbit = true; n.orbit = orbit.Value; }
        sys.nodes.Add(n);
        return n.index;
    }

    private static int AddStar(SystemData sys, SeededRandom rng, StarData d, int parent, OrbitElements? orbit)
    {
        string starType   = DetermineStarType(d.luminosity, d.temperature);
        // Metallicity in dex ([Fe/H]-style: 0 = solar, negative = metal-poor, positive = metal-rich) -
        // shifts this star's own heavy-element abundance (see DetermineChemicalComposition) and, via the
        // system-average passed into GeneratePlanets, nudges rocky-planet iron content the same way real
        // planet-metallicity correlations do.
        float metallicity = rng.Range(-1.0f, 0.3f);
        float ageGyr       = EstimateStarAge(rng, d.mass, starType);

        var n = new NodeData
        {
            index = sys.nodes.Count, parent = parent, kind = NodeKind.Star, name = d.name,
            bodyType       = starType,
            temperature    = d.temperature,
            mass           = d.mass,
            radiusGame     = d.radiusGame,
            radiusSol      = d.radiusSol,
            starLuminosity = d.luminosity,
            metallicity    = metallicity,
            ageGyr         = ageGyr,
            composition    = DetermineChemicalComposition("Star", rng, metallicity)
        };
        n.spectrum = DetermineSpectrum(n.composition);
        if (orbit.HasValue) { n.hasOrbit = true; n.orbit = orbit.Value; }
        sys.nodes.Add(n);
        return n.index;
    }

    /// <summary>
    /// Rough stellar age. Main-sequence lifetime scales steeply with mass (t ~ 10 Gyr * M^-2.5, calibrated
    /// to the Sun's ~10 Gyr lifetime) - a real, well-known approximation. Ordinary stars get a random point
    /// somewhere in that lifetime; anything already flagged as an evolved type (giant, supergiant, white
    /// dwarf) is placed at or just past the end of it, since that's what "evolved" means.
    /// </summary>
    private static float EstimateStarAge(SeededRandom rng, float massSolar, string starType)
    {
        float lifetimeGyr = 10f * Mathf.Pow(Mathf.Max(massSolar, 0.08f), -2.5f);
        bool evolved = starType == "White Dwarf" || starType.Contains("Giant") || starType.Contains("Supergiant");
        return evolved ? lifetimeGyr * rng.Range(1.0f, 1.3f) : rng.Range(0f, lifetimeGyr);
    }

    /// <summary>
    /// Orbits for a two-body pair around their common barycenter. Both share e, i, Omega and M0;
    /// only omega differs by 180 deg, so the bodies are always exactly opposite and the mass-weighted
    /// centre sits on the barycenter. sma_A = sep * mB / M, sma_B = sep * mA / M.
    /// </summary>
    private static void MakePairOrbits(SeededRandom rng, float separationUA, float massA, float massB,
        float eccMax, float incMax, out OrbitElements a, out OrbitElements b)
    {
        float total = massA + massB;
        float ecc = rng.Range(0f, eccMax);
        float inc = rng.Range(-incMax, incMax);
        float lan = rng.Range(0f, 360f);
        float m0  = rng.Range(0f, 360f);
        double period = YearsToSimSeconds(Mathf.Sqrt(Mathf.Pow(separationUA, 3f) / total));

        a = new OrbitElements
        {
            semiMajorAxis = separationUA * GU * massB / total,
            eccentricity = ecc, inclination = inc, longitudeAscNode = lan,
            argumentPeriapsis = 0f, meanAnomalyAtEpoch = m0, orbitalPeriod = period
        };
        b = a;
        b.semiMajorAxis      = separationUA * GU * massA / total;
        b.argumentPeriapsis  = 180f;
    }
    #endregion

    // =========================================================================
    #region Planets
    private static void GeneratePlanets(SystemData sys, SeededRandom rng, float totalStarMass, float minOrbitUA, float systemMetallicity)
    {
        float totalLuminosity = 0f;
        foreach (NodeData n in sys.nodes)
            if (n.kind == NodeKind.Star) totalLuminosity += n.starLuminosity;
        totalLuminosity = Mathf.Max(totalLuminosity, 0.0001f);

        var orbits = new List<(float radius, float width)>();
        var parentPlanetIndex = new Dictionary<int, int>();

        int planetCount = rng.Range(GameConstants.PLANET_COUNT_MIN, GameConstants.PLANET_COUNT_MAX);

        for (int i = 0; i < planetCount; i++)
        {
            bool  valid = false;
            float rGame = 0f, width = 0f, pMass = 0f;
            int   tries = 0;

            while (!valid && tries < GameConstants.PLANET_ORBIT_MAX_TRIES)
            {
                tries++;
                float rUA = RandomOrbitUA(rng, minOrbitUA);
                rGame = rUA * GU;
                // The exact type (with its belt/rocky coin flip) is only decided once an orbit is
                // accepted below; for the spacing search itself, a type guessed from distance alone is
                // enough to pick a realistic mass and therefore a realistic exclusion width.
                pMass = RandomPlanetMass(rng, PlanetTypeForOrbit(rUA), totalStarMass);
                width = CalculateOrbitalWidth(rGame, pMass, totalStarMass);

                valid = true;
                foreach (var o in orbits)
                    if (DoOrbitsOverlap(rGame, width, o.radius, o.width)) { valid = false; break; }
            }
            if (!valid) continue; // no free orbit found for this planet

            orbits.Add((rGame, width));

            float  rUA2    = rGame / GU;
            float  period  = Mathf.Sqrt(Mathf.Pow(rUA2, 3f) / totalStarMass);
            string pType   = DeterminePlanetType(rUA2, rng);
            // Reroll the mass for the FINAL type (matters when the coin flip above landed on the asteroid
            // belt instead of the rocky guess used for spacing - a belt's token mass is far smaller).
            pMass    = RandomPlanetMass(rng, pType, totalStarMass);
            float  albedo  = GenerateAlbedo(pType, rng);
            float  temp    = DetermineTemperature(rUA2, totalLuminosity, albedo);
            float  density = DetermineDensity(pType);
            float  sizeSol = DetermineNormalizedSize(pType, pMass);
            float  sizeGame = SolarRadiusToGameUnits(sizeSol);

            Atmosphere atmo    = AtmosphereModel.Generate(pType, pMass, sizeGame, temp, rng);
            float      surfTemp = AtmosphereModel.SurfaceTemperatureK(temp, atmo);
            string     sClass   = DetermineSurfaceClass(pType, pMass, surfTemp, atmo);

            int parent = FindOrbitParent(sys, rGame);
            string parentShort = sys.nodes[parent].name.Replace(sys.id, "").Trim();
            if (string.IsNullOrEmpty(parentShort)) parentShort = sys.nodes[parent].name;

            parentPlanetIndex.TryGetValue(parent, out int planetNum);
            planetNum++;
            parentPlanetIndex[parent] = planetNum;

            var node = new NodeData
            {
                index = sys.nodes.Count, parent = parent, kind = NodeKind.Planet,
                name = $"{sys.id} {parentShort}-{planetNum}",
                bodyType = pType, temperature = temp, mass = pMass, density = density,
                radiusGame = sizeGame, albedo = albedo,
                composition = DetermineChemicalComposition(pType, rng, systemMetallicity),
                atmosphere = atmo, surfaceTemperature = surfTemp, surfaceClass = sClass,
                hasOrbit = true,
                orbit = new OrbitElements
                {
                    semiMajorAxis      = rGame,
                    eccentricity       = rng.Range(0f, GameConstants.PLANET_ECCENTRICITY_MAX),
                    inclination        = rng.Range(-GameConstants.PLANET_INCLINATION_MAX, GameConstants.PLANET_INCLINATION_MAX),
                    longitudeAscNode   = rng.Range(0f, 360f),
                    argumentPeriapsis  = rng.Range(0f, 360f),
                    meanAnomalyAtEpoch = rng.Range(0f, 360f),
                    orbitalPeriod      = YearsToSimSeconds(period)
                }
            };
            node.spectrum = DetermineSpectrum(node.composition);
            sys.nodes.Add(node);

            GenerateMoons(sys, rng, node.index, pType, pMass, sizeGame, temp, totalStarMass, systemMetallicity);
        }
    }

    // =========================================================================
    #region Moons
    /// <summary>
    /// A moon-generation pass for one just-created planet. Needs no changes anywhere outside SystemFactory:
    /// SystemManager/OrbitalMechanics/ShipOrbit already resolve position, mass and gravity generically via
    /// NodeData.parent chains, regardless of nesting depth - a moon is simply another NodeData with
    /// kind = Planet and parent = its planet's own node index. Orbits are placed within a fraction of the
    /// planet's own Hill sphere (the same Hill-radius formula OrbitalMechanics.FindPrimary already uses to
    /// recognize a stable local orbit), log-uniform for the same statistical reason RandomOrbitUA is -
    /// spacing that's roughly scale-invariant, not linear-uniform.
    /// </summary>
    private static void GenerateMoons(SystemData sys, SeededRandom rng, int planetIndex, string planetType,
        float planetMass, float planetRadiusGame, float planetTemp, float totalStarMass, float systemMetallicity)
    {
        NodeData planet = sys.nodes[planetIndex];
        if (!planet.hasOrbit || planetType == "Ceinture d'astéroïdes") return;

        int moonCount = RollMoonCount(rng, planetType, planetMass);
        if (moonCount == 0) return;

        float hill = planet.orbit.semiMajorAxis * (1f - planet.orbit.eccentricity)
                   * Mathf.Pow(planetMass / (3f * totalStarMass), 1f / 3f);
        float maxOrbit = hill * GameConstants.MOON_HILL_FRACTION_MAX;
        float minOrbit = Mathf.Max(planetRadiusGame * 3f, maxOrbit * 0.02f);
        if (maxOrbit <= minOrbit) return; // no room inside the Hill sphere for a stable moon here

        var moonOrbits = new List<(float radius, float width)>();
        int made = 0;

        for (int m = 0; m < moonCount; m++)
        {
            bool  valid = false;
            float rGame = 0f, width = 0f, mMass = 0f;
            int   tries = 0;
            while (!valid && tries < 20)
            {
                tries++;
                rGame = Mathf.Exp(rng.Range(Mathf.Log(minOrbit), Mathf.Log(maxOrbit)));
                mMass = RandomMoonMass(rng);
                width = CalculateOrbitalWidth(rGame, mMass, planetMass);
                valid = true;
                foreach (var o in moonOrbits)
                    if (DoOrbitsOverlap(rGame, width, o.radius, o.width)) { valid = false; break; }
            }
            if (!valid) continue;
            moonOrbits.Add((rGame, width));

            // Icy moons dominate around giants (Jupiter/Saturn's big moons are almost all ice/rock mixes);
            // a rocky planet's rare moon is rocky, more like Earth's own.
            string mType = planetType == "Rocheuse" ? "Rocheuse" : (rng.Value < 0.7f ? "Glacée" : "Rocheuse");

            float mAlbedo  = GenerateAlbedo(mType, rng);
            float mDensity = DetermineDensity(mType);
            float mSizeSol = DetermineNormalizedSize(mType, mMass);
            float mSizeGame = SolarRadiusToGameUnits(mSizeSol);
            float rUA = rGame / GU;
            // Kepler's third law around the PLANET, not the star - same convention as MakePairOrbits.
            float period = Mathf.Sqrt(Mathf.Pow(rUA, 3f) / Mathf.Max(planetMass, 1e-12f));

            made++;
            Atmosphere atmo    = AtmosphereModel.Generate(mType, mMass, mSizeGame, planetTemp, rng);
            float      surfTemp = AtmosphereModel.SurfaceTemperatureK(planetTemp, atmo);

            var moon = new NodeData
            {
                index = sys.nodes.Count, parent = planetIndex, kind = NodeKind.Planet,
                name = $"{planet.name}-{RomanNumeral(made)}",
                bodyType = mType, temperature = planetTemp, mass = mMass, density = mDensity,
                radiusGame = mSizeGame, albedo = mAlbedo,
                composition = DetermineChemicalComposition(mType, rng, systemMetallicity),
                atmosphere = atmo, surfaceTemperature = surfTemp,
                surfaceClass = DetermineSurfaceClass(mType, mMass, surfTemp, atmo),
                hasOrbit = true,
                orbit = new OrbitElements
                {
                    semiMajorAxis      = rGame,
                    eccentricity       = rng.Range(0f, 0.1f),
                    inclination        = rng.Range(-10f, 10f),
                    longitudeAscNode   = rng.Range(0f, 360f),
                    argumentPeriapsis  = rng.Range(0f, 360f),
                    meanAnomalyAtEpoch = rng.Range(0f, 360f),
                    orbitalPeriod      = YearsToSimSeconds(period)
                }
            };
            moon.spectrum = DetermineSpectrum(moon.composition);
            sys.nodes.Add(moon);
        }
    }

    /// <summary>How many moons a planet gets, roughly matching real solar-system asymmetry: giants
    /// frequently have several, rocky worlds rarely have one, belts never do.</summary>
    private static int RollMoonCount(SeededRandom rng, string type, float mass)
    {
        switch (type)
        {
            case "Gazeuse":
                return rng.Value < 0.35f ? 0 : rng.Range(1, 6); // 1-5
            case "Glacée":
                return mass > 5f * GameConstants.EARTH_MASS_SOLAR
                    ? (rng.Value < 0.5f ? 0 : rng.Range(1, 4))   // ice-giant tier: 1-3
                    : (rng.Value < 0.85f ? 0 : 1);               // dwarf-planet tier: rare single moon
            case "Rocheuse":
                return rng.Value < 0.88f ? 0 : 1;
            default:
                return 0;
        }
    }

    private static float RandomMoonMass(SeededRandom rng)
        => rng.Range(GameConstants.MOON_MASS_MIN, GameConstants.MOON_MASS_MAX);

    private static string RomanNumeral(int n)
    {
        string[] numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };
        return n >= 1 && n <= numerals.Length ? numerals[n - 1] : n.ToString();
    }
    #endregion

    /// <summary>
    /// Single star: always that star. Multi-star: S-type planets (orbit &lt;= threshold x the star's own
    /// orbit) go under that star, everything else stays under the root barycenter.
    /// </summary>
    private static int FindOrbitParent(SystemData sys, float planetOrbitGame)
    {
        int starCount = 0, firstStar = 0;
        for (int i = 0; i < sys.nodes.Count; i++)
            if (sys.nodes[i].kind == NodeKind.Star) { if (starCount++ == 0) firstStar = i; }

        if (starCount == 1) return firstStar;

        for (int i = 0; i < sys.nodes.Count; i++)
        {
            NodeData n = sys.nodes[i];
            if (n.kind != NodeKind.Star) continue;
            float starOrbit = n.hasOrbit ? n.orbit.semiMajorAxis : 0f;
            if (starOrbit > 0f && planetOrbitGame <= GameConstants.STYPE_ORBIT_THRESHOLD * starOrbit)
                return i;
        }
        return 0; // root
    }

    /// <summary>
    /// Orbital distance, drawn LOG-uniform (equal probability per decade of distance) rather than
    /// linear-uniform over [minOrbitUA, minOrbitUA + PLANET_ORBIT_SPREAD_UA]. Linear-uniform was the bug
    /// behind "every planet came out icy": PLANET_ORBIT_SPREAD_UA is 50 AU, but GAS_ORBIT_MAX_UA (the icy
    /// cutoff) is only 5.2 AU, so over 90% of a linear-uniform draw landed past it regardless of seed. Real
    /// orbital spacing is roughly scale-invariant (Titius-Bode is the classic informal example) - a
    /// log-uniform draw gives the inner few AU, where the rocky/gas-giant thresholds actually live, their
    /// fair share of planets while still allowing the occasional far-out icy world.
    /// </summary>
    private static float RandomOrbitUA(SeededRandom rng, float minOrbitUA)
    {
        float lo = Mathf.Max(minOrbitUA, 0.05f);
        float hi = Mathf.Max(minOrbitUA + GameConstants.PLANET_ORBIT_SPREAD_UA, lo * 1.01f);
        return Mathf.Exp(rng.Range(Mathf.Log(lo), Mathf.Log(hi)));
    }

    /// <summary>Distance-only type guess (no RNG, no belt/rocky coin flip) - just enough to pick a
    /// realistic mass for sizing the orbital exclusion zone during the placement search.</summary>
    private static string PlanetTypeForOrbit(float rUA)
    {
        if (rUA < GameConstants.HABITABLE_ORBIT_MAX_UA) return "Rocheuse";
        if (rUA < GameConstants.GAS_ORBIT_MAX_UA) return "Gazeuse";
        return "Glacée";
    }

    /// <summary>
    /// A physically-scaled mass for this planet type, in solar masses (see the Mass scale region of
    /// GameConstant.cs). Gas giants are kept below the deuterium-fusion threshold (~13 Jupiter masses -
    /// above that it's a brown dwarf, not a planet) and, separately, never above their own parent star's
    /// mass (a "planet" heavier than its star isn't a planet either).
    /// </summary>
    private static float RandomPlanetMass(SeededRandom rng, string type, float starMass)
    {
        switch (type)
        {
            case "Gazeuse":
            {
                float max = Mathf.Min(GameConstants.GAS_GIANT_MASS_MAX, 0.9f * starMass);
                float min = Mathf.Min(GameConstants.GAS_GIANT_MASS_MIN, max);
                return rng.Range(min, max);
            }
            case "Glacée":
                return rng.Range(GameConstants.ICY_MASS_MIN, GameConstants.ICY_MASS_MAX);
            case "Ceinture d'astéroïdes":
                return rng.Range(GameConstants.BELT_MASS_MIN, GameConstants.BELT_MASS_MAX);
            default: // "Rocheuse"
                return rng.Range(GameConstants.ROCKY_MASS_MIN, GameConstants.ROCKY_MASS_MAX);
        }
    }

    private static float CalculateMinimumOrbitalDistance(float starRadiusGame) => starRadiusGame * 1.2f;

    private static float CalculateOrbitalWidth(float r, float planetMass, float starMass)
        => r * Mathf.Pow(planetMass / (3f * starMass), 1f / 3f);

    private static bool DoOrbitsOverlap(float r1, float w1, float r2, float w2)
        => Mathf.Abs(r1 - r2) < (w1 + w2) / 2f;
    #endregion

    // =========================================================================
    #region Star properties
    private struct StarData
    {
        public string name;
        public float  temperature, luminosity, mass, radiusGame, radiusSol;
    }

    private static StarData RandomStarData(SeededRandom rng, string name)
    {
        RandomizeHRPosition(rng, out float temp, out float lum);
        float mass  = EstimateStarMass(rng, lum, temp);
        float rSol  = CalculateStarRadius(lum, temp);
        float rGame = SolarRadiusToGameUnits(rSol);
        return new StarData { name = name, temperature = temp, luminosity = lum,
                              mass = mass, radiusGame = rGame, radiusSol = rSol };
    }

    private static void RandomizeHRPosition(SeededRandom rng, out float temperature, out float luminosity)
    {
        float r = rng.Value;
        if (r < 0.70f)
        {
            float m = rng.Range(0.08f, 20f);
            if      (m < 0.43f) { temperature = rng.Range(2400f,  3700f);  luminosity = 0.23f * Mathf.Pow(m, 2.3f); }
            else if (m < 0.80f) { temperature = rng.Range(3700f,  5200f);  luminosity = Mathf.Pow(m, 4f); }
            else if (m < 1.05f) { temperature = rng.Range(5200f,  6000f);  luminosity = Mathf.Pow(m, 4f); }
            else if (m < 1.40f) { temperature = rng.Range(6000f,  7500f);  luminosity = 1.4f * Mathf.Pow(m, 3.5f); }
            else if (m < 2.10f) { temperature = rng.Range(7500f,  10000f); luminosity = 1.4f * Mathf.Pow(m, 3.5f); }
            else if (m < 16f)   { temperature = rng.Range(10000f, 30000f); luminosity = 1.4f * Mathf.Pow(m, 3.5f); }
            else                { temperature = rng.Range(30000f, 50000f); luminosity = 32000f * m; }
        }
        else if (r < 0.85f) { temperature = rng.Range(3500f, 5000f);   luminosity = rng.Range(10f,   1000f); }
        else if (r < 0.95f) { temperature = rng.Range(3500f, 4500f);   luminosity = rng.Range(1000f, 100000f); }
        else                { temperature = rng.Range(8000f, 100000f); luminosity = rng.Range(0.001f, 0.1f); }
    }

    private static float EstimateStarMass(SeededRandom rng, float lum, float temp)
    {
        if (temp >= 8000f && lum <= 0.1f)                                   return rng.Range(0.17f, 1.4f);
        if (temp < 3700f  && lum < 0.1f)                                    return Mathf.Pow(lum / 0.23f, 1f / 2.3f);
        if (temp > 3500f  && temp < 50000f && lum < 1000f)                  return Mathf.Pow(lum / 1.4f, 1f / 3.5f);
        if (temp >= 10000f && lum >= 1000f)                                 return rng.Range(10f, 40f);
        if (temp >= 3500f && temp <= 5000f && lum >= 10f && lum <= 1000f)   return rng.Range(0.8f, 10f);
        if (temp >= 3500f && temp <= 4500f && lum >= 1000f)                 return rng.Range(10f, 40f);
        return 1f;
    }

    private static float CalculateStarRadius(float lum, float temp)
    {
        float lumW = lum * GameConstants.SOLAR_LUMINOSITY;
        float rM   = Mathf.Sqrt(lumW / (4f * Mathf.PI * 5.670374419e-8f * Mathf.Pow(temp, 4f)));
        return rM / GameConstants.SOLAR_RADIUS_IN_METERS;
    }

    private static string DetermineStarType(float lum, float temp)
    {
        if (lum < 0.1f   && temp > 8000f)   return "White Dwarf";
        if (lum < 0.1f   && temp < 3700f)   return "M";
        if (lum < 0.6f   && temp < 5200f)   return "K";
        if (lum < 1.5f   && temp < 6000f)   return "G";
        if (lum < 5f     && temp < 7500f)   return "F";
        if (lum < 20f    && temp < 10000f)  return "A";
        if (lum < 100f   && temp < 30000f)  return "B";
        if (lum < 1000f  && temp >= 30000f) return "O";
        if (lum >= 1000f && temp < 5000f)   return "Red Supergiant";
        if (lum >= 10f   && temp < 5000f)   return "Red Giant";
        if (lum >= 1000f && temp >= 10000f) return "Blue Supergiant";
        return "Unknown";
    }
    #endregion

    // =========================================================================
    #region Planet properties
    private static string DeterminePlanetType(float rUA, SeededRandom rng)
    {
        if      (rUA < 0.72f) return "Rocheuse";
        else if (rUA < 1.52f) return rng.Value > 0.7f ? "Ceinture d'astéroïdes" : "Rocheuse";
        else if (rUA < 5.2f)  return "Gazeuse";
        else                  return "Glacée";
    }

    private static float DetermineTemperature(float rUA, float totalLum, float albedo)
    {
        if (rUA <= 0f) return 50f;
        return Mathf.Clamp(
            278f * Mathf.Pow(totalLum, 0.25f)
                 * Mathf.Pow(Mathf.Max(0f, 1f - albedo), 0.25f)
                 / Mathf.Sqrt(rUA),
            50f, 5000f);
    }

    private static float DetermineDensity(string t)
    {
        switch (t)
        {
            case "Rocheuse":              return 5f;
            case "Gazeuse":               return 1.5f;
            case "Glacée":                return 2f;
            case "Ceinture d'astéroïdes": return 3f;
            default:                      return 1f;
        }
    }

    /// <summary>
    /// Radius in solar radii. Real planet radius doesn't scale linearly with mass (a gas giant's radius is
    /// famously almost flat across a huge mass range - Saturn is a third of Jupiter's mass but nearly the
    /// same size), so this only nudges size mildly with mass within each type's own realistic radius band,
    /// rather than multiplying the raw (now much smaller, physically-scaled) mass value directly.
    /// </summary>
    private static float DetermineNormalizedSize(string t, float mass)
    {
        switch (t)
        {
            case "Gazeuse": // ~0.6-1.3 Jupiter radii (0.0617-0.1336 solar radii)
                return Mathf.Lerp(0.06f, 0.13f,
                    Mathf.InverseLerp(GameConstants.GAS_GIANT_MASS_MIN, GameConstants.GAS_GIANT_MASS_MAX, mass));
            case "Rocheuse": // ~0.4-1.7 Earth radii (0.00366-0.01557 solar radii)
                return Mathf.Lerp(0.0037f, 0.0155f,
                    Mathf.InverseLerp(GameConstants.ROCKY_MASS_MIN, GameConstants.ROCKY_MASS_MAX, mass));
            case "Glacée": // ~0.7 Earth radii up to a small ice giant (~4.5 Earth radii)
                return Mathf.Lerp(0.0064f, 0.041f,
                    Mathf.InverseLerp(GameConstants.ICY_MASS_MIN, GameConstants.ICY_MASS_MAX, mass));
            case "Ceinture d'astéroïdes":
                return 0.001f; // token size - rendered as a marker, not a real sphere
            default:
                return 0.01f;
        }
    }

    /// <summary>
    /// Real "variety" comes from here: a rocky world's temperature + retained atmosphere already tell you
    /// whether it reads as a dead rock, a Venus-style hellscape, a snowball, or something plausibly
    /// temperate - splitting the old flat "Rocheuse" bucket into classes that actually differ. Same idea for
    /// icy/gas bodies, split further by mass into giant vs. small-body tiers.
    /// </summary>
    private static string DetermineSurfaceClass(string bodyType, float mass, float surfaceTempK, Atmosphere atmo)
    {
        if (bodyType == "Ceinture d'astéroïdes") return "Belt";

        if (bodyType == "Gazeuse")
        {
            if (surfaceTempK > 1000f) return "Hot Jupiter";
            return mass < 0.1f * GameConstants.JUPITER_MASS_SOLAR ? "Ice Giant" : "Gas Giant";
        }

        if (bodyType == "Glacée")
        {
            if (mass > 5f * GameConstants.EARTH_MASS_SOLAR) return "Ice Giant";
            return (atmo != null && atmo.surfacePressureAtm > 0.02f) ? "Frozen World" : "Icy Rock";
        }

        // Rocheuse (and any moon of that type)
        bool hasAir = atmo != null && atmo.surfacePressureAtm > 0.01f;
        if (!hasAir) return "Barren";
        if (surfaceTempK > 500f) return "Hellscape";
        if (surfaceTempK < 240f) return "Frozen";

        bool hasWater = false;
        if (atmo != null) foreach (var c in atmo.composition) if (c.element == "H2O") { hasWater = true; break; }
        if (surfaceTempK >= 260f && surfaceTempK <= 320f && hasWater) return "Temperate";
        return "Desert";
    }

    private static float GenerateAlbedo(string t, SeededRandom rng)
    {
        float b, v;
        switch (t)
        {
            case "Rocheuse":              b = 0.15f; v = 0.10f; break;
            case "Gazeuse":               b = 0.50f; v = 0.20f; break;
            case "Glacée":                b = 0.70f; v = 0.15f; break;
            case "Ceinture d'astéroïdes": b = 0.05f; v = 0.03f; break;
            default:                      b = 0.30f; v = 0.10f; break;
        }
        return Mathf.Clamp(b + rng.Range(-v, v), 0f, 1f);
    }
    #endregion

    // =========================================================================
    #region Chemical composition and spectrum
    private static List<ChemicalComposition> DetermineChemicalComposition(string bodyType, SeededRandom rng, float metallicityDex = 0f)
    {
        var c = new List<ChemicalComposition>();
        switch (bodyType)
        {
            case "Star":
                c.Add(new ChemicalComposition { element = "H",  percentage = 73.46f });
                c.Add(new ChemicalComposition { element = "He", percentage = 24.85f });
                c.Add(new ChemicalComposition { element = "O",  percentage = 0.77f  });
                c.Add(new ChemicalComposition { element = "C",  percentage = 0.29f  });
                c.Add(new ChemicalComposition { element = "Fe", percentage = 0.16f  });
                break;
            case "Gazeuse":
                c.Add(new ChemicalComposition { element = "H",  percentage = 89.8f });
                c.Add(new ChemicalComposition { element = "He", percentage = 10.2f });
                break;
            case "Rocheuse":
                c.Add(new ChemicalComposition { element = "O",  percentage = 46.6f });
                c.Add(new ChemicalComposition { element = "Si", percentage = 27.7f });
                c.Add(new ChemicalComposition { element = "Fe", percentage = 8.0f  });
                c.Add(new ChemicalComposition { element = "Mg", percentage = 3.6f  });
                c.Add(new ChemicalComposition { element = "Al", percentage = 1.5f  });
                break;
            case "Glacée":
                c.Add(new ChemicalComposition { element = "H", percentage = 80.0f });
                c.Add(new ChemicalComposition { element = "O", percentage = 10.0f });
                c.Add(new ChemicalComposition { element = "C", percentage = 5.0f  });
                c.Add(new ChemicalComposition { element = "N", percentage = 5.0f  });
                break;
            default:
                c.Add(new ChemicalComposition { element = "H",  percentage = 70.0f });
                c.Add(new ChemicalComposition { element = "He", percentage = 28.0f });
                break;
        }

        // Per-body abundance variation (stars vary less than planets).
        float jitter = bodyType == "Star" ? 0.05f : 0.15f;
        foreach (var e in c)
            e.percentage *= rng.Range(1f - jitter, 1f + jitter);

        // Metallicity ([Fe/H]-style dex, 0 = solar): for a star, scales every element heavier than He - the
        // standard definition of stellar metallicity. For a rocky body, scales only Fe - the classic single
        // proxy for "how metal-rich was the material this thing formed from," without disturbing the O/Si/
        // Mg/Al crust percentages (which is a partial list anyway, not meant to sum to 100%).
        if (metallicityDex != 0f)
        {
            float heavyScale = Mathf.Pow(10f, metallicityDex);
            if (bodyType == "Star")
                foreach (var e in c) if (e.element != "H" && e.element != "He") e.percentage *= heavyScale;
            else if (bodyType == "Rocheuse")
                foreach (var e in c) if (e.element == "Fe") e.percentage *= heavyScale;
        }

        // Complete lists are renormalised to 100%; the rocky list is a partial crust list.
        if (bodyType == "Star" || bodyType == "Gazeuse" || bodyType == "Glacée")
        {
            float sum = 0f;
            foreach (var e in c) sum += e.percentage;
            if (sum > 0f) foreach (var e in c) e.percentage = e.percentage * 100f / sum;
        }

        foreach (var e in c) e.percentage = Mathf.Round(e.percentage * 100f) / 100f;
        return c;
    }

    /// <summary>Base intensity scale applied to every line - keeps the numbers in the same rough range the
    /// old hand-picked per-line multipliers used, now that a line's relative strength comes from
    /// SpectralLineTable's per-element `weight` instead of being baked into a separate constant per line.</summary>
    private const float SpectralIntensityScale = 10f;

    private static Spectrum DetermineSpectrum(List<ChemicalComposition> composition)
    {
        Spectrum s = new()
        {
            emissionLines   = new List<SpectralLine>(),
            absorptionLines = new List<SpectralLine>()
        };
        foreach (var e in composition)
        {
            if (!SpectralLineTable.TryGet(e.element, out SpectralLineTable.Line[] lines)) continue;

            foreach (SpectralLineTable.Line line in lines)
            {
                var sl = new SpectralLine
                {
                    wavelength = line.wavelength,
                    intensity  = e.percentage * line.weight * SpectralIntensityScale,
                    species    = e.element
                };
                (line.emission ? s.emissionLines : s.absorptionLines).Add(sl);
            }
        }
        return s;
    }
    #endregion

    // =========================================================================
    #region Unit conversions
    /// <summary>Orbital period in years to simulated seconds.</summary>
    private static double YearsToSimSeconds(float years) => years * (double)GameConstants.SECONDS_PER_YEAR;

    private static float SolarRadiusToGameUnits(float rSol)
        => (rSol * GameConstants.SOLAR_RADIUS_IN_METERS / GameConstants.AU_IN_METERS) * GU;
    #endregion
}