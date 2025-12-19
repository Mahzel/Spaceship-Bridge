using UnityEngine;
using Random = UnityEngine.Random;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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
    private ObjectPool<CelestialBody> celestialPool;

    public void Awake()
    {
        baseSeed = PlayerPrefs.GetInt("BaseSeed", defaultBaseSeed);
        celestialPool = new ObjectPool<CelestialBody>(planetPrefab.GetComponent<CelestialBody>(), poolSize, transform);
    }

    void Start()
    {
        JumpToNewSystem();
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

        int idSeed = HashIDToSeed(systemID);
        int finalSeed = baseSeed + idSeed;
        Random.InitState(finalSeed);

        Debug.Log($"Génération du système {systemID} avec la seed finale {finalSeed}");

        // Génère une étoile au centre (nom : [ID] A)
        CelestialBody star = celestialPool.Get();
        star.transform.position = Vector3.zero;
        star.name = $"{systemID} {letters[0]}*";

        // Génère 3 planètes aléatoires (nom : [ID] A1, [ID] A2, etc.)
        for (int i = 0; i < (int)Random.Range(1f,poolSize-1); i++)
        {
            float distance = Random.Range(5f, 20f);
            float angle = Random.Range(0f, 360f);
            float inclination = Random.Range(0f, 30f);

            Vector3 orbitalPosition = new Vector3(
                Mathf.Cos(Mathf.Deg2Rad * angle) * distance,
                0,
                Mathf.Sin(Mathf.Deg2Rad * angle) * distance
            );

            Quaternion inclinationRotation = Quaternion.AngleAxis(inclination, Vector3.right);
            Vector3 finalPosition = inclinationRotation * orbitalPosition;

            CelestialBody planet = celestialPool.Get();
            planet.transform.position = finalPosition;
            planet.transform.parent = star.transform;
            planet.name = $"{systemID} A{i + 1}";
        }
    }

    public void JumpToNewSystem()
    {
        currentSystemID = GenerateSystemID(Random.Range(0,640000));
        Debug.Log($"ID du système généré : {currentSystemID}");
        JumpToSystem(currentSystemID);
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
        if(!(child.name=="PlayerShip"))
        {
            celestialPool.ReturnToPool(child.GetComponent<CelestialBody>());                
        }
    }
}

}
