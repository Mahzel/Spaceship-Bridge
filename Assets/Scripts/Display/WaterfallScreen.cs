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
    private int integration = GameConstants.WATERFALL_INTEGRATION_MAX;
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
            pixelSize = (int)sizeSlider.GetComponent<Slider>().value;
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

    // Génère une ligne pour le waterfall
    public void GenerateRandomLine()
    {
        float[] baseNoise = new float[pointCount];

        // Bruit de fond gaussien (distribution plus réaliste que uniforme)
        for (int i = 0; i < baseNoise.Length; i++)
        {
            baseNoise[i] = GaussianNoise(0f, GameConstants.NOISE_STDDEV);
        }

        // Récupère tous les objets du système (étoiles et planètes)
        GameObject[] stars   = GameObject.FindGameObjectsWithTag("Star");
        GameObject[] planets = GameObject.FindGameObjectsWithTag("Planet");

        // Ajoute le signal des corps célestes avec étalement angulaire gaussien (±2 bins)
        foreach (GameObject star in stars)
        {
            var (azimuth, _, _) = star.GetComponent<CelestialBody>().GetData();
            float signal = star.GetComponent<CelestialBody>().apparentLuminosity;
            SpreadSignal(baseNoise, azimuth, signal);
        }
        foreach (GameObject planet in planets)
        {
            var (azimuth, _, _) = planet.GetComponent<CelestialBody>().GetData();
            float signal = planet.GetComponent<CelestialBody>().apparentLuminosity;
            SpreadSignal(baseNoise, azimuth, signal);
        }

        // CFAR sur le signal brut — avant intégration
        // La fenêtre large (51 bins) estime le fond, le rejet des 5 maxima
        // évite que le signal contamine l'estimation du bruit.
        float[] average = SlidingAverage(baseNoise, 51, 5);
        float[] stdev   = SlidingStdDev(baseNoise, 51, 5);

        float[] cfarNoise = new float[baseNoise.Length];
        for (int i = 0; i < baseNoise.Length; i++)
        {
            float sigma = (stdev[i] > 1e-9f) ? stdev[i] : 2f * Mathf.Abs(average[i]);
            if (sigma < 1e-12f) sigma = 1e-12f;
            cfarNoise[i] = (baseNoise[i] - average[i]) / sigma; // z-score brut
        }

        // Intégration sur les scores CFAR — les fluctuations aléatoires du bruit
        // se moyennent vers 0, le signal cohérent s'accumule
        float[] lineData;
        if (integrationToggle.GetComponent<Toggle>().isOn)
        {
            lineData = integrate(cfarNoise);
        }
        else
        {
            integrator.Clear();
            integrationCount = 1;
            lineData = cfarNoise;
        }

        // Compression finale : seuil à 3σ, saturation à 10σ
        lineData = ApplyCompression(lineData, 0f, 3f, 10f);

        AddWaterfallLine(lineData);
        FindFirstObjectByType<DSPGraph>().DrawDSPGraph(lineData, pixelSize);
    }

    // Étale un signal sur ±2 bins voisins avec une pondération gaussienne (PSF du capteur)
    private void SpreadSignal(float[] buffer, float azimuth, float signal)
    {
        int centerIndex = AzimuthToIndex(azimuth);
        float spreadSigma = GameConstants.PSF_SIGMA;
        for (int offset = -GameConstants.PSF_SPREAD_HALF_WIDTH; offset <= GameConstants.PSF_SPREAD_HALF_WIDTH; offset++)
        {
            int idx = (centerIndex + offset + buffer.Length) % buffer.Length;
            float weight = Mathf.Exp(-0.5f * (offset * offset) / (spreadSigma * spreadSigma));
            buffer[idx] += signal * weight;
        }
    }

    // Bruit gaussien via méthode Box-Muller
    private float GaussianNoise(float mean, float stddev)
    {
        float u1 = Mathf.Max(1e-6f, 1f - Random.value);
        float u2 = 1f - Random.value;
        float normal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return mean + stddev * normal;
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
            outline[i] = outline[i] / integrator.Count;
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
    // lineData est en sortie CFAR : ~0 pour le bruit, >1 pour les détections, jusqu'à 100
    private void UpdateLineColors(GameObject line, float[] lineData)
    {
        for (int i = 0; i < lineData.Length; i++)
        {
            RawImage point = line.transform.GetChild(i).GetComponent<RawImage>();
            float t = Mathf.Clamp01(lineData[i]);
            point.color = new Color(0.5f * t, t, 0.5f * t);
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
    // Convertit l'azimut (-180° à 180°) en index (0 à pointCount-1)
    int index = Mathf.FloorToInt((azimuth + 180f) / (360f / pointCount)) % pointCount;
    return Mathf.Clamp(index, 0, pointCount - 1);
}
}