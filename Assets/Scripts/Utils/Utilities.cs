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
    public string species;  // Chemical symbol this line belongs to (see SpectralLineTable) - null/empty for
                             // old data or a line with no known species, in which case the overlay shows "?"
}

[System.Serializable]
public class Spectrum
{
    public List<SpectralLine> emissionLines; // Raies d'émission
    public List<SpectralLine> absorptionLines; // Raies d'absorption
}

/// <summary>
/// A body's retained gas envelope: composition (by mass/volume %, gas-giant style - not a partial crust
/// list) plus surface pressure. See AtmosphereModel for how it's generated (Jeans escape) and how it turns
/// into an actual surface temperature (grey-atmosphere greenhouse approximation).
/// </summary>
[System.Serializable]
public class Atmosphere
{
    public List<ChemicalComposition> composition = new List<ChemicalComposition>();

    /// <summary>Surface pressure in Earth atmospheres. 0 = airless. Meaningless for a gas giant
    /// (isEnvelope = true) - there is no solid surface for a pressure to be "at".</summary>
    public float surfacePressureAtm;

    /// <summary>True for a gas giant's deep H/He envelope: composition here describes the bulk gas, not a
    /// thin retained atmosphere over a surface, and surfacePressureAtm/greenhouse math don't apply.</summary>
    public bool isEnvelope;
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
    if (xMin >= xCoude || xCoude >= xMax)
    {
        Debug.LogError("Invalid parameters: Xmin < Xcoude < Xmax required");
        return 0f;
    }

    if (input <= xMin)
        return 0f;

    if (input <= xCoude)
    {
        // Linear progression from 0 to 0.5
        return Mathf.Lerp(0f, 0.5f, (input - xMin) / (xCoude - xMin));
    }

    if (input <= xMax)
    {
        // Logarithmic progression from 0.5 to 1.0
        float t = (input - xCoude) / (xMax - xCoude);
        return 0.5f + 0.5f * Mathf.Log10(1 + 9 * t); // Maps [0,1] to [0.5,1]
    }

    return 1f; // Clamped maximum
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