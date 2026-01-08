using UnityEngine;
using Random = UnityEngine.Random;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.VisualScripting;

public class SystemManager : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject starPrefab;
    public GameObject planetPrefab;

    [Header("Configuration")]
    public int defaultBaseSeed = 645865465;

    private int baseSeed;
    private string currentSystemID;
    char[] letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
    private int poolSize = 10;
    private ObjectPool<Transform> celestialPool;
    private GameObject playerShip;
    public GameObject playerShipPrefab;

    public void Awake()
    {
        baseSeed = PlayerPrefs.GetInt("BaseSeed", defaultBaseSeed);
        celestialPool = new ObjectPool<Transform>(planetPrefab.transform, poolSize, transform);
    }

    void Start()
    {
        JumpToNewSystem();
    }

// Détermine le type de planète en fonction de sa distance à l'étoile et de la température de l'étoile
string DeterminePlanetType(float orbitalRadius, float starTemperature)
{
    float normalizedDistance = Mathf.Clamp(orbitalRadius, 5f, 50f);

    if (normalizedDistance < 10f)
    {
        return "Rocheuse";
    }
    else if (normalizedDistance < 20f)
    {
        return Random.value > 0.7f ? "Ceinture d'astéroïdes" : "Rocheuse";
    }
    else if (normalizedDistance < 35f)
    {
        return "Gazeuse";
    }
    else
    {
        return "Glacée";
    }
}

// Calcule la température de la planète en fonction de sa distance à l'étoile et de la température de l'étoile
float DetermineTemperature(float orbitalRadius, float starTemperature)
{
    float temperature = starTemperature / Mathf.Sqrt(orbitalRadius);
    return Mathf.Clamp(temperature, 50f, 5000f);
}


float DetermineDensity(string PlanetType)
    {
    switch (PlanetType)
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

// Calcule le rayon d'un objet à partir de sa masse et de sa densité
float CalculateRadius(float mass, float density)
{
    float volume = mass / density;
    return Mathf.Pow((3f * volume) / (4f * Mathf.PI), 1f / 3f);
}

float DetermineNormalizedSize(string bodyType, float mass)
{
    switch (bodyType)
    {
        case "Etoile":
            // Les étoiles ont une taille proportionnelle à leur masse
            return Mathf.Clamp(mass, 0.5f, 5f); // Rayon entre 0.5 et 5 unités

        case "Gazeuse":
            // Les géantes gazeuses sont plus petites que le Soleil
            return Mathf.Clamp(mass * 0.2f, 0.3f, 0.8f); // Rayon entre 0.3 et 0.8 unités

        case "Rocheuse":
            // Les planètes rocheuses sont beaucoup plus petites
            return Mathf.Clamp(mass * 0.02f, 0.01f, 0.05f); // Rayon entre 0.01 et 0.05 unités

        case "Glacée":
            // Les planètes glacées sont un peu plus grandes que les rocheuses
            return Mathf.Clamp(mass * 0.03f, 0.02f, 0.08f); // Rayon entre 0.02 et 0.08 unités

        case "Ceinture d'astéroïdes":
            // Les astéroïdes sont très petits
            return Mathf.Clamp(mass * 0.005f, 0.001f, 0.005f); // Rayon entre 0.001 et 0.005 unités

        default:
            return 0.1f; // Taille par défaut
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
float DetermineStarLuminosity(float temperature)
{
    // Luminosité approximative en fonction de la température (simplifiée)
    if (temperature < 3700f)
    {
        return Random.Range(0.01f, 0.1f); // Naine rouge
    }
    else if (temperature < 5200f)
    {
        return Random.Range(0.1f, 0.6f); // Étoile orange
    }
    else if (temperature < 6000f)
    {
        return Random.Range(0.6f, 1.5f); // Étoile jaune
    }
    else if (temperature < 7500f)
    {
        return Random.Range(1.5f, 5f); // Étoile jaune-blanche
    }
    else if (temperature < 10000f)
    {
        return Random.Range(5f, 20f); // Étoile blanche
    }
    else if (temperature < 30000f)
    {
        return Random.Range(20f, 100f); // Étoile bleue
    }
    else
    {
        return Random.Range(100f, 500f); // Étoile bleue très chaude
    }
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
    float starLuminosity = DetermineStarLuminosity(starTemperature);
    float starMass = Random.Range(1f,10f);
    float starSize = DetermineNormalizedSize("Etoile", starMass);

    // Appliquer la taille à l'étoile
    star.transform.localScale = Vector3.one * starSize;
    SphereCollider starCollider = star.GetComponent<SphereCollider>();
    starCollider.radius = starSize;
    float minDistance = starSize * 2f;

    // Stocker les propriétés de l'étoile dans un composant CelestialBody
    CelestialBody starBody = star.GetComponent<CelestialBody>();
    if (starBody == null)
    {
        starBody = star.AddComponent<CelestialBody>();
    }
    starBody.bodyName = star.name;
    starBody.bodyType = starType+"-Star";
    starBody.starLuminosity = starLuminosity;
    starBody.temperature = starTemperature;
    starBody.mass = starMass;
    starBody.radius = starSize;
    starBody.distance = 0f; // L'étoile est au centre

    // Variable pour stocker le rayon orbital moyen le plus grand
    float maxOrbitalRadius = 0f;
    PlacePlayerShip(star.transform, Math.Max(maxOrbitalRadius * 2f,starBody.radius * 5f));

    // Générer 3 planètes aléatoires
    for (int i = 0; i < Random.Range(0,10); i++)
    {
        // Générer les paramètres orbitaux
        float orbitalRadius = Random.Range(minDistance, 50f);
        if (orbitalRadius > maxOrbitalRadius)
        {
            maxOrbitalRadius = orbitalRadius;
        }
        float orbitalPeriod = Random.Range(15f, 30f); // Période orbitale en secondes (15-30s)
        float orbitalInclination = Random.Range(-15f,15f);
        float orbitalEccentricity = Random.Range(0f, 0.3f);
        float initialAngle = Random.Range(0f, 360f);

        // Calculer la position initiale en fonction des paramètres orbitaux
        Vector3 orbitalPosition = CalculateOrbitalPosition(orbitalRadius, orbitalInclination, orbitalEccentricity, initialAngle);

        // Récupérer une planète depuis le pool
        GameObject planet = celestialPool.Get().gameObject;
        planet.transform.position = star.transform.position + orbitalPosition;
        planet.transform.parent = star.transform;
        planet.name = $"{systemID} A{i + 1}";

        // Déterminer les propriétés de la planète
        string planetType = DeterminePlanetType(orbitalRadius, starTemperature);
        float planetTemperature = DetermineTemperature(orbitalRadius, starTemperature);
        float planetDensity = DetermineDensity(planetType);
        float planetMass = Random.Range(0.5f,5f);
        float planetSize = DetermineNormalizedSize(planetType, planetMass);
        float planetAlbedo = GenerateAlbedo(planetType);

        // Appliquer la taille à l'objet (scale)
        planet.transform.localScale = Vector3.one * planetSize;
        SphereCollider planetCollider = planet.GetComponent<SphereCollider>();
        planetCollider.radius = planetSize;

        // Stocker les propriétés dans un composant (ex : CelestialBody)
        CelestialBody body = planet.GetComponent<CelestialBody>();
        if (body == null)
        {
            body = planet.AddComponent<CelestialBody>();
        }
        body.bodyName = planet.name;
        body.bodyType = planetType;
        body.temperature = planetTemperature;
        body.mass = planetMass;
        body.distance = orbitalRadius;
        body.density = planetDensity;
        body.radius = CalculateRadius(body.mass, body.density);
        body.albedo = planetAlbedo;

        // Configurer les paramètres orbitaux
        body.orbitalPeriod = orbitalPeriod;
        body.orbitalInclination = orbitalInclination;
        body.orbitalRadius = orbitalRadius;
        body.orbitalEccentricity = orbitalEccentricity;
        body.centralBody = star.transform;
        body.orbitalAngle = initialAngle;
    }

    // Placer le vaisseau du joueur
    PlacePlayerShip(star.transform, Math.Max(maxOrbitalRadius * 2f,starBody.radius * 5f));
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
        string sysID = GenerateSystemID(Random.Range(0,640000));
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
    // Supprime tous les enfants de SystemManager (les étoiles et planètes)
    foreach (Transform child in transform)
    {
        if(playerShip != null){
                playerShip.transform.parent = transform;
            }
        if(!(child.tag=="PlayerShip") && child.gameObject.activeInHierarchy)
        {
            while(child.childCount>0)
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

float GenerateAlbedo(string planetType)
{
    float baseAlbedo;
    float variation;

    // Définir une valeur de base et une variation en fonction du type de planète
    switch (planetType)
    {
        case "Rocheuse":
            baseAlbedo = 0.15f; // Valeur typique pour les planètes rocheuses (ex: Mercure, Terre)
            variation = 0.1f;  // Variation possible
            break;

        case "Gazeuse":
            baseAlbedo = 0.5f;  // Valeur typique pour les géantes gazeuses (ex: Jupiter, Saturne)
            variation = 0.2f;  // Variation possible
            break;

        case "Glacée":
            baseAlbedo = 0.7f;  // Valeur typique pour les planètes glacées (ex: Neptune, Uranus)
            variation = 0.15f; // Variation possible
            break;

        case "Ceinture d'astéroïdes":
            baseAlbedo = 0.05f; // Valeur typique pour les astéroïdes (très sombre)
            variation = 0.03f; // Variation possible
            break;

        default:
            baseAlbedo = 0.3f;  // Valeur par défaut
            variation = 0.1f;  // Variation par défaut
            break;
    }

    // Générer une valeur d'albedo aléatoire autour de la valeur de base
    float randomOffset = Random.Range(-variation, variation);
    float albedo = Mathf.Clamp(baseAlbedo + randomOffset, 0f, 1f);

    return albedo;
}


}
