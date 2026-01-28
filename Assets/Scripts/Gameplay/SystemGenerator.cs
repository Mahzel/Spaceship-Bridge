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
    private const int DEFAULT_BASE_SEED = 645865465;
    private const float GAME_UNITS_PER_UA = 100f;
    private const float SOLAR_RADIUS_IN_METERS = 6.957e8f;
    private const float AU_IN_METERS = 1.496e11f;
    private const float SOLAR_LUMINOSITY = 3.828e26f;
    private readonly char[] LETTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
    private const int POOL_SIZE = 10;
    #endregion

    #region Fields
    [Header("Prefabs")]
    public GameObject starPrefab;
    public GameObject planetPrefab;
    public GameObject playerShipPrefab;

    [Header("Configuration")]
    public int defaultBaseSeed = DEFAULT_BASE_SEED;

    private int baseSeed;
    private string currentSystemID;
    private ObjectPool<Transform> celestialPool;
    private GameObject playerShip;
    #endregion

    #region Unity Methods
    private void Awake()
    {
        baseSeed = PlayerPrefs.GetInt("BaseSeed", defaultBaseSeed);
        celestialPool = new ObjectPool<Transform>(planetPrefab.transform, POOL_SIZE, transform);
    }

    private void Start()
    {
        JumpToNewSystem();
    }
    #endregion

    #region System Generation
    public void JumpToNewSystem()
    {
        string newSystemID = GenerateSystemID(Random.Range(0, 640000));
        Debug.Log($"ID du système généré : {newSystemID}");
        JumpToSystem(newSystemID);
    }

    public void JumpToSystem(string targetSystemID)
    {
        if (IsValidSystemID(targetSystemID))
        {
            ClearCurrentSystem();
            GenerateStarSystem(targetSystemID);
            currentSystemID = targetSystemID;
        }
        else
        {
            Debug.LogError("ID de système invalide !");
        }
    }

    public void SetBaseSeed(int newSeed)
    {
        baseSeed = newSeed;
        PlayerPrefs.SetInt("BaseSeed", baseSeed);
        PlayerPrefs.Save();
        Debug.Log($"BaseSeed mise à jour : {baseSeed}");
    }
    #endregion

    #region ID Generation
    private string GenerateSystemID(int seed)
    {
        Random.InitState(seed);
        char firstLetter = LETTERS[Random.Range(0, 26)];
        char secondLetter = LETTERS[Random.Range(0, 26)];
        int part1 = Random.Range(0, 10);
        int part2 = Random.Range(0, 100);
        int part3 = Random.Range(0, 100000);
        return $"{firstLetter}{secondLetter}-{part1}-{part2:D2}-{part3:D5}";
    }

    private bool IsValidSystemID(string id)
    {
        return Regex.IsMatch(id, @"^[A-Z]{2}-\d-\d{2}-\d{5}$");
    }

    private int HashIDToSeed(string id)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(id));
            int seed = BitConverter.ToInt32(hashBytes, 0);
            return seed & 0x7FFFFFFF;
        }
    }
    #endregion

    #region Orbital Mechanics
    // Calcule la distance minimale pour une orbite en fonction du rayon de l'étoile
    private float CalculateMinimumOrbitalDistance(float starRadiusInGameUnits)
    {
        // La distance minimale est au moins 2 fois le rayon de l'étoile
        return starRadiusInGameUnits * 1.2f;
    }

    // Calcule la largeur de l'orbite en fonction de la masse de la planète et de la masse de l'étoile
    private float CalculateOrbitalWidth(float orbitalRadius, float planetMass, float starMass)
    {
        // Calcul du rayon de la sphère de Hill
        float hillSphereRadius = orbitalRadius * Mathf.Pow(planetMass / (3 * starMass), 1f / 3f);

        // La largeur minimale de l'orbite est proportionnelle à la sphère de Hill
        return hillSphereRadius;
    }

    // Vérifie si une nouvelle orbite chevauche une orbite existante
    private bool DoOrbitsOverlap(float newOrbitalRadius, float newOrbitalWidth, float existingOrbitalRadius, float existingOrbitalWidth)
    {
        float minDistance = Mathf.Abs(newOrbitalRadius - existingOrbitalRadius);
        float minSeparation = (newOrbitalWidth + existingOrbitalWidth) / 2f;

        return minDistance < minSeparation;
    }

    // Calcule la position orbitale initiale en fonction des paramètres orbitaux
    private Vector3 CalculateOrbitalPosition(float radius, float inclination, float eccentricity, float angle)
    {
        float trueAnomaly = angle;
        float radialDistance = radius * (1 - eccentricity * eccentricity) / (1 + eccentricity * Mathf.Cos(Mathf.Deg2Rad * trueAnomaly));

        float x = radialDistance * Mathf.Cos(Mathf.Deg2Rad * trueAnomaly);
        float z = radialDistance * Mathf.Sin(Mathf.Deg2Rad * trueAnomaly);
        float y = radialDistance * Mathf.Sin(Mathf.Deg2Rad * inclination) * Mathf.Sin(Mathf.Deg2Rad * trueAnomaly);

        return new Vector3(x, y, z);
    }
    #endregion

    #region Planet Properties
    // Détermine le type de planète en fonction de sa distance à l'étoile
    private string DeterminePlanetType(float orbitalRadiusInUA, float starTemperature)
    {
        if (orbitalRadiusInUA < 0.72f)
        {
            return "Rocheuse";
        }
        else if (orbitalRadiusInUA < 1.52f)
        {
            return Random.value > 0.7f ? "Ceinture d'astéroïdes" : "Rocheuse";
        }
        else if (orbitalRadiusInUA < 5.2f)
        {
            return "Gazeuse";
        }
        else
        {
            return "Glacée";
        }
    }

    // Calcule la température de la planète en fonction de sa distance à l'étoile et de la température de l'étoile
    private float DetermineTemperature(float orbitalRadiusInUA, float starTemperature)
    {
        float temperature = starTemperature / Mathf.Sqrt(orbitalRadiusInUA);
        return Mathf.Clamp(temperature, 50f, 5000f);
    }

    // Détermine la densité de la planète en fonction de son type
    private float DetermineDensity(string planetType)
    {
        switch (planetType)
        {
            case "Rocheuse":
                return 5f;
            case "Gazeuse":
                return 1.5f;
            case "Glacée":
                return 2f;
            case "Ceinture d'astéroïdes":
                return 3f;
            default:
                return 1f;
        }
    }

    // Détermine la taille normalisée de la planète en fonction de son type et de sa masse
    private float DetermineNormalizedSize(string bodyType, float mass)
    {
        switch (bodyType)
        {
            case "Star":
                return Mathf.Clamp(mass, 0.5f, 5f); // Rayon entre 0.5 et 5 rayons solaires

            case "Gazeuse":
                return Mathf.Clamp(mass * 0.2f, 0.3f, 0.8f); // Rayon entre 0.3 et 0.8 rayons solaires

            case "Rocheuse":
                return Mathf.Clamp(mass * 0.02f, 0.01f, 0.05f); // Rayon entre 0.01 et 0.05 rayons solaires

            case "Glacée":
                return Mathf.Clamp(mass * 0.03f, 0.02f, 0.08f); // Rayon entre 0.02 et 0.08 rayons solaires

            case "Ceinture d'astéroïdes":
                return Mathf.Clamp(mass * 0.005f, 0.001f, 0.005f); // Rayon entre 0.001 et 0.005 rayons solaires

            default:
                return 0.1f;
        }
    }

    // Génère un albedo pour une planète en fonction de son type
    private float GenerateAlbedo(string planetType)
    {
        float baseAlbedo;
        float variation;

        switch (planetType)
        {
            case "Rocheuse":
                baseAlbedo = 0.15f;
                variation = 0.1f;
                break;

            case "Gazeuse":
                baseAlbedo = 0.5f;
                variation = 0.2f;
                break;

            case "Glacée":
                baseAlbedo = 0.7f;
                variation = 0.15f;
                break;

            case "Ceinture d'astéroïdes":
                baseAlbedo = 0.05f;
                variation = 0.03f;
                break;

            default:
                baseAlbedo = 0.3f;
                variation = 0.1f;
                break;
        }

        float randomOffset = Random.Range(-variation, variation);
        float albedo = Mathf.Clamp(baseAlbedo + randomOffset, 0f, 1f);

        return albedo;
    }
    #endregion

    #region Star Properties
    // Randomise une position sur le diagramme HR et retourne la température et la luminosité
    private void RandomizeHRPosition(out float temperature, out float luminosity)
    {
        // Choisir aléatoirement une région du diagramme HR
        float region = Random.value;

        if (region < 0.7f) // Séquence principale (70% des étoiles)
        {
            float mass = Random.Range(0.08f, 20f);
            if (mass < 0.43f)
            {
                temperature = Random.Range(2400f, 3700f);
                luminosity = 0.23f * Mathf.Pow(mass, 2.3f);
            }
            else if (mass < 0.8f)
            {
                temperature = Random.Range(3700f, 5200f);
                luminosity = Mathf.Pow(mass, 4f);
            }
            else if (mass < 1.05f)
            {
                temperature = Random.Range(5200f, 6000f);
                luminosity = Mathf.Pow(mass, 4f);
            }
            else if (mass < 1.4f)
            {
                temperature = Random.Range(6000f, 7500f);
                luminosity = 1.4f * Mathf.Pow(mass, 3.5f);
            }
            else if (mass < 2.1f)
            {
                temperature = Random.Range(7500f, 10000f);
                luminosity = 1.4f * Mathf.Pow(mass, 3.5f);
            }
            else if (mass < 16f)
            {
                temperature = Random.Range(10000f, 30000f);
                luminosity = 1.4f * Mathf.Pow(mass, 3.5f);
            }
            else
            {
                temperature = Random.Range(30000f, 50000f);
                luminosity = 32000f * mass;
            }
        }
        else if (region < 0.85f) // Géantes rouges (15% des étoiles)
        {
            temperature = Random.Range(3500f, 5000f);
            float mass = Random.Range(0.8f, 10f);
            luminosity = Random.Range(10f, 1000f);
        }
        else if (region < 0.95f) // Supergéantes rouges (10% des étoiles)
        {
            temperature = Random.Range(3500f, 4500f);
            float mass = Random.Range(10f, 40f);
            luminosity = Random.Range(1000f, 100000f);
        }
        else // Naines blanches (5% des étoiles)
        {
            temperature = Random.Range(8000f, 100000f);
            float mass = Random.Range(0.17f, 1.4f);
            luminosity = Random.Range(0.001f, 0.1f);
        }
    }

    // Estime la masse d'une étoile en fonction de sa luminosité et de sa température
    private float EstimateStarMass(float luminosity, float temperature)
    {
        // Naines blanches
        if (temperature >= 8000f && luminosity <= 0.1f)
        {
            return Random.Range(0.17f, 1.4f);
        }
        // Naines rouges (M) et étoiles de faible masse
        else if (temperature < 3700f && luminosity < 0.1f)
        {
            return Mathf.Pow(luminosity / 0.23f, 1f / 2.3f);
        }
        // Étoiles de la séquence principale (types K, G, F, A, B, O)
        else if (temperature > 3500f && temperature < 50000f)
        {
            // Séquence principale
            if (luminosity < 1000f)
            {
                return Mathf.Pow(luminosity / 1.4f, 1f / 3.5f);
            }
            // Supergéantes bleues
            else if (temperature >= 10000f && luminosity >= 1000f)
            {
                return Random.Range(10f, 40f);
            }
            else
            {
                return Random.Range(20f, 50f);
            }
        }
        // Géantes rouges
        else if (temperature >= 3500f && temperature <= 5000f && luminosity >= 10f && luminosity <= 1000f)
        {
            return Random.Range(0.8f, 10f);
        }
        // Supergéantes rouges
        else if (temperature >= 3500f && temperature <= 4500f && luminosity >= 1000f)
        {
            return Random.Range(10f, 40f);
        }
        // Hypergéantes rouges
        else if (temperature >= 3500f && temperature <= 4500f && luminosity >= 10000f)
        {
            return Random.Range(20f, 50f);
        }
        // Supergéantes bleues
        else if (temperature >= 10000f && luminosity >= 1000f)
        {
            return Random.Range(10f, 40f);
        }
        // Valeur par défaut
        else
        {
            return 1f;
        }
    }

    // Calcule le rayon d'une étoile en rayons solaires en fonction de sa luminosité et de sa température
    private float CalculateStarRadius(float luminosity, float temperature)
    {
        // Constante de Stefan-Boltzmann
        float sigma = 5.670374419f * Mathf.Pow(10, -8f);

        // Luminosité en watts (L_sun = 3.828e26 W)
        float luminosityInWatts = luminosity * 3.828f * Mathf.Pow(10, 26f);

        // Rayon en mètres
        float radiusInMeters = Mathf.Sqrt(luminosityInWatts / (4f * Mathf.PI * sigma * Mathf.Pow(temperature, 4f)));

        // Rayon en rayons solaires (R_sun = 6.957e8 m)
        float radiusInSolarRadii = radiusInMeters / (6.957f * Mathf.Pow(10, 8f));

        return radiusInSolarRadii;
    }

    // Détermine le type spectral d'une étoile en fonction de sa luminosité et de sa température
    private string DetermineStarType(float luminosity, float temperature)
    {
        // Naines blanches
        if (luminosity < 0.1f && temperature > 8000f)
        {
            return "White Dwarf";
        }
        // Naines rouges (M)
        else if (luminosity < 0.1f && temperature < 3700f)
        {
            return "M";
        }
        // Étoiles de type K
        else if (luminosity < 0.6f && temperature < 5200f)
        {
            return "K";
        }
        // Étoiles de type G
        else if (luminosity < 1.5f && temperature < 6000f)
        {
            return "G";
        }
        // Étoiles de type F
        else if (luminosity < 5f && temperature < 7500f)
        {
            return "F";
        }
        // Étoiles de type A
        else if (luminosity < 20f && temperature < 10000f)
        {
            return "A";
        }
        // Étoiles de type B
        else if (luminosity < 100f && temperature < 30000f)
        {
            return "B";
        }
        // Étoiles de type O
        else if (luminosity < 1000f && temperature >= 30000f)
        {
            return "O";
        }
        // Géantes rouges
        else if (luminosity >= 10f && luminosity < 1000f && temperature < 5000f)
        {
            return "Red Giant";
        }
        // Supergéantes rouges
        else if (luminosity >= 1000f && temperature < 5000f)
        {
            return "Red Supergiant";
        }
        // Hypergéantes rouges
        else if (luminosity >= 10000f && temperature < 5000f)
        {
            return "Red Hypergiant";
        }
        // Supergéantes bleues
        else if (luminosity >= 1000f && temperature >= 10000f)
        {
            return "Blue Supergiant";
        }
        // Hypergéantes bleues
        else if (luminosity >= 10000f && temperature >= 10000f)
        {
            return "Blue Hypergiant";
        }
        // Par défaut, si aucune condition n'est remplie
        else
        {
            return "Unknown";
        }
    }
    #endregion

    #region Chemical Composition and Spectrum
    // Détermine la composition chimique d'un corps céleste en fonction de son type
    private List<ChemicalComposition> DetermineChemicalComposition(string bodyType)
    {
        List<ChemicalComposition> composition = new List<ChemicalComposition>();

        switch (bodyType)
        {
            case "Star":
                // Composition typique d'une étoile (principalement hydrogène et hélium)
                composition.Add(new ChemicalComposition { element = "H", percentage = 73.46f });
                composition.Add(new ChemicalComposition { element = "He", percentage = 24.85f });
                composition.Add(new ChemicalComposition { element = "O", percentage = 0.77f });
                composition.Add(new ChemicalComposition { element = "C", percentage = 0.29f });
                composition.Add(new ChemicalComposition { element = "Fe", percentage = 0.16f });
                break;

            case "Gazeuse":
                // Composition typique d'une géante gazeuse (principalement hydrogène et hélium)
                composition.Add(new ChemicalComposition { element = "H", percentage = 89.8f });
                composition.Add(new ChemicalComposition { element = "He", percentage = 10.2f });
                break;

            case "Rocheuse":
                // Composition typique d'une planète rocheuse (silicates et métaux)
                composition.Add(new ChemicalComposition { element = "O", percentage = 46.6f });
                composition.Add(new ChemicalComposition { element = "Si", percentage = 27.7f });
                composition.Add(new ChemicalComposition { element = "Fe", percentage = 8.0f });
                composition.Add(new ChemicalComposition { element = "Mg", percentage = 3.6f });
                composition.Add(new ChemicalComposition { element = "Al", percentage = 1.5f });
                break;

            case "Glacée":
                // Composition typique d'une planète glacée (eau, méthane, ammoniac)
                composition.Add(new ChemicalComposition { element = "H", percentage = 80.0f });
                composition.Add(new ChemicalComposition { element = "O", percentage = 10.0f });
                composition.Add(new ChemicalComposition { element = "C", percentage = 5.0f });
                composition.Add(new ChemicalComposition { element = "N", percentage = 5.0f });
                break;

            default:
                // Composition par défaut
                composition.Add(new ChemicalComposition { element = "H", percentage = 70.0f });
                composition.Add(new ChemicalComposition { element = "He", percentage = 28.0f });
                break;
        }

        return composition;
    }

    // Détermine le spectre d'un corps céleste en fonction de sa composition chimique
    private Spectrum DetermineSpectrum(List<ChemicalComposition> composition)
    {
        Spectrum spectrum = new Spectrum();
        spectrum.emissionLines = new List<SpectralLine>();
        spectrum.absorptionLines = new List<SpectralLine>();

        foreach (var element in composition)
        {
            // Ajouter des raies spectrales typiques pour chaque élément
            switch (element.element)
            {
                case "H": // Hydrogène
                    spectrum.emissionLines.Add(new SpectralLine { wavelength = 656.3f, intensity = element.percentage * 10f }); // Raie H-alpha
                    spectrum.emissionLines.Add(new SpectralLine { wavelength = 486.1f, intensity = element.percentage * 8f }); // Raie H-beta
                    spectrum.absorptionLines.Add(new SpectralLine { wavelength = 434.0f, intensity = element.percentage * 5f }); // Raie H-gamma
                    break;

                case "He": // Hélium
                    spectrum.emissionLines.Add(new SpectralLine { wavelength = 587.6f, intensity = element.percentage * 5f }); // Raie D3
                    break;

                case "O": // Oxygène
                    spectrum.absorptionLines.Add(new SpectralLine { wavelength = 777.4f, intensity = element.percentage * 3f });
                    break;

                case "C": // Carbone
                    spectrum.absorptionLines.Add(new SpectralLine { wavelength = 477.0f, intensity = element.percentage * 2f });
                    break;

                case "Fe": // Fer
                    spectrum.absorptionLines.Add(new SpectralLine { wavelength = 527.0f, intensity = element.percentage * 4f });
                    break;

                case "Si": // Silicium
                    spectrum.absorptionLines.Add(new SpectralLine { wavelength = 634.7f, intensity = element.percentage * 2f });
                    break;

                case "Mg": // Magnésium
                    spectrum.absorptionLines.Add(new SpectralLine { wavelength = 517.3f, intensity = element.percentage * 2f });
                    break;

                case "Al": // Aluminium
                    spectrum.absorptionLines.Add(new SpectralLine { wavelength = 396.2f, intensity = element.percentage * 2f });
                    break;

                case "N": // Azote
                    spectrum.absorptionLines.Add(new SpectralLine { wavelength = 388.4f, intensity = element.percentage * 2f });
                    break;
            }
        }

        return spectrum;
    }
    #endregion

    #region System Generation
    private void GenerateStarSystem(string systemID)
    {
        if (!IsValidSystemID(systemID))
        {
            Debug.LogError("ID invalide !");
            return;
        }

        ClearCurrentSystem();

        int idSeed = HashIDToSeed(systemID);
        int finalSeed = baseSeed + idSeed;
        Random.InitState(finalSeed);

        Debug.Log($"Génération du système {systemID} avec la seed finale {finalSeed}");

        // Générer une étoile au centre
        Vector3 starPosition = Vector3.zero;
        GameObject star = celestialPool.Get().gameObject;
        star.transform.position = starPosition;
        star.name = $"{systemID} A";

        // Randomiser une position sur le diagramme HR
        float starTemperature, starLuminosity;
        RandomizeHRPosition(out starTemperature, out starLuminosity);

        // Estimer la masse de l'étoile
        float starMass = EstimateStarMass(starLuminosity, starTemperature);

        // Calculer le rayon de l'étoile
        float starRadiusInSolarRadii = CalculateStarRadius(starLuminosity, starTemperature);
        float starSizeInGameUnits = SolarRadiusToGameUnits(starRadiusInSolarRadii);

        // Appliquer la taille à l'étoile
        star.transform.localScale = Vector3.one * starSizeInGameUnits;
        star.tag = "Star";

        // Ajuster la taille du collider de l'étoile
        SphereCollider starCollider = star.GetComponent<SphereCollider>();
        if (starCollider != null)
        {
            starCollider.radius = starSizeInGameUnits;
        }
        else
        {
            Debug.LogWarning("Pas de SphereCollider trouvé sur l'étoile !");
        }

        // Stocker les propriétés de l'étoile dans un composant CelestialBody
        CelestialBody starBody = star.GetComponent<CelestialBody>();
        if (starBody == null)
        {
            starBody = star.AddComponent<CelestialBody>();
        }
        starBody.bodyName = star.name;
        starBody.bodyType = DetermineStarType(starLuminosity, starTemperature);
        starBody.temperature = starTemperature;
        starBody.mass = starMass;
        starBody.radius = starSizeInGameUnits;
        starBody.solRadius = starRadiusInSolarRadii;
        starBody.distance = 0f;
        starBody.starLuminosity = starLuminosity;
        starBody.chemicalComposition = DetermineChemicalComposition("Star");
        starBody.spectrum = DetermineSpectrum(starBody.chemicalComposition);

        // Calculer la distance minimale pour les orbites en fonction du rayon de l'étoile
        float minOrbitalDistanceInGameUnits = CalculateMinimumOrbitalDistance(starSizeInGameUnits);

        // Liste pour stocker les orbites des planètes existantes
        List<(float radius, float width)> planetOrbits = new List<(float, float)>();

        // Générer entre 3 et 10 planètes aléatoires
        int planetCount = Random.Range(3, 10);
        for (int i = 0; i < planetCount; i++)
        {
            bool validOrbit = false;
            float orbitalRadiusInGameUnits = 0f;
            float orbitalWidthInGameUnits = 0f;
            float planetMass = 0f;

            // Essayer de générer une orbite valide
            int attempts = 0;
            while (!validOrbit && attempts < 100)
            {
                attempts++;
                float orbitalRadiusInUA = Random.Range(minOrbitalDistanceInGameUnits / GAME_UNITS_PER_UA, 10f); // Distance en UA
                orbitalRadiusInGameUnits = orbitalRadiusInUA * GAME_UNITS_PER_UA;
                planetMass = Random.Range(0.1f, 5f);
                orbitalWidthInGameUnits = CalculateOrbitalWidth(orbitalRadiusInGameUnits, planetMass, starMass);

                // Vérifier si cette orbite chevauche une autre
                validOrbit = true;
                foreach (var orbit in planetOrbits)
                {
                    if (DoOrbitsOverlap(orbitalRadiusInGameUnits, orbitalWidthInGameUnits, orbit.radius, orbit.width))
                    {
                        validOrbit = false;
                        break;
                    }
                }
            }

            if (!validOrbit)
            {
                Debug.LogWarning($"Impossible de trouver une orbite valide pour la planète {i} après {attempts} tentatives.");
                continue;
            }

            // Ajouter cette orbite à la liste
            planetOrbits.Add((orbitalRadiusInGameUnits, orbitalWidthInGameUnits));
            planetOrbits = planetOrbits.OrderBy(orbit => orbit.radius).ToList();

            // Période orbitale en années
            float orbitalPeriodInYears = Mathf.Sqrt(Mathf.Pow(orbitalRadiusInGameUnits / GAME_UNITS_PER_UA, 3));
            float orbitalPeriodInGameSeconds = YearsToGameSeconds(orbitalPeriodInYears);

            float orbitalInclination = Random.Range(-15f, 15f);
            float orbitalEccentricity = Random.Range(0f, 0.3f);
            float initialAngle = Random.Range(0f, 360f);

            // Calculer la position initiale en fonction des paramètres orbitaux
            Vector3 orbitalPosition = CalculateOrbitalPosition(orbitalRadiusInGameUnits, orbitalInclination, orbitalEccentricity, initialAngle);

            // Récupérer une planète depuis le pool
            GameObject planet = celestialPool.Get().gameObject;
            planet.transform.position = star.transform.position + orbitalPosition;
            planet.transform.parent = star.transform;
            planet.tag = "Planet";

            // Déterminer les propriétés de la planète
            string planetType = DeterminePlanetType(orbitalRadiusInGameUnits / GAME_UNITS_PER_UA, starTemperature);
            float planetTemperature = DetermineTemperature(orbitalRadiusInGameUnits / GAME_UNITS_PER_UA, starTemperature);
            float planetDensity = DetermineDensity(planetType);
            float planetSizeInSolarRadii = DetermineNormalizedSize(planetType, planetMass);
            float planetSizeInGameUnits = SolarRadiusToGameUnits(planetSizeInSolarRadii);
            float planetAlbedo = GenerateAlbedo(planetType);

            // Appliquer la taille à l'objet (scale)
            planet.transform.localScale = Vector3.one * planetSizeInGameUnits;

            // Ajuster la taille du collider de la planète
            SphereCollider planetCollider = planet.GetComponent<SphereCollider>();
            if (planetCollider != null)
            {
                planetCollider.radius = planetSizeInGameUnits;
            }
            else
            {
                Debug.LogWarning("Pas de SphereCollider trouvé sur la planète " + planet.name + " !");
            }

            // Stocker les propriétés dans un composant CelestialBody
            CelestialBody body = planet.GetComponent<CelestialBody>();
            if (body == null)
            {
                body = planet.AddComponent<CelestialBody>();
            }
            body.bodyType = planetType;
            body.temperature = planetTemperature;
            body.mass = planetMass;
            body.distance = orbitalRadiusInGameUnits;
            body.density = planetDensity;
            body.radius = planetSizeInGameUnits;
            body.albedo = planetAlbedo;
            body.chemicalComposition = DetermineChemicalComposition(planetType);
            body.spectrum = DetermineSpectrum(body.chemicalComposition);

            // Configurer les paramètres orbitaux
            body.orbitalPeriod = orbitalPeriodInGameSeconds;
            body.orbitalInclination = orbitalInclination;
            body.orbitalRadius = orbitalRadiusInGameUnits;
            body.orbitalEccentricity = orbitalEccentricity;
            body.centralBody = star.transform;
            body.orbitalAngle = initialAngle;
        }

        List<CelestialBody> children = new List<CelestialBody>();

        // Parcourt tous les enfants directs
        foreach (Transform child in star.transform)
        {
            children.Add(child.GetComponent<CelestialBody>());
        }
        children = children.OrderBy(child => child.orbitalRadius).ToList();
        for (int i = 0; i < children.Count; i++)
        {
            children[i].gameObject.name = star.name + (i + 1);
            children[i].bodyName = children[i].gameObject.name;
            children[i].transform.SetAsLastSibling();
        }

        // Placer le vaisseau du joueur
        PlacePlayerShip(star.transform, Math.Max(planetOrbits.Count > 0 ? planetOrbits[planetOrbits.Count - 1].radius * 1.1f : minOrbitalDistanceInGameUnits * 2f, starSizeInGameUnits * 2f));
    }

    private void PlacePlayerShip(Transform starTransform, float distance)
    {
        // Si le vaisseau du joueur n'existe pas encore, l'instancier
        if (playerShip == null && playerShipPrefab != null)
        {
            playerShip = Instantiate(playerShipPrefab);
        }

        // Positionner le vaisseau du joueur
        if (playerShip != null)
        {
            playerShip.transform.parent = starTransform;
            // Azimut aléatoire
            float randomAzimuth = Random.Range(0f, 360f);

            // Calculer la position du vaisseau
            float x = distance * Mathf.Cos(Mathf.Deg2Rad * randomAzimuth);
            float z = distance * Mathf.Sin(Mathf.Deg2Rad * randomAzimuth);
            Vector3 playerPosition = new Vector3(x, 0, z);

            // Positionner le vaisseau
            playerShip.transform.position = starTransform.position + playerPosition;

            // Orienter le vaisseau vers l'étoile
            playerShip.transform.LookAt(starTransform);
            playerShip.transform.SetAsLastSibling();
        }
        else
        {
            Debug.LogError("Le prefab du vaisseau du joueur n'est pas assigné !");
        }
    }

    private float YearsToGameSeconds(float periodInYears)
    {
        return periodInYears * 10f;
    }

    private float SolarRadiusToGameUnits(float radiusInSolarRadii)
    {
        float radiusInMeters = radiusInSolarRadii * SOLAR_RADIUS_IN_METERS;
        float radiusInAU = radiusInMeters / AU_IN_METERS;
        return radiusInAU * GAME_UNITS_PER_UA;
    }
    #endregion

    #region System Cleanup
    private void ClearCurrentSystem()
    {
        // Désactiver tous les enfants (étoiles et planètes)
        foreach (Transform child in transform)
        {
            if (playerShip != null)
            {
                playerShip.transform.parent = transform;
            }
            if (!(child.CompareTag("PlayerShip")) && child.gameObject.activeInHierarchy)
            {
                while (child.childCount > 0)
                {
                    foreach (Transform subchild in child)
                    {
                        subchild.parent = transform;
                        celestialPool.ReturnToPool(subchild);
                    }
                }
                celestialPool.ReturnToPool(child);
            }
        }
    }
    #endregion
}
