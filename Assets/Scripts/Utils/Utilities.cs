using System.Collections.Generic;

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
