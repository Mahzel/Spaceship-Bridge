using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class Spectrometer : MonoBehaviour
{
    #region References
    [Header("References")]
    public Imager imager; // Référence à l'Imager pour la direction de visée
    public RawImage spectrumDisplay; // UI RawImage pour afficher le spectre
    public GameObject lineSegmentPrefab; // Prefab pour les segments de la courbe DSP
    #endregion

    #region Parameters
    [Header("Spectrometer Parameters")]
    public float wavelengthMin = 380f; // Longueur d'onde min (nm)
    public float wavelengthMax = 780f; // Longueur d'onde max (nm)
    public Color curveColor = Color.green; // Couleur de la courbe

    private int spectrumWidth = 256; // Largeur de la texture du spectre (en pixels)
    private int spectrumHeight = 64; // Hauteur de la texture (en pixels)
    #endregion

    #region Private Fields
    private Texture2D _spectrumTexture;
    private CelestialBody _currentTarget;
    private List<GameObject> lineSegments;
    private float _maxSpectralIntensity = 1f; // Intensité max du spectre courant pour normalisation
    private Transform _playerShip;            // Caché pour éviter FindGameObjectWithTag par frame
    #endregion

    #region Unity Methods
    void Start()
    {
        _playerShip = GameObject.FindGameObjectWithTag("PlayerShip").transform;
        InitializeSpectrumTexture();
    }
    #endregion

    #region Initialization
    /// <summary>
    /// Initialise la texture du spectre en fonction de la taille du RawImage.
    /// </summary>
    private void InitializeSpectrumTexture()
    {
        // Initialiser la taille de la texture en fonction de la taille du RawImage
        spectrumWidth = (int)spectrumDisplay.rectTransform.rect.width;
        spectrumHeight = (int)spectrumDisplay.rectTransform.rect.height;

        // Créer une nouvelle texture
        _spectrumTexture = new Texture2D(spectrumWidth, spectrumHeight);
        spectrumDisplay.texture = _spectrumTexture;

        // Initialiser la liste des segments
        lineSegments = new List<GameObject>();

        // Effacer la texture
        ClearSpectrumTexture();
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Met à jour la spectroscopie en fonction de la cible actuelle de l'Imager.
    /// </summary>
    public void UpdateSpectrometry()
    {
        if (imager == null) return;

        // Trouver la cible au centre de l'Imager
        _currentTarget = FindTargetInImagerCenter();

        if ((_currentTarget != null) && imager.isScanning())
        {
            DrawSpectrumCurve(_currentTarget.spectrum);
            DrawDSP(_currentTarget);
            _spectrumTexture.Apply();
        }
        else
        {
            ClearSpectrumTexture();
        }
    }
    #endregion

    #region Target Detection
    /// <summary>
    /// Trouve l'objet au centre du FOV de l'Imager (2D ou 3D), en tenant compte des offsets d'azimut et d'élévation.
    /// </summary>
    /// <returns>Le CelestialBody ciblé, ou null si aucun.</returns>
    private CelestialBody FindTargetInImagerCenter()
    {
        if (_playerShip == null)
            _playerShip = GameObject.FindGameObjectWithTag("PlayerShip").transform;

        Vector3 scanDirection = CalculateAdjustedScanDirection(_playerShip, imager.offsetAzimuth, imager.offsetElevation);
        Debug.DrawRay(_playerShip.position, scanDirection * 10000, Color.red, 0.1f);

        RaycastHit hit;
        if (Physics.SphereCast(_playerShip.position, 0.1f, scanDirection, out hit, Mathf.Infinity))
        {
            CelestialBody body = hit.collider?.GetComponent<CelestialBody>();
            if (body != null) return body;
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
    #endregion

    #region Spectrum Drawing
    /// <summary>
    /// Dessine la courbe du spectre sur la texture.
    /// </summary>
    /// <param name="spectrum">Spectre à dessiner.</param>
    private void DrawSpectrumCurve(Spectrum spectrum)
    {
        ClearSpectrumTexture();
        DrawAxes();

        _maxSpectralIntensity = 1f;
        foreach (SpectralLine line in spectrum.emissionLines)
            _maxSpectralIntensity = Mathf.Max(_maxSpectralIntensity, line.intensity);
        foreach (SpectralLine line in spectrum.absorptionLines)
            _maxSpectralIntensity = Mathf.Max(_maxSpectralIntensity, line.intensity);

        foreach (SpectralLine line in spectrum.emissionLines)
            DrawSpectralLine(line.wavelength, line.intensity, curveColor, false);

        foreach (SpectralLine line in spectrum.absorptionLines)
            DrawSpectralLine(line.wavelength, line.intensity, Color.grey, true); // vers le bas
    }

    private void DrawSpectralLine(float wavelength, float intensity, Color color, bool isAbsorption)
    {
        if (intensity <= 0f) return;

        int x = Mathf.RoundToInt(Mathf.InverseLerp(wavelengthMin, wavelengthMax, wavelength) * (spectrumWidth - 1));
        if (x < 0 || x >= spectrumWidth) return;

        float normalizedIntensity = Mathf.Clamp01(intensity / _maxSpectralIntensity);
        float logHeight = Mathf.Log10(1f + 9f * normalizedIntensity);
        int height = Mathf.RoundToInt(logHeight * (spectrumHeight / 2f - 1f));
        if (height <= 0) return;

        int centerY = spectrumHeight / 2;
        for (int y = 0; y < height; y++)
        {
            int yPos = isAbsorption ? centerY - y : centerY + y;
            if (yPos >= 0 && yPos < spectrumHeight)
                _spectrumTexture.SetPixel(x, yPos, color);
        }
    }

    /// <summary>
    /// Efface la texture du spectre.
    /// </summary>
    private void ClearSpectrumTexture()
    {
        Color[] pixels = new Color[spectrumWidth * spectrumHeight];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.black;
        _spectrumTexture.SetPixels(pixels);
        _spectrumTexture.Apply();
    }

    /// <summary>
    /// Dessine les axes X et Y (optionnel).
    /// </summary>
    private void DrawAxes()
    {
        // Axe X (longueurs d'onde)
        for (int x = 0; x < spectrumWidth; x++)
            _spectrumTexture.SetPixel(x, Mathf.RoundToInt(spectrumHeight / 2), Color.white);

        // Axe Y (centre)
        for (int y = 0; y < spectrumHeight; y++)
            _spectrumTexture.SetPixel(0, y, Color.white);
    }
    #endregion

    #region DSP Drawing
    /// <summary>
    /// Dessine la courbe DSP (Digital Signal Processing) en fonction du spectre de la cible.
    /// </summary>
    /// <param name="target">Cible dont le spectre est analysé.</param>
    private void DrawDSP(CelestialBody target)
    {
        float[] dataPoints = new float[spectrumWidth];

        // Bruit gaussien cohérent avec le reste du pipeline (σ = 0.002)
        for (int i = 0; i < spectrumWidth; i++)
            dataPoints[i] = GaussianNoise(0f, 0.002f);

        // Raies d'émission et d'absorption étalées sur ±2 pixels (slit function gaussienne)
        foreach (SpectralLine s in target.spectrum.emissionLines)
        {
            int xCenter = Mathf.RoundToInt(Mathf.InverseLerp(wavelengthMin, wavelengthMax, s.wavelength) * (spectrumWidth - 1));
            float amplitude = (s.intensity > 0f) ? Mathf.Log10(1f + s.intensity / _maxSpectralIntensity * 9f) * 5f : 0f;
            SpreadSpectralSignal(dataPoints, xCenter, amplitude);
        }
        foreach (SpectralLine s in target.spectrum.absorptionLines)
        {
            int xCenter = Mathf.RoundToInt(Mathf.InverseLerp(wavelengthMin, wavelengthMax, s.wavelength) * (spectrumWidth - 1));
            float amplitude = (s.intensity > 0f) ? Mathf.Log10(1f + s.intensity / _maxSpectralIntensity * 9f) * 5f : 0f;
            SpreadSpectralSignal(dataPoints, xCenter, -amplitude);
        }

        // Construire et afficher la courbe
        for (int i = 0; i < dataPoints.Length - 1; i++)
        {
            float y = dataPoints[i];
            GameObject segment;
            if (lineSegments.Count <= i)
            {
                segment = Instantiate(lineSegmentPrefab, spectrumDisplay.transform);
                lineSegments.Add(segment);
            }
            else
            {
                segment = lineSegments[i];
            }

            RectTransform segmentRect = segment.GetComponent<RectTransform>();
            segmentRect.anchorMin = new Vector2(0, 0.5f);
            segmentRect.anchorMax = new Vector2(0, 0.5f);
            segmentRect.pivot     = Vector2.zero;

            Vector2 p0 = new Vector2(i,     dataPoints[i]);
            Vector2 p1 = new Vector2(i + 1, dataPoints[i + 1]);
            Vector2 dir = p1 - p0;
            segmentRect.localPosition = p0;
            segmentRect.sizeDelta  = new Vector2(dir.magnitude, 1f);
            segmentRect.rotation   = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        // Désactiver les segments excédentaires du frame précédent
        for (int i = dataPoints.Length - 1; i < lineSegments.Count; i++)
            lineSegments[i].SetActive(false);
    }

    // Étale un signal sur ±2 pixels avec une PSF gaussienne (σ = 1.2 px)
    private void SpreadSpectralSignal(float[] buffer, int center, float amplitude)
    {
        float sigma = 1.2f;
        for (int offset = -2; offset <= 2; offset++)
        {
            int idx = center + offset;
            if (idx < 0 || idx >= buffer.Length) continue;
            float weight = Mathf.Exp(-0.5f * (offset * offset) / (sigma * sigma));
            buffer[idx] += amplitude * weight;
        }
    }

    // Bruit gaussien via Box-Muller
    private float GaussianNoise(float mean, float stddev)
    {
        float u1 = Mathf.Max(1e-6f, 1f - Random.value);
        float u2 = 1f - Random.value;
        float normal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return mean + stddev * normal;
    }
    #endregion
}