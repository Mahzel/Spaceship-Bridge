using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The home system: our own Solar System, hand-built from real data instead of generated. It is the same for
/// every world seed, which makes it a fixed, known test ground (and the future tutorial's stage).
///
/// Epoch: SimSeconds = 0 is J2000 (2000-01-01 12:00 TT). Planet elements are the JPL/Standish J2000 mean
/// elements (ecliptic and equinox of J2000); the ship's clock simply starts there on every new game.
/// Moons are given in their parent's EQUATOR frame (which is where regular satellites orbit) and rotated into
/// the ecliptic from the parent's IAU pole direction, so Saturn's rings plane, Uranus' sideways system and
/// Triton's retrograde orbit all come out tilted the right way. Moon orbital phases (node, periapsis, mean
/// anomaly) and the dwarf planets' mean anomalies are approximate: good to show where things roughly are,
/// not an ephemeris.
///
/// Physical data (mass, radius, albedo, mean surface / 1-bar temperature, atmosphere) are real values, rounded.
/// Bulk "composition" follows the generator's convention (a partial crust / surface list for rocky bodies,
/// a full list for gas and ice): it is what the spectrometer's identification reports. The visible spectrum of a
/// planet or moon is reflected sunlight plus its atmosphere's molecular bands (see SpectralLineTable).
///
/// Catalogued = already in the Atlas at the start of a game (known bodies are worth nothing to survey again).
/// Haumea, Makemake, Eris and Sedna are deliberately NOT catalogued: they are the tutorial's discoveries.
/// </summary>
public static class SolSystem
{
    public const string Id = "SOL";

    private const double KmPerAu = 1.495978707e8;
    private const double KgPerSun = 1.98847e30;
    private const double KmPerSunRadius = 695700.0;
    private const double Obliquity = 23.439281; // J2000, degrees

    // ---------------------------------------------------------------------------------------------------------
    #region Data
    private enum Frame { Ecliptic, ParentEquator }

    private sealed class Body
    {
        public string name, parent;          // parent null = the Sun (root)
        public NodeKind kind = NodeKind.Planet;
        public string type, surfaceClass;
        public double massKg, radiusKm;
        public float albedo, tempK;          // Bond albedo; mean surface (or 1-bar) temperature
        public bool catalogued = true;

        // Orbit. Heliocentric bodies: a in AU. Moons: a in km, angles in the parent's equator frame.
        public double a, e, i, node, argPeri, meanAnomaly;
        public Frame frame = Frame.Ecliptic;

        public (string el, float pct)[] composition;
        public float pressureAtm;
        public bool envelope;
        public (string gas, float pct)[] atmosphere;
    }

    /// <summary>ROTATION pole (right ascension, declination, degrees, J2000 equatorial) of bodies that carry
    /// moons given in their equator frame, i.e. the pole the moons circle counterclockwise around. That is the IAU
    /// north pole, except for Uranus, which spins retrograde about it: its rotation pole is the IAU pole's
    /// antipode. (Pluto's IAU pole is already the rotational, "positive" pole.)</summary>
    private static readonly Dictionary<string, (double ra, double dec)> Poles = new Dictionary<string, (double, double)>
    {
        ["Jupiter"] = (268.056595, 64.495303),
        ["Saturn"]  = (40.589, 83.537),
        ["Uranus"]  = (77.311, 15.175),   // antipode of the IAU north pole (257.311, -15.175)
        ["Neptune"] = (299.36, 43.46),
        ["Pluto"]   = (132.993, -6.163),
    };

    // Standish J2000 mean elements: a (AU), e, i, L (mean longitude), varpi (long. of perihelion), Omega.
    private static Body Planet(string name, double a, double e, double i, double L, double varpi, double node)
    {
        return new Body
        {
            name = name, a = a, e = e, i = i, node = node,
            argPeri = Wrap(varpi - node), meanAnomaly = Wrap(L - varpi),
        };
    }

    private static Body Moon(string name, string parent, double aKm, double e, double iEq, double node, double argPeri, double m)
    {
        return new Body
        {
            name = name, parent = parent, a = aKm, e = e, i = iEq, node = node, argPeri = argPeri, meanAnomaly = m,
            frame = Frame.ParentEquator,
        };
    }

    private static readonly (string, float)[] NoAir = new (string, float)[0];

    private static List<Body> Bodies()
    {
        var list = new List<Body>();

        // ---- Planets (Standish J2000) ------------------------------------------------------------------
        Body mercury = Planet("Mercury", 0.38709927, 0.20563593, 7.00497902, 252.25032350, 77.45779628, 48.33076593);
        mercury.type = "Rocheuse"; mercury.surfaceClass = "Barren";
        mercury.massKg = 3.3011e23; mercury.radiusKm = 2439.7; mercury.albedo = 0.088f; mercury.tempK = 440f;
        mercury.composition = new[] { ("O", 42f), ("Si", 25f), ("Mg", 12f), ("Al", 7f), ("Ca", 5f), ("Na", 2f), ("Fe", 2f) };
        mercury.atmosphere = NoAir;
        list.Add(mercury);

        Body venus = Planet("Venus", 0.72333566, 0.00677672, 3.39467605, 181.97909950, 131.60246718, 76.67984255);
        venus.type = "Rocheuse"; venus.surfaceClass = "Hellscape";
        venus.massKg = 4.8675e24; venus.radiusKm = 6051.8; venus.albedo = 0.76f; venus.tempK = 737f;
        venus.composition = new[] { ("O", 44f), ("Si", 23f), ("Al", 8f), ("Fe", 7f), ("Ca", 7f), ("Mg", 5f), ("Na", 2f) };
        venus.pressureAtm = 92f;
        venus.atmosphere = new[] { ("CO2", 96.5f), ("N2", 3.5f), ("SO2", 0.015f), ("H2O", 0.002f) };
        list.Add(venus);

        // Earth: the Earth-Moon barycenter's elements, applied to Earth itself (the Moon then orbits Earth).
        Body earth = Planet("Earth", 1.00000261, 0.01671123, -0.00001531, 100.46457166, 102.93768193, 0.0);
        earth.type = "Rocheuse"; earth.surfaceClass = "Temperate";
        earth.massKg = 5.97237e24; earth.radiusKm = 6371.0; earth.albedo = 0.306f; earth.tempK = 288f;
        earth.composition = new[] { ("O", 46.1f), ("Si", 28.2f), ("Al", 8.2f), ("Fe", 5.6f), ("Ca", 4.2f), ("Na", 2.4f), ("Mg", 2.3f) };
        earth.pressureAtm = 1f;
        earth.atmosphere = new[] { ("N2", 78.08f), ("O2", 20.95f), ("Ar", 0.93f), ("H2O", 0.4f), ("CO2", 0.042f) };
        list.Add(earth);

        Body mars = Planet("Mars", 1.52371034, 0.09339410, 1.84969142, -4.55343205, -23.94362959, 49.55953891);
        mars.type = "Rocheuse"; mars.surfaceClass = "Desert";
        mars.massKg = 6.4171e23; mars.radiusKm = 3389.5; mars.albedo = 0.25f; mars.tempK = 210f;
        mars.composition = new[] { ("O", 44f), ("Si", 21f), ("Fe", 14f), ("Mg", 5f), ("Ca", 5f), ("Al", 5f), ("Na", 2f) };
        mars.pressureAtm = 0.006f;
        mars.atmosphere = new[] { ("CO2", 95.3f), ("N2", 2.6f), ("Ar", 1.9f), ("O2", 0.17f), ("H2O", 0.021f) };
        list.Add(mars);

        Body jupiter = Planet("Jupiter", 5.20288700, 0.04838624, 1.30439695, 34.39644051, 14.72847983, 100.47390909);
        jupiter.type = "Gazeuse"; jupiter.surfaceClass = "Gas Giant";
        jupiter.massKg = 1.89819e27; jupiter.radiusKm = 69911; jupiter.albedo = 0.343f; jupiter.tempK = 165f;
        jupiter.composition = new[] { ("H", 89.8f), ("He", 10.2f) };
        jupiter.envelope = true;
        jupiter.atmosphere = new[] { ("H2", 89.8f), ("He", 10.2f), ("CH4", 0.3f), ("NH3", 0.026f) };
        list.Add(jupiter);

        Body saturn = Planet("Saturn", 9.53667594, 0.05386179, 2.48599187, 49.95424423, 92.59887831, 113.66242448);
        saturn.type = "Gazeuse"; saturn.surfaceClass = "Gas Giant";
        saturn.massKg = 5.6834e26; saturn.radiusKm = 58232; saturn.albedo = 0.342f; saturn.tempK = 134f;
        saturn.composition = new[] { ("H", 96.3f), ("He", 3.25f), ("C", 0.45f) };
        saturn.envelope = true;
        saturn.atmosphere = new[] { ("H2", 96.3f), ("He", 3.25f), ("CH4", 0.45f), ("NH3", 0.0125f) };
        list.Add(saturn);

        Body uranus = Planet("Uranus", 19.18916464, 0.04725744, 0.77263783, 313.23810451, 170.95427630, 74.01692503);
        uranus.type = "Gazeuse"; uranus.surfaceClass = "Ice Giant";
        uranus.massKg = 8.6810e25; uranus.radiusKm = 25362; uranus.albedo = 0.30f; uranus.tempK = 76f;
        uranus.composition = new[] { ("H", 82.5f), ("He", 15.2f), ("C", 2.3f) };
        uranus.envelope = true;
        uranus.atmosphere = new[] { ("H2", 82.5f), ("He", 15.2f), ("CH4", 2.3f) };
        list.Add(uranus);

        Body neptune = Planet("Neptune", 30.06992276, 0.00859048, 1.77004347, -55.12002969, 44.96476227, 131.78422574);
        neptune.type = "Gazeuse"; neptune.surfaceClass = "Ice Giant";
        neptune.massKg = 1.02413e26; neptune.radiusKm = 24622; neptune.albedo = 0.29f; neptune.tempK = 72f;
        neptune.composition = new[] { ("H", 80f), ("He", 19f), ("C", 1.5f) };
        neptune.envelope = true;
        neptune.atmosphere = new[] { ("H2", 80f), ("He", 19f), ("CH4", 1.5f) };
        list.Add(neptune);

        // ---- Asteroid belt and dwarf planets --------------------------------------------------------------
        var belt = new Body
        {
            name = "Main Belt", a = 2.7, e = 0.08, i = 9.0, node = 0, argPeri = 0, meanAnomaly = 0,
            type = "Ceinture d'astéroïdes", surfaceClass = "Belt",
            massKg = 2.39e21, radiusKm = 0.001 * KmPerSunRadius, albedo = 0.10f, tempK = 170f,
            composition = new[] { ("O", 40f), ("Si", 20f), ("Fe", 18f), ("Mg", 14f), ("Ca", 1.5f), ("Al", 1.5f) },
            atmosphere = NoAir,
        };
        list.Add(belt);

        var ceres = new Body
        {
            name = "Ceres", a = 2.7675, e = 0.0758, i = 10.593, node = 80.305, argPeri = 73.597, meanAnomaly = 7.0,
            type = "Glacée", surfaceClass = "Icy Rock",
            massKg = 9.3835e20, radiusKm = 469.7, albedo = 0.09f, tempK = 168f,
            composition = new[] { ("O", 38f), ("H", 25f), ("Si", 14f), ("Mg", 10f), ("Fe", 8f), ("C", 5f) },
            atmosphere = NoAir,
        };
        list.Add(ceres);

        // Pluto: Standish J2000 elements.
        Body pluto = Planet("Pluto", 39.48211675, 0.24882730, 17.14001206, 238.92903833, 224.06891629, 110.30393684);
        pluto.type = "Glacée"; pluto.surfaceClass = "Icy Rock";
        pluto.massKg = 1.303e22; pluto.radiusKm = 1188.3; pluto.albedo = 0.72f; pluto.tempK = 44f;
        pluto.composition = new[] { ("H", 45f), ("O", 30f), ("N", 12f), ("C", 13f) };
        pluto.pressureAtm = 1e-5f;
        pluto.atmosphere = new[] { ("N2", 99.4f), ("CH4", 0.5f), ("CO", 0.05f) };
        list.Add(pluto);

        // Trans-Neptunian discoveries: NOT catalogued (tutorial targets). Mean anomalies from their perihelion
        // dates (Haumea ~1992, Makemake ~1881, Eris 2257, Sedna 2076): approximate.
        list.Add(Dwarf("Haumea",   43.18, 0.195, 28.2,  121.9, 240.9,  10.0, 4.006e21, 780, 0.66f, 32f));
        list.Add(Dwarf("Makemake", 45.43, 0.161, 29.0,   79.3, 297.2, 142.0, 3.1e21,   715, 0.77f, 30f));
        list.Add(Dwarf("Eris",     67.86, 0.436, 44.04,  35.95, 151.0, 194.0, 1.6466e22, 1163, 0.96f, 30f));
        list.Add(Dwarf("Sedna",   506.0,  0.855, 11.93, 144.5, 311.4, 357.6, 1.0e21,  500, 0.32f, 12f));

        // ---- Moons (parent equator frame; phases approximate) -----------------------------------------------
        var moon = new Body
        {
            name = "Moon", parent = "Earth", frame = Frame.Ecliptic, // the Moon's orbit is referred to the ecliptic
            a = 384399, e = 0.0549, i = 5.145, node = 125.08, argPeri = 318.15, meanAnomaly = 134.96,
            type = "Rocheuse", surfaceClass = "Barren",
            massKg = 7.342e22, radiusKm = 1737.4, albedo = 0.11f, tempK = 220f,
            composition = new[] { ("O", 43f), ("Si", 21f), ("Al", 10f), ("Ca", 8f), ("Fe", 6f), ("Mg", 5f) },
            atmosphere = NoAir,
        };
        list.Add(moon);

        // Jupiter: the Galileans.
        list.Add(Rocky(Moon("Io", "Jupiter", 421700, 0.0041, 0.036, 43.9, 84.1, 342.0),
                       8.931938e22, 1821.6, 0.63f, 110f, "Volcanic",
                       new[] { ("O", 40f), ("Si", 20f), ("Fe", 15f), ("Mg", 10f), ("S", 8f), ("Na", 2f) },
                       1e-9f, new[] { ("SO2", 100f) }));
        list.Add(Icy(Moon("Europa", "Jupiter", 671034, 0.009, 0.466, 219.1, 88.97, 171.9),
                     4.799844e22, 1560.8, 0.68f, 102f, new[] { ("H", 55f), ("O", 38f), ("Si", 4f), ("Mg", 2f), ("Na", 1f) }));
        list.Add(Icy(Moon("Ganymede", "Jupiter", 1070412, 0.0013, 0.177, 63.55, 192.4, 317.5),
                     1.4819e23, 2634.1, 0.44f, 110f, new[] { ("H", 50f), ("O", 36f), ("Si", 7f), ("Mg", 4f), ("Fe", 3f) }));
        list.Add(Icy(Moon("Callisto", "Jupiter", 1882709, 0.0074, 0.192, 298.8, 52.6, 181.4),
                     1.075938e23, 2410.3, 0.19f, 134f, new[] { ("H", 45f), ("O", 35f), ("Si", 10f), ("Mg", 5f), ("Fe", 5f) }));

        // Saturn: the seven major moons.
        list.Add(Icy(Moon("Mimas",     "Saturn",  185539, 0.0196,  1.574, 173.0,  332.5,  14.8), 3.7493e19, 198.2, 0.96f, 64f, IceMix()));
        list.Add(Icy(Moon("Enceladus", "Saturn",  237948, 0.0047,  0.009, 342.5,    0.0, 199.7), 1.08022e20, 252.1, 0.81f, 75f, IceMix()));
        list.Add(Icy(Moon("Tethys",    "Saturn",  294619, 0.0001,  1.12,  259.8,  262.8, 243.4), 6.17449e20, 531.1, 0.80f, 86f, IceMix()));
        list.Add(Icy(Moon("Dione",     "Saturn",  377396, 0.0022,  0.019, 290.4,  168.8, 322.2), 1.095452e21, 561.4, 0.70f, 87f, IceMix()));
        list.Add(Icy(Moon("Rhea",      "Saturn",  527108, 0.0012,  0.345, 351.0,  256.6, 179.8), 2.306518e21, 763.8, 0.70f, 76f, IceMix()));
        Body titan = Moon("Titan", "Saturn", 1221870, 0.0288, 0.348, 28.1, 180.5, 163.3);
        titan.type = "Glacée"; titan.surfaceClass = "Frozen World";
        titan.massKg = 1.3452e23; titan.radiusKm = 2574.7; titan.albedo = 0.27f; titan.tempK = 94f;
        titan.composition = new[] { ("H", 55f), ("O", 30f), ("C", 8f), ("N", 7f) };
        titan.pressureAtm = 1.45f;
        titan.atmosphere = new[] { ("N2", 98.4f), ("CH4", 1.5f), ("H2", 0.1f) };
        list.Add(titan);
        list.Add(Icy(Moon("Iapetus",   "Saturn", 3560820, 0.0286, 15.47,   81.1,  271.6, 201.8), 1.805635e21, 734.5, 0.30f, 100f,
                     new[] { ("H", 50f), ("O", 35f), ("C", 10f), ("Si", 5f) }));

        // Uranus: the five major moons (the whole system lies in Uranus' sideways equator).
        list.Add(Icy(Moon("Miranda", "Uranus", 129390, 0.0013, 4.338, 326.4,  68.3, 311.3), 6.4e19,   235.8, 0.32f, 60f, IceMix()));
        list.Add(Icy(Moon("Ariel",   "Uranus", 191020, 0.0012, 0.260,  22.4, 115.3,  39.5), 1.251e21, 578.9, 0.39f, 60f, IceMix()));
        list.Add(Icy(Moon("Umbriel", "Uranus", 266300, 0.0039, 0.128,  33.5,  84.7,  12.5), 1.275e21, 584.7, 0.10f, 75f, IceMix()));
        list.Add(Icy(Moon("Titania", "Uranus", 435910, 0.0011, 0.340,  99.8, 284.4,  24.6), 3.4e21,   788.4, 0.17f, 70f, IceMix()));
        list.Add(Icy(Moon("Oberon",  "Uranus", 583520, 0.0014, 0.058, 279.8, 104.4, 283.1), 3.076e21, 761.4, 0.14f, 75f, IceMix()));

        // Neptune: Triton, retrograde.
        Body triton = Moon("Triton", "Neptune", 354759, 0.000016, 156.885, 177.6, 66.1, 352.3);
        triton.type = "Glacée"; triton.surfaceClass = "Icy Rock";
        triton.massKg = 2.1390e22; triton.radiusKm = 1353.4; triton.albedo = 0.76f; triton.tempK = 38f;
        triton.composition = new[] { ("H", 45f), ("O", 30f), ("N", 15f), ("C", 10f) };
        triton.pressureAtm = 1.4e-5f;
        triton.atmosphere = new[] { ("N2", 99.9f), ("CH4", 0.02f) };
        list.Add(triton);

        // Pluto: Charon (in Pluto's equator, which is tipped past 90 degrees).
        list.Add(Icy(Moon("Charon", "Pluto", 19591, 0.0002, 0.08, 223.0, 146.1, 131.1), 1.586e21, 606, 0.25f, 53f, IceMix()));

        return list;
    }

    private static (string, float)[] IceMix() => new[] { ("H", 60f), ("O", 35f), ("Si", 3f), ("C", 2f) };

    private static Body Icy(Body b, double massKg, double radiusKm, float albedo, float tempK, (string, float)[] comp)
    {
        b.type = "Glacée"; b.surfaceClass = "Icy Rock";
        b.massKg = massKg; b.radiusKm = radiusKm; b.albedo = albedo; b.tempK = tempK;
        b.composition = comp; b.atmosphere = NoAir;
        return b;
    }

    private static Body Rocky(Body b, double massKg, double radiusKm, float albedo, float tempK, string surfaceClass,
                              (string, float)[] comp, float pressureAtm, (string, float)[] atmo)
    {
        b.type = "Rocheuse"; b.surfaceClass = surfaceClass;
        b.massKg = massKg; b.radiusKm = radiusKm; b.albedo = albedo; b.tempK = tempK;
        b.composition = comp; b.pressureAtm = pressureAtm; b.atmosphere = atmo;
        return b;
    }

    private static Body Dwarf(string name, double a, double e, double i, double node, double argPeri, double m,
                              double massKg, double radiusKm, float albedo, float tempK)
    {
        return new Body
        {
            name = name, a = a, e = e, i = i, node = node, argPeri = argPeri, meanAnomaly = m,
            type = "Glacée", surfaceClass = "Icy Rock", catalogued = false,
            massKg = massKg, radiusKm = radiusKm, albedo = albedo, tempK = tempK,
            composition = new[] { ("H", 50f), ("O", 30f), ("C", 10f), ("N", 10f) },
            atmosphere = NoAir,
        };
    }
    #endregion

    // ---------------------------------------------------------------------------------------------------------
    #region Build
    /// <summary>Names of the bodies already in the Atlas when a game starts.</summary>
    public static List<string> CataloguedNames()
    {
        var names = new List<string> { "Sun" };
        foreach (Body b in Bodies()) if (b.catalogued) names.Add(b.name);
        return names;
    }

    /// <summary>Parking orbit radius around Earth where every probe starts.</summary>
    public const double StartOrbitKm = 100000.0;

    public static SystemData Build()
    {
        var sys = new SystemData(Id, 0);

        // The Sun (root, no orbit).
        var sun = new NodeData
        {
            index = 0, parent = -1, kind = NodeKind.Star, name = "Sun",
            bodyType = "G", temperature = 5772f, mass = 1f, radiusSol = 1f,
            radiusGame = KmToGame(KmPerSunRadius), starLuminosity = 1f, metallicity = 0f, ageGyr = 4.6f,
            surfaceTemperature = 5772f, surfaceClass = "",
            atmosphere = new Atmosphere(),
        };
        sun.composition = Composition(new[] { ("H", 73.46f), ("He", 24.85f), ("O", 0.77f), ("C", 0.29f), ("Fe", 0.16f),
                                              ("N", 0.09f), ("Si", 0.07f), ("Mg", 0.05f), ("Ca", 0.006f), ("Na", 0.003f) });
        sun.spectrum = SpectralLineTable.BuildStellar(sun.temperature, sun.metallicity);
        sys.nodes.Add(sun);

        var byName = new Dictionary<string, int> { ["Sun"] = 0 };
        var massByName = new Dictionary<string, double> { ["Sun"] = 1.0 };

        foreach (Body b in Bodies())
        {
            int parent = b.parent != null ? byName[b.parent] : 0;
            double massSun = b.massKg / KgPerSun;
            double parentMass = massByName[b.parent ?? "Sun"];

            double aAu = b.parent == null ? b.a : b.a / KmPerAu;
            double inc = b.i, node = b.node, argPeri = b.argPeri;
            if (b.frame == Frame.ParentEquator && Poles.TryGetValue(b.parent, out var pole))
                EquatorToEcliptic(pole.ra, pole.dec, ref inc, ref node, ref argPeri);

            double periodYears = Math.Sqrt(aAu * aAu * aAu / (parentMass + massSun));

            // Equilibrium (blackbody) temperature from the Sun: moons use their planet's distance.
            double sunDistAu = b.parent == null ? aAu : HelioA(sys, parent);
            float teq = (float)(278.0 * Math.Pow(Math.Max(0.0, 1.0 - b.albedo), 0.25) / Math.Sqrt(sunDistAu));

            var atmo = new Atmosphere { surfacePressureAtm = b.envelope ? 0f : b.pressureAtm, isEnvelope = b.envelope };
            foreach (var g in b.atmosphere) atmo.composition.Add(new ChemicalComposition { element = g.gas, percentage = g.pct });

            var n = new NodeData
            {
                index = sys.nodes.Count, parent = parent, kind = NodeKind.Planet, name = b.name,
                bodyType = b.type, surfaceClass = b.surfaceClass,
                mass = (float)massSun, radiusGame = KmToGame(b.radiusKm), radiusSol = (float)(b.radiusKm / KmPerSunRadius),
                density = (float)(b.massKg / (4.0 / 3.0 * Math.PI * Math.Pow(b.radiusKm * 1000.0, 3)) / 1000.0), // g/cm3
                albedo = b.albedo, temperature = teq, surfaceTemperature = b.tempK,
                composition = Composition(b.composition), atmosphere = atmo,
                hasOrbit = true,
                orbit = new OrbitElements
                {
                    semiMajorAxis = (float)(aAu * GameConstants.GAME_UNITS_PER_UA),
                    eccentricity = (float)b.e,
                    inclination = (float)inc,
                    longitudeAscNode = (float)Wrap(node),
                    argumentPeriapsis = (float)Wrap(argPeri),
                    meanAnomalyAtEpoch = (float)Wrap(b.meanAnomaly),
                    orbitalPeriod = periodYears * GameConstants.SECONDS_PER_YEAR,
                },
            };
            n.spectrum = SpectralLineTable.BuildReflected(sun.spectrum, atmo);
            byName[b.name] = n.index;
            massByName[b.name] = massSun;
            sys.nodes.Add(n);
        }

        sys.startNode = byName["Earth"];
        sys.startOrbitRadiusGame = (float)(StartOrbitKm / KmPerAu * GameConstants.GAME_UNITS_PER_UA);
        return sys;
    }

    private static double HelioA(SystemData sys, int index)
    {
        // Walk up to the body orbiting the Sun directly.
        int i = index;
        while (i > 0 && sys.nodes[i].parent > 0) i = sys.nodes[i].parent;
        return i > 0 ? sys.nodes[i].orbit.semiMajorAxis / GameConstants.GAME_UNITS_PER_UA : 1.0;
    }

    private static List<ChemicalComposition> Composition((string el, float pct)[] items)
    {
        var list = new List<ChemicalComposition>(items.Length);
        foreach (var it in items) list.Add(new ChemicalComposition { element = it.el, percentage = it.pct });
        return list;
    }

    private static float KmToGame(double km) => (float)(km / KmPerAu * GameConstants.GAME_UNITS_PER_UA);

    private static double Wrap(double deg)
    {
        deg %= 360.0;
        return deg < 0 ? deg + 360.0 : deg;
    }
    #endregion

    // ---------------------------------------------------------------------------------------------------------
    #region Frames
    /// <summary>
    /// Rotates orbit angles given relative to a body's equator (inclination, node measured from the equator's
    /// ascending node on the ecliptic, argument of periapsis) into the ecliptic frame, from the body's pole
    /// (right ascension / declination, J2000 equatorial).
    /// </summary>
    private static void EquatorToEcliptic(double raDeg, double decDeg, ref double inc, ref double node, ref double argPeri)
    {
        double d2r = Math.PI / 180.0;
        // Pole in equatorial coordinates, then into ecliptic (rotation about X by the obliquity).
        double ra = raDeg * d2r, dec = decDeg * d2r, eps = Obliquity * d2r;
        double px = Math.Cos(dec) * Math.Cos(ra), py = Math.Cos(dec) * Math.Sin(ra), pz = Math.Sin(dec);
        var P = new Vec3d(px, py * Math.Cos(eps) + pz * Math.Sin(eps), -py * Math.Sin(eps) + pz * Math.Cos(eps));

        // Equator frame basis: X' = ascending node of the equator on the ecliptic, Z' = pole.
        Vec3d zEcl = new Vec3d(0, 0, 1);
        Vec3d X = Vec3d.Cross(zEcl, P);
        if (X.Magnitude < 1e-9) X = new Vec3d(1, 0, 0);
        X = X.Normalized;
        Vec3d Y = Vec3d.Cross(P, X);

        // Orbit normal and periapsis direction in the equator frame (standard perifocal construction).
        double i = inc * d2r, O = node * d2r, w = argPeri * d2r;
        Vec3d nEq = new Vec3d(Math.Sin(O) * Math.Sin(i), -Math.Cos(O) * Math.Sin(i), Math.Cos(i));
        Vec3d pEq = new Vec3d(Math.Cos(O) * Math.Cos(w) - Math.Sin(O) * Math.Sin(w) * Math.Cos(i),
                              Math.Sin(O) * Math.Cos(w) + Math.Cos(O) * Math.Sin(w) * Math.Cos(i),
                              Math.Sin(w) * Math.Sin(i));

        Vec3d n = X * nEq.x + Y * nEq.y + P * nEq.z;
        Vec3d p = X * pEq.x + Y * pEq.y + P * pEq.z;

        inc = Math.Acos(Math.Max(-1.0, Math.Min(1.0, n.z))) / d2r;
        Vec3d nodeVec = Vec3d.Cross(zEcl, n);
        if (nodeVec.Magnitude < 1e-9) { node = 0; nodeVec = new Vec3d(1, 0, 0); }
        else { nodeVec = nodeVec.Normalized; node = Math.Atan2(nodeVec.y, nodeVec.x) / d2r; }
        double cw = Vec3d.Dot(nodeVec, p);
        double sw = Vec3d.Dot(Vec3d.Cross(nodeVec, p), n);
        argPeri = Math.Atan2(sw, cw) / d2r;
    }
    #endregion
}
