using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ChemicalComposition
{
    public string element; // Symbole chimique (ex: "H", "He", "Fe")
    public float percentage; // Pourcentage de l'élément dans la composition
}

[System.Serializable]
public class SpectralLine
{
    public float wavelength; // Longueur d'onde en nanomètres (nm)
    public float intensity; // Intensité de la raie spectrale
}

[System.Serializable]
public class Spectrum
{
    public List<SpectralLine> emissionLines; // Raies d'émission
    public List<SpectralLine> absorptionLines; // Raies d'absorption
}

public static class Utils
{
    // Constantes pour les conversions d'unités
    public const float UA_TO_GAME_UNITS = 100f; // 1 UA = 100 unités de jeu
    public const float SOLAR_RADIUS_IN_UA = 0.00465f; // 1 rayon solaire = 0.00465 UA
    public const float SOLAR_RADIUS_IN_GAME_UNITS = SOLAR_RADIUS_IN_UA * UA_TO_GAME_UNITS; // 1 rayon solaire = 0.465 unités de jeu

    /// <summary>
/// Calcule une moyenne glissante sur un tableau circulaire,
/// avec la possibilité de rejeter un certain nombre de maxima.
/// </summary>
/// <param name="signal">Tableau d'entrée.</param>
/// <param name="windowSize">Taille de la fenêtre (doit être impaire).</param>
/// <param name="rejectMaxCount">Nombre de maxima à rejeter (doit être < floor(windowSize / 2)).</param>
/// <returns>Tableau lissé par la moyenne glissante.</returns>
public static float[] SlidingAverage(float[] signal, int windowSize, int rejectMaxCount = 0)
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
public static float[] SlidingStdDev(float[] signal, int windowSize, int rejectMaxCount = 0)
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

}
