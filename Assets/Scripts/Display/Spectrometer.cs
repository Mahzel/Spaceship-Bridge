using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class Spectrometer : MonoBehaviour
{
    [Header("Réferences")]
    public Imager imager; // Référence à l'Imager pour la direction de visée
    public RawImage spectrumDisplay; // UI RawImage pour afficher le spectre

    [Header("Paramètres du Spectromètre")]
    public int spectrumWidth = 256; // Largeur de la texture du spectre (en pixels)
    public int spectrumHeight = 64; // Hauteur de la texture (en pixels)
    public float wavelengthMin = 380f; // Longueur d'onde min (nm)
    public float wavelengthMax = 780f; // Longueur d'onde max (nm)
    public Color curveColor = Color.green; // Couleur de la courbe

    private Texture2D _spectrumTexture;
    private CelestialBody _currentTarget;

    void Start()
    {
        // Initialiser la texture du spectre
        spectrumWidth = (int)spectrumDisplay.rectTransform.rect.width;
        spectrumHeight = (int)spectrumDisplay.rectTransform.rect.height;
        _spectrumTexture = new Texture2D(spectrumWidth, spectrumHeight);
        spectrumDisplay.texture = _spectrumTexture;
        ClearSpectrumTexture();
    }

    public void UpdateSpectrometry()
    {
        if (imager == null) return;

        // Trouver la cible au centre de l'Imager
        _currentTarget = FindTargetInImagerCenter();
        if ((_currentTarget != null) && imager.isScanning())
        {
            DrawSpectrumCurve(_currentTarget.spectrum);
            _spectrumTexture.Apply();
        }
        else
        {
            ClearSpectrumTexture();
        }
    }

    /// <summary>
/// Trouve l'objet au centre du FOV de l'Imager (2D ou 3D), en tenant compte des offsets d'azimut et d'élévation.
/// </summary>
/// <returns>Le CelestialBody ciblé, ou null si aucun.</returns>
private CelestialBody FindTargetInImagerCenter()
{
    Transform playerShip = GameObject.FindGameObjectWithTag("PlayerShip").transform;

    // Calculer la direction ajustée avec les offsets
    Vector3 scanDirection = CalculateAdjustedScanDirection(playerShip, imager.offsetAzimuth, imager.offsetElevation);
    Debug.DrawRay(playerShip.position, scanDirection*100, Color.red, 0.1f);


    RaycastHit hit;
    bool isHit = Physics.SphereCast(playerShip.position,0.1f, scanDirection, out hit, Mathf.Infinity);

    if (isHit && hit.collider != null)
    {
        CelestialBody body = hit.collider.GetComponent<CelestialBody>();
        if (body != null)
            return body;
    }
    return null;
}

/// <summary>
/// Calcule la direction de balayage ajustée avec les offsets d'azimut et d'élévation.
/// </summary>
/// <param name="playerShip">Transform du vaisseau du joueur.</param>
/// <param name="offsetAzimut">Offset d'azimut (en degrés).</param>
/// <param name="offsetElevation">Offset d'élévation (en degrés).</param>
/// <returns>Direction ajustée.</returns>
private Vector3 CalculateAdjustedScanDirection(Transform playerShip, float offsetAzimut, float offsetElevation)
{
    // Direction de base (avant du vaisseau)
    Vector3 direction = playerShip.forward;

    // Appliquer l'offset d'azimut (rotation autour de l'axe Y)
    if (offsetAzimut != 0f)
    {
        Quaternion azimutRotation = Quaternion.AngleAxis(offsetAzimut, playerShip.up);
        direction = azimutRotation * direction;
    }

    // Appliquer l'offset d'élévation (rotation autour de l'axe X ou droit du vaisseau)
    if (offsetElevation != 0f)
    {
        Quaternion elevationRotation = Quaternion.AngleAxis(offsetElevation, playerShip.right);
        direction = elevationRotation * direction;
    }

    return direction.normalized;
}


    // Dessine la courbe du spectre sur la texture
    private void DrawSpectrumCurve(Spectrum spectrum)
    {
        // Effacer la texture
        ClearSpectrumTexture();

        // Dessiner les axes (optionnel)
        DrawAxes();

        // Dessiner les raies d'émission
        foreach (SpectralLine line in spectrum.emissionLines)
        {
            DrawSpectralLine(line.wavelength, line.intensity, curveColor);
        }

        // Dessiner les raies d'absorption (en noir ou gris)
        foreach (SpectralLine line in spectrum.absorptionLines)
        {
            DrawSpectralLine(line.wavelength, -line.intensity, Color.grey);
        }
    }

    // Dessine une raie spectrale (émission ou absorption)
    private void DrawSpectralLine(float wavelength, float intensity, Color color)
    {
        // Convertir la longueur d'onde en position X (0 à spectrumWidth)
        int x = Mathf.RoundToInt(Mathf.InverseLerp(wavelengthMin, wavelengthMax, wavelength) * (spectrumWidth - 1));

        // Calculer la hauteur de la raie (0 à spectrumHeight)
        int height = Mathf.RoundToInt(Mathf.Abs(intensity) * (spectrumHeight - 1));

        // Dessiner la raie (comme une ligne verticale)
        for (int y = 0; y < height; y++)
        {
            int yPos = intensity > 0 ? (spectrumHeight - 1 - y) : (spectrumHeight / 2 + y); // Émission vers le haut, absorption vers le bas
            if (yPos >= 0 && yPos < spectrumHeight)
                _spectrumTexture.SetPixel(x, yPos, color);
        }
    }

    // Efface la texture du spectre
    private void ClearSpectrumTexture()
    {
        Color[] pixels = new Color[spectrumWidth * spectrumHeight];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.black;
        _spectrumTexture.SetPixels(pixels);
        _spectrumTexture.Apply();
    }

    // Dessine les axes X et Y (optionnel)
    private void DrawAxes()
    {
        // Axe X (longueurs d'onde)
        for (int x = 0; x < spectrumWidth; x++)
            _spectrumTexture.SetPixel(x, spectrumHeight - 1, Color.white);

        // Axe Y (centre)
        for (int y = 0; y < spectrumHeight; y++)
            _spectrumTexture.SetPixel(0, y, Color.white);
    }
}
