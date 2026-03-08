using UnityEngine;
using Random = UnityEngine.Random;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

public class SystemManager : MonoBehaviour
{
    #region Constants
    private const int    DEFAULT_BASE_SEED      = GameConstants.DEFAULT_BASE_SEED;
    private const float  GAME_UNITS_PER_UA      = GameConstants.GAME_UNITS_PER_UA;
    private const float  SOLAR_RADIUS_IN_METERS = GameConstants.SOLAR_RADIUS_IN_METERS;
    private const float  AU_IN_METERS           = GameConstants.AU_IN_METERS;
    private const float  SOLAR_LUMINOSITY       = GameConstants.SOLAR_LUMINOSITY;
    private readonly char[] LETTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
    private const int POOL_SIZE = GameConstants.POOL_SIZE;
    #endregion

    #region Fields
    [Header("Prefabs")]
    public GameObject starPrefab;
    public GameObject planetPrefab;
    public GameObject playerShipPrefab;

    [Header("Configuration")]
    public int defaultBaseSeed = DEFAULT_BASE_SEED;

    private int    baseSeed;
    private string currentSystemID;
    private ObjectPool<Transform> celestialPool;
    private GameObject playerShip;
    #endregion

    #region Unity Methods
    private void Awake()
    {
        baseSeed      = PlayerPrefs.GetInt("BaseSeed", defaultBaseSeed);
        celestialPool = new ObjectPool<Transform>(planetPrefab.transform, POOL_SIZE, transform);
    }

    private void Start() => JumpToNewSystem();
    #endregion

    #region Public API
    public void JumpToNewSystem()
    {
        string id = GenerateSystemID(Random.Range(0, 640000));
        Debug.Log($"ID du système généré : {id}");
        JumpToSystem(id);
    }

    public void JumpToSystem(string targetID)
    {
        if (!IsValidSystemID(targetID)) { Debug.LogError("ID de système invalide !"); return; }
        ClearCurrentSystem();
        GenerateStarSystem(targetID);
        currentSystemID = targetID;
    }

    public void SetBaseSeed(int newSeed)
    {
        baseSeed = newSeed;
        PlayerPrefs.SetInt("BaseSeed", baseSeed);
        PlayerPrefs.Save();
    }
    #endregion

    // =========================================================================
    #region System Generation — Entry Point
    private void GenerateStarSystem(string systemID)
    {
        if (!IsValidSystemID(systemID)) { Debug.LogError("ID invalide !"); return; }

        ClearCurrentSystem();

        if (playerShip == null && playerShipPrefab != null)
            playerShip = Instantiate(playerShipPrefab);

        Random.InitState(baseSeed + HashIDToSeed(systemID));

        // --- Multiplicité ---
        // 50% simples | 40% binaires | 10% trinaires
        float roll     = Random.value;
        int   starCount = roll < GameConstants.STAR_SINGLE_THRESHOLD ? 1
                        : roll < GameConstants.STAR_BINARY_THRESHOLD ? 2 : 3;

        Transform systemRoot;
        float     totalStarMass;
        float     minOrbitUA;

        if (starCount == 1)
            (systemRoot, totalStarMass, minOrbitUA) = GenerateSingleStar(systemID, Vector3.zero);
        else
            (systemRoot, totalStarMass, minOrbitUA) = GenerateMultipleStars(systemID, Vector3.zero, starCount);

        GeneratePlanets(systemID, systemRoot, totalStarMass, minOrbitUA);

        float lastOrbit = GetLastPlanetOrbit(systemRoot);
        PlacePlayerShip(systemRoot,
            Mathf.Max(lastOrbit * 1.1f, minOrbitUA * GAME_UNITS_PER_UA * 2f));
    }
    #endregion

    // =========================================================================
    #region Star Generation

    /// <summary>Étoile unique fixe au centre.</summary>
    private (Transform root, float mass, float minOrbitUA) GenerateSingleStar(
        string systemID, Vector3 position)
    {
        StarData data  = RandomStarData($"{systemID} A");
        GameObject go  = SpawnStar(data, transform, position);
        go.name        = data.name;

        float minOrbitUA = CalculateMinimumOrbitalDistance(data.radiusGame) / GAME_UNITS_PER_UA;
        return (go.transform, data.mass, minOrbitUA);
    }

    /// <summary>Système binaire ou trinaire avec barycentre(s).</summary>
    private (Transform root, float totalMass, float minOrbitUA) GenerateMultipleStars(
        string systemID, Vector3 position, int count)
    {
        // Barycentre racine — immobile, centre du système
        GameObject rootBC = CreateBarycenter($"{systemID}_BC", transform, position);
        Barycenter bc     = rootBC.GetComponent<Barycenter>();

        float totalMass      = 0f;
        float maxRadiusGame  = 0f;

        if (count == 2)
        {
            float separationUA = Random.Range(GameConstants.BINARY_SEPARATION_MIN_UA, GameConstants.BINARY_SEPARATION_MAX_UA);
            var (starA, starB) = GenerateBinaryPair(
                systemID, rootBC.transform, separationUA, "A", "B");

            bc.bodies = new List<CelestialBody> { starA, starB };
            bc.UpdatePosition();

            totalMass     = starA.mass + starB.mass;
            maxRadiusGame = Mathf.Max(starA.radius, starB.radius);
        }
        else // count == 3
        {
            // Paire AB + étoile C lointaine
            float sepAB = Random.Range(GameConstants.TRINARY_INNER_SEPARATION_MIN_UA, GameConstants.TRINARY_INNER_SEPARATION_MAX_UA);
            float sepC  = Random.Range(GameConstants.TRINARY_OUTER_SEPARATION_MIN_UA, GameConstants.TRINARY_OUTER_SEPARATION_MAX_UA);

            GameObject abBC  = CreateBarycenter($"{systemID}_BC_AB", rootBC.transform, position);
            Barycenter abBCc = abBC.GetComponent<Barycenter>();

            var (starA, starB) = GenerateBinaryPair(
                systemID, abBC.transform, sepAB, "A", "B");

            abBCc.bodies = new List<CelestialBody> { starA, starB };
            abBCc.UpdatePosition();

            float massAB = starA.mass + starB.mass;

            StarData dataC = RandomStarData($"{systemID} C");
            GameObject goC = SpawnStar(dataC, rootBC.transform, position);

            // AB orbite autour du barycentre racine
            ConfigureOrbit(abBC.GetComponent<OrbitalComponent>() ?? abBC.AddComponent<OrbitalComponent>(),
                rootBC.transform, sepC, massAB, dataC.mass, isBodyA: true);

            // C orbite autour du barycentre racine (côté opposé)
            ConfigureOrbit(goC.GetComponent<OrbitalComponent>() ?? goC.AddComponent<OrbitalComponent>(),
                rootBC.transform, sepC, massAB, dataC.mass, isBodyA: false);

            CelestialBody starC = goC.GetComponent<CelestialBody>();
            bc.bodies = new List<CelestialBody> { starA, starB, starC };
            bc.UpdatePosition();

            totalMass     = massAB + dataC.mass;
            maxRadiusGame = Mathf.Max(starA.radius, Mathf.Max(starB.radius, dataC.radiusGame));
        }

        // Planètes circumbinaires : règle P-type, a > 3.5 × séparation
        float minOrbitUA = Mathf.Max(
            CalculateMinimumOrbitalDistance(maxRadiusGame) / GAME_UNITS_PER_UA,
            GameConstants.CIRCUMBINARY_MIN_ORBIT_FACTOR);

        return (rootBC.transform, totalMass, minOrbitUA);
    }

    /// <summary>Génère une paire d'étoiles en orbite mutuelle autour d'un barycentre.</summary>
    private (CelestialBody starA, CelestialBody starB) GenerateBinaryPair(
        string systemID, Transform barycenter,
        float separationUA, string suffA, string suffB)
    {
        StarData dA = RandomStarData($"{systemID} {suffA}");
        StarData dB = RandomStarData($"{systemID} {suffB}");

        GameObject goA = SpawnStar(dA, barycenter, barycenter.position);
        GameObject goB = SpawnStar(dB, barycenter, barycenter.position);

        OrbitalComponent orbA = goA.GetComponent<OrbitalComponent>() ?? goA.AddComponent<OrbitalComponent>();
        OrbitalComponent orbB = goB.GetComponent<OrbitalComponent>() ?? goB.AddComponent<OrbitalComponent>();

        ConfigureOrbit(orbA, barycenter, separationUA, dA.mass, dB.mass, isBodyA: true);
        ConfigureOrbit(orbB, barycenter, separationUA, dA.mass, dB.mass, isBodyA: false);

        return (goA.GetComponent<CelestialBody>(), goB.GetComponent<CelestialBody>());
    }

    /// <summary>
    /// Configure un OrbitalComponent pour un membre d'une paire.
    /// isBodyA=true → demi-grand axe pondéré par m_B, argument ω = 0°
    /// isBodyA=false → demi-grand axe pondéré par m_A, argument ω = 180° (orbites opposées)
    /// </summary>
    private void ConfigureOrbit(OrbitalComponent orb, Transform focus,
        float separationUA, float massA, float massB, bool isBodyA)
    {
        float total  = massA + massB;
        float sma    = separationUA * GAME_UNITS_PER_UA
                     * (isBodyA ? massB / total : massA / total);
        float period = Mathf.Sqrt(Mathf.Pow(separationUA, 3f) / total);

        orb.focus              = focus;
        orb.semiMajorAxis      = sma;
        orb.eccentricity       = Random.Range(0f, GameConstants.STAR_ECCENTRICITY_MAX);
        orb.inclination        = Random.Range(-GameConstants.STAR_INCLINATION_MAX, GameConstants.STAR_INCLINATION_MAX);
        orb.longitudeAscNode   = Random.Range(0f, 360f);
        orb.argumentPeriapsis  = isBodyA ? 0f   : 180f;
        orb.meanAnomalyAtEpoch = isBodyA ? 0f   : 180f;
        orb.orbitalPeriod      = YearsToGameSeconds(period);
        orb.Reset();
    }

    // --- Helpers ---

    private struct StarData
    {
        public string name;
        public float  temperature, luminosity, mass, radiusGame, radiusSol;
    }

    private StarData RandomStarData(string name)
    {
        float temp, lum;
        RandomizeHRPosition(out temp, out lum);
        float mass   = EstimateStarMass(lum, temp);
        float rSol   = CalculateStarRadius(lum, temp);
        float rGame  = SolarRadiusToGameUnits(rSol);
        return new StarData { name = name, temperature = temp, luminosity = lum,
                              mass = mass, radiusGame = rGame, radiusSol = rSol };
    }

    private GameObject SpawnStar(StarData d, Transform parent, Vector3 position)
    {
        GameObject go = celestialPool.Get().gameObject;
        go.GetComponent<CelestialBody>()?.Reset();
        go.transform.SetParent(parent);
        go.transform.position  = position;
        go.transform.localScale = Vector3.one * d.radiusGame;
        go.name = d.name;
        go.tag  = "Star";

        SphereCollider col = go.GetComponent<SphereCollider>();
        if (col != null) col.radius = d.radiusGame;

        CelestialBody body = go.GetComponent<CelestialBody>();
        if (body == null) body = go.AddComponent<CelestialBody>();
        body.bodyName            = d.name;
        body.bodyType            = DetermineStarType(d.luminosity, d.temperature);
        body.temperature         = d.temperature;
        body.mass                = d.mass;
        body.radius              = d.radiusGame;
        body.solRadius           = d.radiusSol;
        body.starLuminosity      = d.luminosity;
        body.chemicalComposition = DetermineChemicalComposition("Star");
        body.spectrum            = DetermineSpectrum(body.chemicalComposition);
        return go;
    }

    private GameObject CreateBarycenter(string name, Transform parent, Vector3 position)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.AddComponent<Barycenter>();
        // OrbitalComponent ajouté à la demande dans GenerateMultipleStars
        return go;
    }
    #endregion

    // =========================================================================
    #region Planet Generation
    private void GeneratePlanets(string systemID, Transform systemRoot,
        float totalStarMass, float minOrbitUA)
    {
        float totalLuminosity = GetTotalSystemLuminosity(systemRoot);
        List<(float radius, float width)> orbits = new List<(float, float)>();

        // Per-parent planet counters so each star gets its own numbering (A-1, A-2, B-1...)
        Dictionary<Transform, int> parentPlanetIndex = new Dictionary<Transform, int>();

        int planetCount = Random.Range(GameConstants.PLANET_COUNT_MIN, GameConstants.PLANET_COUNT_MAX);

        for (int i = 0; i < planetCount; i++)
        {
            bool  valid   = false;
            float rGame   = 0f;
            float width   = 0f;
            float pMass   = 0f;
            int   tries   = 0;

            while (!valid && tries < GameConstants.PLANET_ORBIT_MAX_TRIES)
            {
                tries++;
                float rUA = Random.Range(minOrbitUA, minOrbitUA + GameConstants.PLANET_ORBIT_SPREAD_UA);
                rGame  = rUA * GAME_UNITS_PER_UA;
                pMass  = Random.Range(GameConstants.PLANET_MASS_MIN, GameConstants.PLANET_MASS_MAX);
                width  = CalculateOrbitalWidth(rGame, pMass, totalStarMass);
                valid  = orbits.All(o => !DoOrbitsOverlap(rGame, width, o.radius, o.width));
            }

            if (!valid) { Debug.LogWarning($"Pas d'orbite valide pour planète {i}."); continue; }

            orbits.Add((rGame, width));
            orbits = orbits.OrderBy(o => o.radius).ToList();

            float rUA2       = rGame / GAME_UNITS_PER_UA;
            float period     = Mathf.Sqrt(Mathf.Pow(rUA2, 3f) / totalStarMass);
            string pType     = DeterminePlanetType(rUA2, 0f);
            float  albedo    = GenerateAlbedo(pType);
            float  temp      = DetermineTemperature(rUA2, totalLuminosity, albedo);
            float  density   = DetermineDensity(pType);
            float  sizeSol   = DetermineNormalizedSize(pType, pMass);
            float  sizeGame  = SolarRadiusToGameUnits(sizeSol);

            // Hierarchy & naming: find the most specific parent (star or barycenter)
            Transform orbitParent = FindOrbitParent(systemRoot, rGame);
            string    parentShort = orbitParent.name.Replace(systemID, "").Trim();
            if (string.IsNullOrEmpty(parentShort)) parentShort = orbitParent.name;

            if (!parentPlanetIndex.ContainsKey(orbitParent))
                parentPlanetIndex[orbitParent] = 1;
            int planetNum = parentPlanetIndex[orbitParent]++;

            string planetName = $"{systemID} {parentShort}-{planetNum}";

            GameObject planet = celestialPool.Get().gameObject;
            planet.GetComponent<CelestialBody>()?.Reset();
            planet.transform.SetParent(orbitParent);
            planet.transform.position   = orbitParent.position;
            planet.transform.localScale = Vector3.one * sizeGame;
            planet.tag  = "Planet";
            planet.name = planetName;

            SphereCollider col = planet.GetComponent<SphereCollider>();
            if (col != null) col.radius = sizeGame;

            CelestialBody body = planet.GetComponent<CelestialBody>() ?? planet.AddComponent<CelestialBody>();
            body.bodyName            = planet.name;
            body.bodyType            = pType;
            body.temperature         = temp;
            body.mass                = pMass;
            body.density             = density;
            body.radius              = sizeGame;
            body.albedo              = albedo;
            body.chemicalComposition = DetermineChemicalComposition(pType);
            body.spectrum            = DetermineSpectrum(body.chemicalComposition);

            OrbitalComponent orb = planet.GetComponent<OrbitalComponent>() ?? planet.AddComponent<OrbitalComponent>();
            orb.focus              = orbitParent;
            orb.semiMajorAxis      = rGame;
            orb.eccentricity       = Random.Range(0f, GameConstants.PLANET_ECCENTRICITY_MAX);
            orb.inclination        = Random.Range(-GameConstants.PLANET_INCLINATION_MAX, GameConstants.PLANET_INCLINATION_MAX);
            orb.longitudeAscNode   = Random.Range(0f, 360f);
            orb.argumentPeriapsis  = Random.Range(0f, 360f);
            orb.meanAnomalyAtEpoch = Random.Range(0f, 360f);
            orb.orbitalPeriod      = YearsToGameSeconds(period);
            orb.Reset();
        }
    }

    /// <summary>
    /// Finds the most appropriate Transform to parent a planet under, given its orbital radius.
    /// Single-star: always the star. Multi-star: S-type planets go under the nearest star,
    /// circumbinary/circumtriple stay under the root barycenter.
    /// S-type condition: planetOrbit <= STYPE_ORBIT_THRESHOLD * star's own orbital radius.
    /// </summary>
    private Transform FindOrbitParent(Transform systemRoot, float planetOrbitGame)
    {
        List<(Transform t, float starOrbit)> stars = new List<(Transform, float)>();

        foreach (Transform child in systemRoot)
        {
            CelestialBody cb = child.GetComponent<CelestialBody>();
            if (cb != null && cb.starLuminosity > 0f)
            {
                OrbitalComponent oc = child.GetComponent<OrbitalComponent>();
                stars.Add((child, oc != null ? oc.semiMajorAxis : 0f));
                continue;
            }
            Barycenter bc = child.GetComponent<Barycenter>();
            if (bc != null)
            {
                foreach (Transform grandChild in child)
                {
                    CelestialBody gcb = grandChild.GetComponent<CelestialBody>();
                    if (gcb != null && gcb.starLuminosity > 0f)
                    {
                        OrbitalComponent oc = grandChild.GetComponent<OrbitalComponent>();
                        stars.Add((grandChild, oc != null ? oc.semiMajorAxis : 0f));
                    }
                }
            }
        }

        if (stars.Count == 1)
            return stars[0].t;

        foreach (var (starTransform, starOrbit) in stars)
        {
            if (starOrbit > 0f && planetOrbitGame <= GameConstants.STYPE_ORBIT_THRESHOLD * starOrbit)
                return starTransform;
        }

        return systemRoot;
    }

    private float GetTotalSystemLuminosity(Transform root)
    {
        float total = 0f;
        foreach (CelestialBody b in root.GetComponentsInChildren<CelestialBody>())
            if (b.starLuminosity > 0f) total += b.starLuminosity;
        return Mathf.Max(total, 0.0001f);
    }

    private float GetLastPlanetOrbit(Transform root)
    {
        float last = 0f;
        foreach (OrbitalComponent orb in root.GetComponentsInChildren<OrbitalComponent>())
        {
            CelestialBody b = orb.GetComponent<CelestialBody>();
            if (b != null && b.starLuminosity == 0f)
                last = Mathf.Max(last, orb.semiMajorAxis);
        }
        return last;
    }
    #endregion

    // =========================================================================
    #region Player Placement
    private void PlacePlayerShip(Transform systemRoot, float distance)
    {
        if (playerShip == null) { Debug.LogError("Vaisseau joueur non assigné !"); return; }
        playerShip.transform.SetParent(systemRoot);
        float az = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        playerShip.transform.position = systemRoot.position
            + new Vector3(distance * Mathf.Cos(az), 0f, distance * Mathf.Sin(az));
        playerShip.transform.LookAt(systemRoot);
        playerShip.transform.SetAsLastSibling();
    }
    #endregion

    // =========================================================================
    #region System Cleanup
    private void ClearCurrentSystem()
    {
        List<Transform> children = new List<Transform>();
        foreach (Transform child in transform) children.Add(child);

        foreach (Transform child in children)
        {
            if (playerShip != null) playerShip.transform.parent = transform;
            if (!child.CompareTag("PlayerShip") && child.gameObject.activeInHierarchy)
                ReturnToPoolRecursive(child);
        }
    }

    private void ReturnToPoolRecursive(Transform t)
    {
        List<Transform> children = new List<Transform>();
        foreach (Transform c in t) children.Add(c);
        foreach (Transform c in children) ReturnToPoolRecursive(c);

        CelestialBody body = t.GetComponent<CelestialBody>();
        if (body != null)
        {
            body.Reset();
            t.parent = transform;
            celestialPool.ReturnToPool(t);
        }
        else
        {
            Destroy(t.gameObject); // barycentre ou autre objet non-poolé
        }
    }
    #endregion

    // =========================================================================
    #region Orbital Mechanics Helpers
    private float CalculateMinimumOrbitalDistance(float starRadiusGame) => starRadiusGame * 1.2f;

    private float CalculateOrbitalWidth(float r, float planetMass, float starMass)
        => r * Mathf.Pow(planetMass / (3f * starMass), 1f / 3f);

    private bool DoOrbitsOverlap(float r1, float w1, float r2, float w2)
        => Mathf.Abs(r1 - r2) < (w1 + w2) / 2f;
    #endregion

    // =========================================================================
    #region ID Generation
    private string GenerateSystemID(int seed)
    {
        Random.InitState(seed);
        return $"{LETTERS[Random.Range(0,26)]}{LETTERS[Random.Range(0,26)]}"
             + $"-{Random.Range(0,10)}-{Random.Range(0,100):D2}-{Random.Range(0,100000):D5}";
    }

    private bool IsValidSystemID(string id)
        => Regex.IsMatch(id, @"^[A-Z]{2}-\d-\d{2}-\d{5}$");

    private int HashIDToSeed(string id)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(id));
            return BitConverter.ToInt32(h, 0) & 0x7FFFFFFF;
        }
    }
    #endregion

    // =========================================================================
    #region Star Properties
    private void RandomizeHRPosition(out float temperature, out float luminosity)
    {
        float r = Random.value;
        if (r < 0.70f)
        {
            float m = Random.Range(0.08f, 20f);
            if      (m < 0.43f) { temperature = Random.Range(2400f,  3700f);  luminosity = 0.23f * Mathf.Pow(m, 2.3f); }
            else if (m < 0.80f) { temperature = Random.Range(3700f,  5200f);  luminosity = Mathf.Pow(m, 4f); }
            else if (m < 1.05f) { temperature = Random.Range(5200f,  6000f);  luminosity = Mathf.Pow(m, 4f); }
            else if (m < 1.40f) { temperature = Random.Range(6000f,  7500f);  luminosity = 1.4f * Mathf.Pow(m, 3.5f); }
            else if (m < 2.10f) { temperature = Random.Range(7500f,  10000f); luminosity = 1.4f * Mathf.Pow(m, 3.5f); }
            else if (m < 16f)   { temperature = Random.Range(10000f, 30000f); luminosity = 1.4f * Mathf.Pow(m, 3.5f); }
            else                { temperature = Random.Range(30000f, 50000f); luminosity = 32000f * m; }
        }
        else if (r < 0.85f) { temperature = Random.Range(3500f,  5000f);  luminosity = Random.Range(10f,    1000f); }
        else if (r < 0.95f) { temperature = Random.Range(3500f,  4500f);  luminosity = Random.Range(1000f,  100000f); }
        else                { temperature = Random.Range(8000f,  100000f); luminosity = Random.Range(0.001f, 0.1f); }
    }

    private float EstimateStarMass(float lum, float temp)
    {
        if (temp >= 8000f && lum <= 0.1f)                                          return Random.Range(0.17f, 1.4f);
        if (temp < 3700f  && lum < 0.1f)                                           return Mathf.Pow(lum / 0.23f, 1f / 2.3f);
        if (temp > 3500f  && temp < 50000f && lum < 1000f)                         return Mathf.Pow(lum / 1.4f, 1f / 3.5f);
        if (temp >= 10000f && lum >= 1000f)                                         return Random.Range(10f, 40f);
        if (temp >= 3500f && temp <= 5000f && lum >= 10f && lum <= 1000f)          return Random.Range(0.8f, 10f);
        if (temp >= 3500f && temp <= 4500f && lum >= 1000f)                         return Random.Range(10f, 40f);
        return 1f;
    }

    private float CalculateStarRadius(float lum, float temp)
    {
        float lumW = lum * SOLAR_LUMINOSITY;
        float rM   = Mathf.Sqrt(lumW / (4f * Mathf.PI * 5.670374419e-8f * Mathf.Pow(temp, 4f)));
        return rM / SOLAR_RADIUS_IN_METERS;
    }

    private string DetermineStarType(float lum, float temp)
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
    #region Planet Properties
    private string DeterminePlanetType(float rUA, float _)
    {
        if      (rUA < 0.72f) return "Rocheuse";
        else if (rUA < 1.52f) return Random.value > 0.7f ? "Ceinture d'astéroïdes" : "Rocheuse";
        else if (rUA < 5.2f)  return "Gazeuse";
        else                  return "Glacée";
    }

    private float DetermineTemperature(float rUA, float totalLum, float albedo)
    {
        if (rUA <= 0f) return 50f;
        return Mathf.Clamp(
            278f * Mathf.Pow(totalLum, 0.25f)
                 * Mathf.Pow(Mathf.Max(0f, 1f - albedo), 0.25f)
                 / Mathf.Sqrt(rUA),
            50f, 5000f);
    }

    private float DetermineDensity(string t)
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

    private float DetermineNormalizedSize(string t, float mass)
    {
        switch (t)
        {
            case "Gazeuse":               return Mathf.Clamp(mass * 0.2f,   0.3f,   0.8f);
            case "Rocheuse":              return Mathf.Clamp(mass * 0.02f,  0.01f,  0.05f);
            case "Glacée":                return Mathf.Clamp(mass * 0.03f,  0.02f,  0.08f);
            case "Ceinture d'astéroïdes": return Mathf.Clamp(mass * 0.005f, 0.001f, 0.005f);
            default:                      return 0.1f;
        }
    }

    private float GenerateAlbedo(string t)
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
        return Mathf.Clamp(b + Random.Range(-v, v), 0f, 1f);
    }
    #endregion

    // =========================================================================
    #region Chemical Composition and Spectrum
    private List<ChemicalComposition> DetermineChemicalComposition(string bodyType)
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
        return c;
    }

    private Spectrum DetermineSpectrum(List<ChemicalComposition> composition)
    {
        Spectrum s = new Spectrum
        {
            emissionLines   = new List<SpectralLine>(),
            absorptionLines = new List<SpectralLine>()
        };
        foreach (var e in composition)
        {
            switch (e.element)
            {
                case "H":  s.emissionLines.Add  (new SpectralLine { wavelength = 656.3f, intensity = e.percentage * 10f });
                           s.emissionLines.Add  (new SpectralLine { wavelength = 486.1f, intensity = e.percentage *  8f });
                           s.absorptionLines.Add(new SpectralLine { wavelength = 434.0f, intensity = e.percentage *  5f }); break;
                case "He": s.emissionLines.Add  (new SpectralLine { wavelength = 587.6f, intensity = e.percentage *  5f }); break;
                case "O":  s.absorptionLines.Add(new SpectralLine { wavelength = 777.4f, intensity = e.percentage *  3f }); break;
                case "C":  s.absorptionLines.Add(new SpectralLine { wavelength = 477.0f, intensity = e.percentage *  2f }); break;
                case "Fe": s.absorptionLines.Add(new SpectralLine { wavelength = 527.0f, intensity = e.percentage *  4f }); break;
                case "Si": s.absorptionLines.Add(new SpectralLine { wavelength = 634.7f, intensity = e.percentage *  2f }); break;
                case "Mg": s.absorptionLines.Add(new SpectralLine { wavelength = 517.3f, intensity = e.percentage *  2f }); break;
                case "Al": s.absorptionLines.Add(new SpectralLine { wavelength = 396.2f, intensity = e.percentage *  2f }); break;
                case "N":  s.absorptionLines.Add(new SpectralLine { wavelength = 388.4f, intensity = e.percentage *  2f }); break;
            }
        }
        return s;
    }
    #endregion

    // =========================================================================
    #region Unit Conversions
    /// <summary>
    /// Converts an orbital period in years to real seconds.
    /// OrbitalComponent multiplies by GameConstants.TIME_MULTIPLIER at runtime.
    /// </summary>
    private float YearsToGameSeconds(float years) => years * GameConstants.SECONDS_PER_YEAR;

    private float SolarRadiusToGameUnits(float rSol)
        => (rSol * SOLAR_RADIUS_IN_METERS / AU_IN_METERS) * GAME_UNITS_PER_UA;
    #endregion
}