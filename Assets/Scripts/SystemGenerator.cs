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
    private ObjectPool<Transform> celestialPool;

    public void Awake()
    {
        baseSeed = PlayerPrefs.GetInt("BaseSeed", defaultBaseSeed);
        celestialPool = new ObjectPool<Transform>(planetPrefab.transform, poolSize, transform);
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
        Transform star = celestialPool.Get();
        star.gameObject.tag = "Star";
        star.transform.position = Vector3.zero;
        star.name = $"{systemID} {letters[0]}*";

        // Génère 3 planètes aléatoires (nom : [ID] A1, [ID] A2, etc.)
        for (int i = 0; i < (int)Random.Range(0,poolSize-1); i++)
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

            Transform planet = celestialPool.Get();
            planet.gameObject.tag = "Planet";
            planet.transform.position = finalPosition;
            planet.transform.parent = star.transform;
            planet.gameObject.GetComponent<CelestialBody>().Start();
            planet.name = $"{systemID} A{i + 1}";
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
        if(!(child.name=="PlayerShip") && child.gameObject.activeInHierarchy)
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

}
