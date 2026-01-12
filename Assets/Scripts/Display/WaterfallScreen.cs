using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

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
    public static float CompressionLaw(float input, float xMin, float xCoude, float xMax)
    {
        // Vérifie que les valeurs sont dans l'ordre attendu
        if (xMin >= xCoude || xCoude >= xMax)
        {
            Debug.LogError("Les valeurs doivent être dans l'ordre : Xmin < Xcoude < Xmax.");
            return input;
        }

        // Si la valeur est en dessous de Xmin, retourne 0 (ou une valeur minimale)
        if (input <= xMin)
            return 0f;

        // Si la valeur est entre Xmin et Xcoude, applique une progression linéaire
        if (input <= xCoude)
        {
            return (input + xMin);
        }

        // Si la valeur est entre Xcoude et Xmax, applique une progression logarithmique
        if (input <= xMax)
        {
            return Mathf.Log10(input)+input;
        }

        // Si la valeur dépasse Xmax, retourne 1 (ou une valeur maximale)
        return 1f;
    }

    // Applique la loi de compression à un tableau de valeurs
    public static float[] ApplyCompression(float[] input, float xMin, float xCoude, float xMax)
    {
        float[] output = new float[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = CompressionLaw(input[i], xMin, xCoude, xMax);
        }
        return output;
    }

private int AzimuthToIndex(float azimuth)
{
    // Convertit l'azimut (0°-360°) en index (0-35) avec 0° au centre
    int index = Mathf.FloorToInt((azimuth + 180f) / (360/pointCount)) % pointCount;
    return index;
}
/// <summary>
/// Calcule une moyenne glissante sur un tableau circulaire,
/// avec la possibilité de rejeter un certain nombre de maxima.
/// </summary>
/// <param name="signal">Tableau d'entrée.</param>
/// <param name="windowSize">Taille de la fenêtre (doit être impaire).</param>
/// <param name="rejectMaxCount">Nombre de maxima à rejeter (doit être < floor(windowSize / 2)).</param>
/// <returns>Tableau lissé par la moyenne glissante.</returns>
private float[] SlidingAverage(float[] signal, int windowSize, int rejectMaxCount = 0)
{
    if (windowSize % 2 == 0)
    {
        windowSize++;
    }

    if (rejectMaxCount >= windowSize / 2)
    {
        rejectMaxCount = 0;
    }

    float[] smoothedSignal = new float[signal.Length];
    int halfWindow = windowSize / 2;

    for (int i = 0; i < signal.Length; i++)
    {
        List<float> windowValues = new List<float>(windowSize);

        // Remplit la fenêtre avec les valeurs autour de i (circulaire)
        for (int j = -halfWindow; j <= halfWindow; j++)
        {
            int index = (i + j + signal.Length) % signal.Length;
            windowValues.Add(signal[index]);
        }

        // Rejette les maxima si nécessaire
        if (rejectMaxCount > 0)
        {
            windowValues.Sort();
            windowValues.RemoveRange(windowValues.Count - rejectMaxCount, rejectMaxCount);
        }

        // Calcule la moyenne
        float sum = 0f;
        foreach (float value in windowValues)
        {
            sum += value;
        }
        smoothedSignal[i] = sum / windowValues.Count;
    }

    return smoothedSignal;
}

/// <summary>
/// Calcule un écart-type glissant sur un tableau circulaire,
/// avec la possibilité de rejeter un certain nombre de maxima.
/// </summary>
/// <param name="signal">Tableau d'entrée.</param>
/// <param name="windowSize">Taille de la fenêtre (doit être impaire).</param>
/// <param name="rejectMaxCount">Nombre de maxima à rejeter (doit être < floor(windowSize / 2)).</param>
/// <returns>Tableau des écarts-types glissants.</returns>
private float[] SlidingStdDev(float[] signal, int windowSize, int rejectMaxCount = 0)
{
    if (windowSize % 2 == 0)
    {
        windowSize++;
    }

    if (rejectMaxCount >= windowSize / 2)
    {
        rejectMaxCount = 0;
    }

    float[] stdDevSignal = new float[signal.Length];
    int halfWindow = windowSize / 2;

    for (int i = 0; i < signal.Length; i++)
    {
        List<float> windowValues = new List<float>(windowSize);

        // Remplit la fenêtre avec les valeurs autour de i (circulaire)
        for (int j = -halfWindow; j <= halfWindow; j++)
        {
            int index = (i + j + signal.Length) % signal.Length;
            windowValues.Add(signal[index]);
        }

        // Rejette les maxima si nécessaire
        if (rejectMaxCount > 0)
        {
            windowValues.Sort();
            windowValues.RemoveRange(windowValues.Count - rejectMaxCount, rejectMaxCount);
        }

        // Calcule la moyenne
        float sum = 0f;
        foreach (float value in windowValues)
        {
            sum += value;
        }
        float mean = sum / windowValues.Count;

        // Calcule l'écart-type
        float varianceSum = 0f;
        foreach (float value in windowValues)
        {
            varianceSum += Mathf.Pow(value - mean, 2);
        }
        float stdDev = Mathf.Sqrt(varianceSum / windowValues.Count);

        stdDevSignal[i] = stdDev;
    }

    return stdDevSignal;
}

}
