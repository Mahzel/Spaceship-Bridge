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
    private Texture2D _spectrumTexture; // Texture pour afficher le spectre
    private CelestialBody _currentTarget; // Cible actuelle du spectromètre
    private List<GameObject> lineSegments; // Liste des segments de la courbe DSP
    #endregion

    #region Unity Methods
    void Start()
    {
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
        Transform playerShip = GameObject.FindGameObjectWithTag("PlayerShip").transform;

        // Calculer la direction ajustée avec les offsets
        Vector3 scanDirection = CalculateAdjustedScanDirection(playerShip, imager.offsetAzimuth, imager.offsetElevation);
        Debug.DrawRay(playerShip.position, scanDirection * 10000, Color.red, 0.1f);

        RaycastHit hit;
        bool isHit = Physics.SphereCast(playerShip.position, 0.1f, scanDirection, out hit, Mathf.Infinity);

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
    #endregion

    #region Spectrum Drawing
    /// <summary>
    /// Dessine la courbe du spectre sur la texture.
    /// </summary>
    /// <param name="spectrum">Spectre à dessiner.</param>
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

        // Dessiner les raies d'absorption (en gris)
        foreach (SpectralLine line in spectrum.absorptionLines)
        {
            DrawSpectralLine(line.wavelength, -line.intensity, Color.grey);
        }
    }

    /// <summary>
    /// Dessine une raie spectrale (émission ou absorption).
    /// </summary>
    /// <param name="wavelength">Longueur d'onde de la raie.</param>
    /// <param name="intensity">Intensité de la raie.</param>
    /// <param name="color">Couleur de la raie.</param>
    private void DrawSpectralLine(float wavelength, float intensity, Color color)
    {
        // Convertir la longueur d'onde en position X (0 à spectrumWidth)
        int x = Mathf.RoundToInt(Mathf.InverseLerp(wavelengthMin, wavelengthMax, wavelength) * (spectrumWidth - 1));

        // Calculer la hauteur de la raie (0 à spectrumHeight)
        int height = Mathf.RoundToInt(2 * Mathf.Log10((Mathf.Abs(intensity) * (spectrumHeight - 1))));

        // Dessiner la raie (comme une ligne verticale)
        for (int y = 0; y < height; y++)
        {
            int yPos = intensity > 0 ? (spectrumHeight / 2 + y) : (spectrumHeight / 2 - y); // Émission vers le haut, absorption vers le bas
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
        // Initialiser les points de données
        float[] dataPoints = new float[spectrumWidth];
        for (int i = 0; i < spectrumWidth - 1; i++)
        {
            dataPoints[i] = Random.Range(-1f, 1f);
        }

        // Ajouter les raies d'émission
        foreach (SpectralLine s in target.spectrum.emissionLines)
        {
            int x = Mathf.RoundToInt(Mathf.InverseLerp(wavelengthMin, wavelengthMax, s.wavelength) * (spectrumWidth - 1));
            if (x >= 0 && x < spectrumWidth)
                dataPoints[x] += Mathf.Log(s.intensity * 100) * 5;
        }

        // Ajouter les raies d'absorption
        foreach (SpectralLine s in target.spectrum.absorptionLines)
        {
            int x = Mathf.RoundToInt(Mathf.InverseLerp(wavelengthMin, wavelengthMax, s.wavelength) * (spectrumWidth - 1));
            if (x >= 0 && x < spectrumWidth)
                dataPoints[x] -= Mathf.Log(s.intensity * 100) * 5;
        }

        // Créer les points pour la courbe
        Vector2[] points = new Vector2[spectrumWidth];
        for (int i = 0; i < spectrumWidth; i++)
        {
            float y = Mathf.RoundToInt(dataPoints[i]);
            points[i] = new Vector2(i, y);
        }

        // Dessiner les segments entre les points
        for (int i = 0; i < points.Length - 1; i++)
        {
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

            // Configurer le RectTransform du segment
            RectTransform segmentRect = segment.GetComponent<RectTransform>();
            segmentRect.anchorMin = new Vector2(0, 0.5f);
            segmentRect.anchorMax = new Vector2(0, 0.5f);
            segmentRect.pivot = Vector2.zero;

            // Positionner et étirer le segment entre les deux points
            segmentRect.localPosition = points[i];
            Vector2 direction = points[i + 1] - points[i];
            segmentRect.sizeDelta = new Vector2(direction.magnitude, 1f);
            segmentRect.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }
    }
    #endregion
}
