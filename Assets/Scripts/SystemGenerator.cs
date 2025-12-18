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
    public int defaultBaseSeed = 12345;

    private int baseSeed;
    private string currentSystemID;

    void Awake()
    {
        baseSeed = PlayerPrefs.GetInt("BaseSeed", defaultBaseSeed);
    }

    void Start()
    {
        currentSystemID = GenerateSystemID(baseSeed);
        Debug.Log($"ID du système généré : {currentSystemID}");
        GenerateStarSystem(baseSeed, currentSystemID);
    }

    string GenerateSystemID(int seed)
    {
        Random.InitState(seed);
        char[] letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
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

    void GenerateStarSystem(int baseSeed, string systemID)
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
        Vector3 starPosition = Vector3.zero;
        GameObject star = Instantiate(starPrefab, starPosition, Quaternion.identity, gameObject.transform);
        star.name = $"{systemID} A";

        // Génère 3 planètes aléatoires (nom : [ID] A1, [ID] A2, etc.)
        for (int i = 0; i < 3; i++)
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

            GameObject planet = Instantiate(planetPrefab, finalPosition, Quaternion.identity, star.transform);
            planet.name = $"{systemID} A{i + 1}"; // Nommage des planètes : A1, A2, A3
        }
    }

    public void JumpToSystem(string targetSystemID)
    {
        if (IsValidSystemID(targetSystemID))
        {
            ClearCurrentSystem();
            GenerateStarSystem(baseSeed, targetSystemID);
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
        Destroy(child.gameObject);
    }
}

}
