using UnityEngine;
using Random = UnityEngine.Random;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public class SystemManager : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject starPrefab;
    public GameObject planetPrefab;

    [Header("Configuration")]
    public int defaultBaseSeed = 645865465;

    private int baseSeed;
    private string currentSystemID;
    private char[] letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
    private int poolSize = 10;
    private ObjectPool<Transform> celestialPool;
    private GameObject playerShip;
    public GameObject playerShipPrefab;

    // Constantes pour les conversions d'unités
    private const float SOLAR_RADIUS_IN_AU = 0.00465f; // 1 rayon solaire = 0.00465 UA

    public void Awake()
    {
        baseSeed = PlayerPrefs.GetInt("BaseSeed", defaultBaseSeed);
        celestialPool = new ObjectPool<Transform>(planetPrefab.transform, poolSize, transform);
    }

    void Start()
    {
        JumpToNewSystem();
    }

    // Calcule la distance minimale pour une orbite en fonction du rayon de l'étoile
    float CalculateMinimumOrbitalDistance(float starRadiusInGameUnits)
    {
        // La distance minimale est au moins 2 fois le rayon de l'étoile
        return starRadiusInGameUnits * 1.1f;
    }

    // Calcule la largeur de l'orbite en fonction de la masse de la planète et de la masse de l'étoile
    float CalculateOrbitalWidth(float orbitalRadius, float planetMass, float starMass)
    {
        // Calcul du rayon de la sphère de Hill
        float hillSphereRadius = orbitalRadius * Mathf.Pow(planetMass / (3 * starMass), 1f / 3f);

        // La largeur minimale de l'orbite est proportionnelle à la sphère de Hill
        // On utilise un facteur de 5 pour garantir une séparation suffisante
        return hillSphereRadius;
    }

    // Vérifie si une nouvelle orbite chevauche une orbite existante
    bool DoOrbitsOverlap(float newOrbitalRadius, float newOrbitalWidth, float existingOrbitalRadius, float existingOrbitalWidth)
    {
        float minDistance = Mathf.Abs(newOrbitalRadius - existingOrbitalRadius);
        float minSeparation = (newOrbitalWidth + existingOrbitalWidth) / 2f;

        return minDistance < minSeparation;
    }

    // Détermine le type de planète en fonction de sa distance à l'étoile
    string DeterminePlanetType(float orbitalRadiusInUA, float starTemperature)
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
    float DetermineTemperature(float orbitalRadiusInUA, float starTemperature)
    {
        float temperature = starTemperature / Mathf.Sqrt(orbitalRadiusInUA);
        return Mathf.Clamp(temperature, 50f, 5000f);
    }

    // Détermine la densité de la planète en fonction de son type
    float DetermineDensity(string planetType)
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
    float DetermineNormalizedSize(string bodyType, float mass)
    {
        switch (bodyType)
        {
            case "Etoile":
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

    // Calcule la position orbitale initiale en fonction des paramètres orbitaux
    Vector3 CalculateOrbitalPosition(float radius, float inclination, float eccentricity, float angle)
    {
        float trueAnomaly = angle;
        float radialDistance = radius * (1 - eccentricity * eccentricity) / (1 + eccentricity * Mathf.Cos(Mathf.Deg2Rad * trueAnomaly));

        float x = radialDistance * Mathf.Cos(Mathf.Deg2Rad * trueAnomaly);
        float z = radialDistance * Mathf.Sin(Mathf.Deg2Rad * trueAnomaly);
        float y = radialDistance * Mathf.Sin(Mathf.Deg2Rad * inclination) * Mathf.Sin(Mathf.Deg2Rad * trueAnomaly);

        return new Vector3(x, y, z);
    }

    // Détermine le type spectral de l'étoile
    string DetermineStarType(float temperature)
    {
        if (temperature < 3700f)
        {
            return "M"; // Naine rouge
        }
        else if (temperature < 5200f)
        {
            return "K"; // Étoile orange
        }
        else if (temperature < 6000f)
        {
            return "G"; // Étoile jaune (comme le Soleil)
        }
        else if (temperature < 7500f)
        {
            return "F"; // Étoile jaune-blanche
        }
        else if (temperature < 10000f)
        {
            return "A"; // Étoile blanche
        }
        else if (temperature < 30000f)
        {
            return "B"; // Étoile bleue
        }
        else
        {
            return "O"; // Étoile bleue très chaude
        }
    }

    // Calcule la luminosité de l'étoile en fonction de sa température
    /// <summary>
/// Calcule la luminosité de l'étoile en fonction de sa température et de son type spectral.
/// </summary>
/// <param name="temperature">Température de l'étoile en Kelvin.</param>
/// <param name="starType">Type spectral de l'étoile.</param>
/// <returns>Luminosité de l'étoile en unités de luminosité solaire (L☉).</returns>
float DetermineStarLuminosity(float temperature, string starType)
{
    // Luminosité en fonction du type spectral et de la température
    switch (starType)
    {
        case "M": // Naine rouge
            return Random.Range(0.0001f, 0.1f); // Luminosité entre 0.0001 L☉ et 0.1 L☉

        case "K": // Étoile orange
            return Random.Range(0.1f, 0.6f); // Luminosité entre 0.1 L☉ et 0.6 L☉

        case "G": // Étoile jaune (comme le Soleil)
            return Random.Range(0.6f, 1.5f); // Luminosité entre 0.6 L☉ et 1.5 L☉

        case "F": // Étoile jaune-blanche
            return Random.Range(1.5f, 5f); // Luminosité entre 1.5 L☉ et 5 L☉

        case "A": // Étoile blanche
            return Random.Range(5f, 20f); // Luminosité entre 5 L☉ et 20 L☉

        case "B": // Étoile bleue
            return Random.Range(20f, 100f); // Luminosité entre 20 L☉ et 100 L☉

        case "O": // Étoile bleue très chaude
            return Random.Range(100f, 1000f); // Luminosité entre 100 L☉ et 1000 L☉

        default:
            return 1f; // Luminosité par défaut (1 L☉, comme le Soleil)
    }
}


    // Génère un albedo pour une planète en fonction de son type
    float GenerateAlbedo(string planetType)
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

    string GenerateSystemID(int seed)
    {
        Random.InitState(seed);
        char firstLetter = letters[Random.Range(0, 26)];
        char secondLetter = letters[Random.Range(0, 26)];
        int part1 = Random.Range(0, 10);
        int part2 = Random.Range(0, 100);
        int part3 = Random.Range(0, 100000);
        return $"{firstLetter}{secondLetter}-{part1}-{part2:D2}-{part3:D5}";
    }

    bool IsValidSystemID(string id)
    {
        return Regex.IsMatch(id, @"^[A-Z]{2}-\d-\d{2}-\d{5}$");
    }

    int HashIDToSeed(string id)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(id));
            int seed = BitConverter.ToInt32(hashBytes, 0);
            return seed & 0x7FFFFFFF;
        }
    }

    void GenerateStarSystem(string systemID)
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

        // Température de l'étoile
        float starTemperature = Random.Range(3000f, 30000f);

        // Déterminer les propriétés de l'étoile
        string starType = DetermineStarType(starTemperature);
        float starDensity = DetermineDensity("Etoile");
        float starLuminosity = DetermineStarLuminosity(starTemperature, starType);
        float starMass = Random.Range(1f, 10f);
        float starSizeInSolarRadii = DetermineNormalizedSize("Etoile", starMass);
        float starSizeInGameUnits = starSizeInSolarRadii * SOLAR_RADIUS_IN_AU;

        // Appliquer la taille à l'étoile
        star.transform.localScale = Vector3.one * starSizeInGameUnits;

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
        starBody.bodyType = starType + "-Star";
        starBody.temperature = starTemperature;
        starBody.mass = starMass;
        starBody.radius = starSizeInGameUnits;
        starBody.distance = 0f;
        starBody.starLuminosity = starLuminosity;

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
                orbitalRadiusInGameUnits = Random.Range(minOrbitalDistanceInGameUnits, 50f * SOLAR_RADIUS_IN_AU);
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

            // Période orbitale en années
            float orbitalPeriodInYears = Mathf.Sqrt(Mathf.Pow(orbitalRadiusInGameUnits / SOLAR_RADIUS_IN_AU, 3));
            float orbitalPeriodInGameSeconds = orbitalPeriodInYears; //*fps

            float orbitalInclination = Random.Range(-15f, 15f);
            float orbitalEccentricity = Random.Range(0f, 0.3f);
            float initialAngle = Random.Range(0f, 360f);

            // Calculer la position initiale en fonction des paramètres orbitaux
            Vector3 orbitalPosition = CalculateOrbitalPosition(orbitalRadiusInGameUnits, orbitalInclination, orbitalEccentricity, initialAngle);

            // Récupérer une planète depuis le pool
            GameObject planet = celestialPool.Get().gameObject;
            planet.transform.position = star.transform.position + orbitalPosition;
            planet.transform.parent = star.transform;
            planet.name = $"{systemID} A{i + 1}";

            // Déterminer les propriétés de la planète
            string planetType = DeterminePlanetType(orbitalRadiusInGameUnits / SOLAR_RADIUS_IN_AU, starTemperature);
            float planetTemperature = DetermineTemperature(orbitalRadiusInGameUnits / SOLAR_RADIUS_IN_AU, starTemperature);
            float planetDensity = DetermineDensity(planetType);
            float planetSizeInSolarRadii = DetermineNormalizedSize(planetType, planetMass);
            float planetSizeInGameUnits = planetSizeInSolarRadii * SOLAR_RADIUS_IN_AU;
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
            body.bodyName = planet.name;
            body.bodyType = planetType;
            body.temperature = planetTemperature;
            body.mass = planetMass;
            body.distance = orbitalRadiusInGameUnits / SOLAR_RADIUS_IN_AU;
            body.density = planetDensity;
            body.radius = planetSizeInGameUnits;
            body.albedo = planetAlbedo;

            // Configurer les paramètres orbitaux
            body.orbitalPeriod = orbitalPeriodInGameSeconds;
            body.orbitalInclination = orbitalInclination;
            body.orbitalRadius = orbitalRadiusInGameUnits;
            body.orbitalEccentricity = orbitalEccentricity;
            body.centralBody = star.transform;
            body.orbitalAngle = initialAngle;
        }

        // Placer le vaisseau du joueur
        PlacePlayerShip(star.transform, Math.Max(planetOrbits.Count > 0 ? planetOrbits[planetOrbits.Count - 1].radius * 1.01f : minOrbitalDistanceInGameUnits * 2f, starSizeInGameUnits * 2f));
    }

    void PlacePlayerShip(Transform starTransform, float distance)
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
        }
        else
        {
            Debug.LogError("Le prefab du vaisseau du joueur n'est pas assigné !");
        }
    }

    public void JumpToNewSystem()
    {
        string sysID = GenerateSystemID(Random.Range(0, 640000));
        Debug.Log($"ID du système généré : {sysID}");
        JumpToSystem(sysID);
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

    void ClearCurrentSystem()
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
}
