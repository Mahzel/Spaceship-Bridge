using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using static Utils;

public class WaterfallScreen : ComputerScreen
{
    [Header("Réglages du Waterfall")]
    public RectTransform waterfallContainer; // Conteneur du waterfall
    public GameObject waterfallLinePrefab;   // Prefab d'une ligne du waterfall
    public GameObject waterfallPointPrefab;  // Prefab d'un point du waterfall
    public GameObject sizeSlider;
    public GameObject integrationToggle;
    public int pointCount;
    public int lineCount = 20;               // Nombre de lignes visibles
    public float maxValue = 100f;            // Valeur maximale (100)
    public float updateInterval = 2f;        // Intervalle d'ajout de ligne (5 secondes)
    private int pixelSize = 1;
    private int integration = 20;
    ArrayList integrator;
    int integrationCount = 1;

    private List<GameObject> waterfallLines = new List<GameObject>();

    // Démarre la coroutine pour ajouter des lignes automatiquement
    protected override void Start()
    {
        pixelSize = (int)sizeSlider.GetComponent<Slider>().value;
        pointCount = (int)(waterfallContainer.rect.width)/pixelSize;
        lineCount = (int)(waterfallContainer.rect.height)/pixelSize;
        integrator = new ArrayList();
        GenerateRandomLine(); // Génère une ligne initiale
        StartCoroutine(AddLineRoutine());
    }

    // Coroutine pour ajouter une ligne toutes les 5 secondes
    private IEnumerator AddLineRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(updateInterval);
            pixelSize = (int)transform.Find("Slider").GetComponent<Slider>().value;
            pointCount = (int)(waterfallContainer.rect.width)/pixelSize;
            lineCount = (int)(waterfallContainer.rect.height)/pixelSize;
            GenerateRandomLine();
        }
    }

    public void onSizeChange()
    {
        integrator = new ArrayList();
        integrationCount = 1;
        ClearWaterfall();
    }

    // Génère une ligne aléatoire pour le waterfall
    public void GenerateRandomLine()
    {
        float[] lineData = new float[pointCount]; // 36 points (0° à 360° par pas de 10°)
        float[] baseNoise = new float[pointCount]; // Bruit de base aléatoire

        // Génère un bruit de base aléatoire (0 à 20 dB)
        for (int i = 0; i < lineData.Length; i++)
        {
            baseNoise[i] = Random.Range(-0.005f, 0.005f);
        }

        // Récupère tous les objets du système (étoiles et planètes)
        GameObject[] stars = GameObject.FindGameObjectsWithTag("Star");
        GameObject[] planets = GameObject.FindGameObjectsWithTag("Planet");

        // Ajoute le signal des étoiles (80 dB)
        foreach (GameObject star in stars)
        {
            var (azimuth, _, _) = star.GetComponent<CelestialBody>().GetData();
            int index = AzimuthToIndex(azimuth);
            baseNoise[index] = star.GetComponent<CelestialBody>().apparentLuminosity; // Ajoute 80 dB pour une étoile
        }

        // Ajoute le signal des planètes (35 dB)
        foreach (GameObject planet in planets)
        {
            var (azimuth, _, _) = planet.GetComponent<CelestialBody>().GetData();
            int index = AzimuthToIndex(azimuth);
            baseNoise[index] = planet.GetComponent<CelestialBody>().apparentLuminosity; // Ajoute 35 dB pour une étoile
        }
        if(integrationToggle.GetComponent<Toggle>().isOn)
        {
            lineData = integrate(baseNoise);
        } 
        else
        {
            integrator.Clear();
            integrationCount = 1;       
            lineData = baseNoise;
        }
        float[] average = SlidingAverage(lineData, 20, 5);
        float[] stdev = SlidingStdDev(lineData, 15, 5);

        for (int i = 0; i<lineData.Length;i++)
        {
            lineData[i] = Mathf.Min(lineData[i]+((lineData[i]-average[i])/stdev[i]),100f);
        }
        // Ajoute la ligne au waterfall
        AddWaterfallLine(lineData);
        // Met à jour le graphique DSP
        FindFirstObjectByType<DSPGraph>().DrawDSPGraph(lineData, pixelSize);
    }

    private float[] integrate(float[] line)
    {
        float[] outline = new float[line.Length];
        integrator.Add(line);
        if(integrator.Count > integration){
            integrator.RemoveAt(0);
        }
        for(int i = 0;i<line.Length;i++)
        {
            foreach(float[] integ in integrator)
            {
                outline[i] += integ[i];
            }
        }
        for(int i = 0;i<line.Length;i++)
        {
            outline[i] = outline[i]/integrationCount;
        }
        integrationCount = Mathf.Min(++integrationCount,integration);
        return outline;
    }


    // Ajoute une ligne au waterfall
    private void AddWaterfallLine(float[] lineData)
    {
        // Décale toutes les lignes vers le bas
        foreach (GameObject line in waterfallLines)
        {
            RectTransform lineRect = line.GetComponent<RectTransform>();
            lineRect.anchoredPosition += Vector2.down*pixelSize; // Décalage de 5 pixels vers le bas
        }

        // Crée une nouvelle ligne en haut
        GameObject newLine = Instantiate(waterfallLinePrefab, waterfallContainer);
        for(int i=0; i<pointCount; i++)
        {
            GameObject point = Instantiate(waterfallPointPrefab, newLine.transform);
            point.GetComponent<RectTransform>().sizeDelta = new Vector2(pixelSize, pixelSize);
            point.GetComponent<RectTransform>().anchoredPosition = new Vector2(i*pixelSize, 0); // Espacement de 5 pixels
        }
        newLine.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
        waterfallLines.Insert(0, newLine);

        // Met à jour la couleur des points de la ligne (noir → vert clair)
        UpdateLineColors(newLine, lineData);

        // Supprime la ligne la plus ancienne si nécessaire
        if (waterfallLines.Count > lineCount)
        {
            GameObject oldLine = waterfallLines[waterfallLines.Count - 1];
            waterfallLines.RemoveAt(waterfallLines.Count - 1);
            Destroy(oldLine);
        }
    }

    // Met à jour les couleurs des points d'une ligne (noir → vert clair)
    private void UpdateLineColors(GameObject line, float[] lineData)
    {
        lineData = ApplyCompression(lineData, -0.5f, 30f, 100f);
        for (int i = 0; i < lineData.Length; i++)
        {
            RawImage point = line.transform.GetChild(i).GetComponent<RawImage>();
            float normalizedValue = lineData[i] / maxValue;
            // Noir (0,0,0) → Vert clair (0.5f, 1, 0.5f)
            point.color = new Color(0.5f * normalizedValue, normalizedValue, 0.5f * normalizedValue);
        }
    }

    // Efface le waterfall
    public void ClearWaterfall()
    {
        foreach (GameObject line in waterfallLines)
        {
            Destroy(line);
        }
        waterfallLines.Clear();
    }

    public override void OnButtonClick(int index)
    {
        return;
    }
    

private int AzimuthToIndex(float azimuth)
{
    // Convertit l'azimut (0°-360°) en index (0-35) avec 0° au centre
    int index = Mathf.FloorToInt((azimuth + 180f) / (360/pointCount)) % pointCount;
    return index;
}
}