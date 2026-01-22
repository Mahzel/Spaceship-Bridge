using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using static Utils;

/// <summary>
/// Gère un système de capteur passif pour le projet Spaceship Bridge.
/// Simule un capteur optique/infrarouge qui mesure la luminosité apparente dans chaque direction.
/// La mire est gérée par des sprites (pas de dessin direct sur la texture).
/// </summary>
public class Imager : MonoBehaviour
{
    // =========================================================================
    #region PARAMÈTRES PUBLIQUES (CONFIGURATION DANS L'INSPECTEUR)
    // =========================================================================

    #region Display Settings
    /// <summary> Référence au RawImage pour l'affichage du scan. </summary>
    public RawImage display;

    /// <summary> Référence au vaisseau du joueur. </summary>
    public Transform playerShip;

    /// <summary> Référence à l'étoile principale (pour calculer les phases). </summary>
    public Transform star;
    #endregion

    #region Scan Settings
    /// <summary> Distance maximale de détection (en UA). </summary>
    public float maxScanDistance = 50000f;

    /// <summary> Champ de vision en degrés (horizontal et vertical). </summary>
    public float fieldOfView = 60f;

    /// <summary> Taille d'un bloc de pixels (résolution du capteur). </summary>
    public int blockSize = 10;

    /// <summary> Intervalle de mise à jour (en secondes). </summary>
    public float updateInterval = 0.05f;

    /// <summary> Gain du capteur (amplification du signal). </summary>
    public float sensorGain = 1f;

    /// <summary> Niveau de bruit de fond. </summary>
    public float backgroundNoise = 0.1f;
    #endregion

    #region UI Controls
    /// <summary> Slider pour ajuster le champ de vision (FOV). </summary>
    public Slider fovSlider;

    /// <summary> Slider pour ajuster la résolution (taille des blocs). </summary>
    public Slider resSlider;

    /// <summary> Slider pour ajuster le gain (intensité du signal). </summary>
    public Slider gainSlider;

    public Slider dynSlider;

    /// <summary> Référence directe au bouton Zoom. </summary>
    public Button zoomButton;
    public float offsetAzimuth=0f;
    public float offsetElevation=0f;
    public RawImage debugImage;
    #endregion

    #region Crosshair Settings
    /// <summary> Sprite de la mire normale (avec carré central). </summary>
    public Sprite normalCrosshairSprite;

    /// <summary> Sprite de la mire en mode zoom (croix simple). </summary>
    public Sprite zoomCrosshairSprite;

    /// <summary> Référence à l'Image UI pour la mire. </summary>
    public Image crosshairImage;

    /// <summary> Texte pour afficher la résolution. </summary>
    public TMP_Text resolutionText;
    public TMP_Text fovText;
    public TMP_Text gainText;
    public TMP_Dropdown bodyList;
    #endregion
    // =========================================================================
    #endregion

    // =========================================================================
    #region VARIABLES PRIVÉES
    // =========================================================================

    /// <summary> Texture pour afficher le scan. </summary>
    private Texture2D _scannedTexture;

    /// <summary> Indique si le scan est en cours. </summary>
    private bool _isScanning;

    /// <summary> Coroutine du scan. </summary>
    private Coroutine _scanCoroutine;

    /// <summary> Largeur et hauteur de l'affichage (en pixels). </summary>
    private int _displayWidth, _displayHeight;

    /// <summary> Résolution du scan (nombre de blocs). </summary>
    private int _scanResolution;

    /// <summary> Ligne actuelle du balayage. </summary>
    private int _currentScanLine;

    /// <summary> Facteur de zoom (1 = pas de zoom, 10 = zoom x10). </summary>
    private float _zoomFactor = 1f;

    /// <summary> Liste des objets célestes dans la scène. </summary>
    private List<CelestialBody> _celestialBodies = new List<CelestialBody>();
    private CelestialBody _selectedBody;

    private float dynamicCompressionFactor = 2f;
    /// <summary> Texture pour l'overlay de debug. </summary>
    private Texture2D _debugTexture;


    float maxExpectedLuminosity = 1000f;
    // =========================================================================
    #endregion

    // =========================================================================
    #region MÉTHODES UNITY (MONOBEHAVIOUR)
    // =========================================================================

    /// <summary>
    /// Initialisation au démarrage.
    /// </summary>
    private void Start()
    {
        // Récupérer la taille de l'affichage
        blockSize = (int)resSlider.value;
        fieldOfView = fovSlider.value;
        sensorGain = gainSlider.value;
        maxExpectedLuminosity = dynSlider.value;
        _displayWidth = (int)display.rectTransform.rect.width;
        _displayHeight = (int)display.rectTransform.rect.height;
        _scanResolution = _displayWidth / blockSize;

        // Créer la texture du scan
        _scannedTexture = new Texture2D(_displayWidth, _displayHeight);
        display.texture = _scannedTexture;
        _debugTexture = new Texture2D(_displayWidth, _displayHeight);
        debugImage.texture = _debugTexture;
        // Trouver le vaisseau du joueur
        playerShip = GameObject.FindGameObjectWithTag("PlayerShip").transform;
        // Trouver tous les objets célestes dans la scène
        FindAllCelestialBodies();

        // Initialiser l'affichage
        ClearImage();
        UpdateText();
        InitializeZoomButton();
    }
    // =========================================================================
    #endregion

    // =========================================================================
    #region MÉTHODES PUBLIQUES (APPELÉES PAR L'UI)
    // =========================================================================

    public bool isScanning()
    {
        return _isScanning;
    }
    /// <summary>
    /// Démarre ou arrête le scan.
    /// </summary>
    public void ToggleScan()
    {
        if (!_isScanning)
        {
            ClearImage();
            _isScanning = true;
            _scanCoroutine = StartCoroutine(ScanRoutine());
        }
        else
        {
            StopScanning();
        }
    }

    /// <summary>
    /// Active ou désactive le mode zoom.
    /// </summary>
    public void ToggleZoom()
    {
        _zoomFactor = _zoomFactor == 1f ? 10f : 1f;

        if (_zoomFactor == 10f)
        {
            crosshairImage.sprite = zoomCrosshairSprite;
            if (zoomButton != null)
            {
                TMP_Text buttonText = zoomButton.GetComponent<TMP_Text>();
                if (buttonText != null)
                {
                    buttonText.text = "Zoom On";
                }
                ColorBlock colors = zoomButton.colors;
                colors.normalColor = Color.green;
                zoomButton.colors = colors;
            }
        }
        else
        {
            crosshairImage.sprite = normalCrosshairSprite;
            if (zoomButton != null)
            {
                TMP_Text buttonText = zoomButton.GetComponent<TMP_Text>();
                if (buttonText != null)
                {
                    buttonText.text = "Zoom Off";
                }
                ColorBlock colors = zoomButton.colors;
                colors.normalColor = Color.red;
                zoomButton.colors = colors;
            }
        }
        UpdateFOV();
    }

    /// <summary>
    /// Met à jour le FOV (appelé par un slider UI).
    /// </summary>
    public void UpdateFOV()
    {
        fieldOfView = fovSlider.value/_zoomFactor;
        UpdateText();

        if (_isScanning)
        {
            StopScanning();
            ToggleScan();
        }
    }

    public void UpdateDynamic()
    {
        maxExpectedLuminosity = dynSlider.value;
    }

    /// <summary>
    /// Met à jour la taille des blocs (appelé par un slider UI).
    /// </summary>
    public void UpdateBlockSize()
    {
        // Forcer une valeur impaire
        int newBlockSize = Mathf.RoundToInt(resSlider.value);
        if (newBlockSize % 2 == 0) newBlockSize++;

        blockSize = Mathf.Clamp(newBlockSize, 1, 20);
        _scanResolution = _displayWidth / blockSize;

        if (_isScanning)
        {
            StopScanning();
            _scannedTexture = new Texture2D(_displayWidth, _displayHeight);
            display.texture = _scannedTexture;
            ToggleScan();
        }
        else
        {
            _scannedTexture = new Texture2D(_displayWidth, _displayHeight);
            display.texture = _scannedTexture;
        }

        UpdateText();
    }

    /// <summary>
    /// Met à jour le gain du capteur (appelé par un slider UI).
    /// </summary>
    public void UpdateSensorGain()
    {
        sensorGain = gainSlider.value;
        UpdateText();
    }

    /// <summary>
/// Méthode appelée quand un corps est sélectionné dans le dropdown.
/// </summary>
public void OnBodySelected()
{
    int index = bodyList.value; //récupérer l'index de l'item séléctionné.
    if (index == 0)
    {
        // Aucune sélection (option par défaut)
        _selectedBody = null;
        return;
    }

    // Mettre à jour le corps sélectionné
    _selectedBody = _celestialBodies[index - 1];

    // Calculer les offsets pour centrer l'objet sélectionné
    if (_selectedBody != null)
    {
        offsetAzimuth = _selectedBody.azimuth;
        offsetElevation = _selectedBody.elevation;
    }

    Debug.Log("Corps sélectionné : " + _selectedBody.bodyName);
}


    // =========================================================================
    #endregion

    // =========================================================================
    #region MÉTHODES PRIVÉES (UTILITAIRES)
    // =========================================================================

    private void UpdateText()
    {
        fovText.text = "FOV : "+fieldOfView+"°";
        gainText.text = "Gain x "+sensorGain;
        resolutionText.text = "Res : "+fieldOfView/_scanResolution+"°/px";
    }

    /// <summary>
    /// Efface l'image du scan.
    /// </summary>
    private void ClearImage()
    {
        Color[] clearColors = new Color[_displayWidth * _displayHeight];
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = Color.black;
        }
        _scannedTexture.SetPixels(clearColors);
        _scannedTexture.Apply();
    }

    /// <summary>
    /// Arrête le scan en cours.
    /// </summary>
    private void StopScanning()
    {
        if (_isScanning)
        {
            _isScanning = false;
            if (_scanCoroutine != null)
                StopCoroutine(_scanCoroutine);
        }
    }

    /// <summary>
    /// Trouve tous les objets célestes dans la scène au démarrage.
    /// </summary>
    private void FindAllCelestialBodies()
    {
        CelestialBody[] bodies = FindObjectsByType<CelestialBody>(FindObjectsSortMode.None);
        if(!_celestialBodies.Contains(bodies.First<CelestialBody>()))
        {
            _celestialBodies.Clear();
            _celestialBodies.AddRange(bodies);
            bodyList.ClearOptions();
            List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
            foreach (CelestialBody body in _celestialBodies)
            {
                options.Add(new TMP_Dropdown.OptionData(body.bodyName));
            }
            options = options.OrderBy(option => option.text).ToList();
            options.Insert(0,new TMP_Dropdown.OptionData("None"));
            bodyList.options = options;
            bodyList.value = 0;
            OnBodySelected();
        }
    }

    /// <summary>
    /// Initialise le bouton Zoom.
    /// </summary>
    private void InitializeZoomButton()
    {
        if (zoomButton != null)
        {
            TextMeshProUGUI buttonText = zoomButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = "Zoom Off";
            }

            ColorBlock colors = zoomButton.colors;
            colors.normalColor = Color.red;
            zoomButton.colors = colors;
        }
    }

private float CalculateDirectionalLuminosity(float azimuth, float elevation)
{
    float totalLuminosity = UnityEngine.Random.Range(0, 0.005f);

    // Résolution angulaire d'un bloc (en degrés)
    float blockAngularResolution = fieldOfView / _scanResolution;

    // Trier les objets par distance
    List<CelestialBody> sortedBodies = _celestialBodies.OrderBy(body => body.distance).ToList();

    foreach (CelestialBody body in sortedBodies)
    {
        if (body.distance > 5000f) continue;

        // Calculer le rayon angulaire de l'objet
        float angularRadius = body.angularSize;
        float sigma = angularRadius*1.25f;
        float bodyAzimuth = body.azimuth;
        float bodyElevation = body.elevation;

        // Calculer l'angle entre la direction du capteur et l'objet
        float azimuthDiff = Mathf.Abs(azimuth - bodyAzimuth);
        float elevationDiff = Mathf.Abs(elevation - bodyElevation);

        // Si l'objet est "dans cette direction" (chevauche le bloc)
        if (azimuthDiff < angularRadius + blockAngularResolution / 2f &&
            elevationDiff < angularRadius + blockAngularResolution / 2f)
        {
            // Calculer la fraction de chevauchement
            float overlapFraction = CalculateOverlapFraction(
                azimuthDiff, elevationDiff,
                angularRadius, blockAngularResolution / 2f);

            // Pondération gaussienne pour les objets proches des bords
            float weight = Mathf.Exp(-0.5f * (azimuthDiff * azimuthDiff + elevationDiff * elevationDiff) / (sigma * sigma));

            // Contribution totale = luminosité * fraction de chevauchement * poids gaussien
            float bodyLuminosity = body.apparentLuminosity;
            totalLuminosity += bodyLuminosity * overlapFraction * weight;
        }
    }

    return NormalizeLuminosity(totalLuminosity);
}

/// <summary>
/// Calcule la fraction de chevauchement entre un objet et un bloc.
/// </summary>
private float CalculateOverlapFraction(float azimuthDiff, float elevationDiff,
    float angularRadius, float halfBlockResolution)
{
    // Distance normalisée entre le centre de l'objet et le centre du bloc
    float normalizedAzimuthDiff = azimuthDiff / (angularRadius + halfBlockResolution);
    float normalizedElevationDiff = elevationDiff / (angularRadius + halfBlockResolution);

    // Calculer la distance normalisée au centre
    float normalizedDistanceToCenter = Mathf.Sqrt(
        normalizedAzimuthDiff * normalizedAzimuthDiff +
        normalizedElevationDiff * normalizedElevationDiff);

    // Si l'objet est complètement dans le bloc
    if (normalizedDistanceToCenter <= halfBlockResolution / (angularRadius + halfBlockResolution))
        return 1f;

    // Si l'objet est complètement hors du bloc
    if (normalizedDistanceToCenter >= 1f)
        return 0f;

    // Fraction de chevauchement (1 à la frontière, 0 en dehors)
    float overlapFraction = 1f - normalizedDistanceToCenter;
    return Mathf.Clamp01(overlapFraction);
}


    /// <summary>
    /// Dessine une ligne de balayage à la position spécifiée.
    /// </summary>
    /// <param name="y">Position verticale de la ligne.</param>
    /// <param name="color">Couleur de la ligne (0 ou 1).</param>
    private void DrawScanLine(int y, int color)
    {
        Color lineColor = color == 1 ? Color.darkGreen : Color.yellow;

        for (int x = 0; x < 5; x++)
        {
            if (x < _displayWidth && y < _displayHeight)
                _scannedTexture.SetPixel(x, y, lineColor);
        }

        for (int x = _displayWidth - 1; x >= _displayWidth - 5; x--)
        {
            if (x >= 0 && y < _displayHeight)
                _scannedTexture.SetPixel(x, y, lineColor);
        }
    }

    /// <summary>
    /// Convertit une luminosité en couleur (noir → vert → blanc).
    /// </summary>
    /// <param name="luminosity">Luminosité (0 à 1).</param>
    /// <returns>Couleur correspondante.</returns>
    private Color GetIntensityColor(float luminosity)
    {
        if (luminosity < 0.5f)
        {
            float t = luminosity * 2f;
            return new Color(0, t, 0);
        }
        else
        {
            float t = (luminosity - 0.5f) * 2f;
            return new Color(t, 1, t);
        }
    }
/// <summary>
    /// Normalise la luminosité en utilisant une échelle logarithmique et une compression de dynamique.
    /// </summary>
    /// <param name="luminosity">Luminosité brute.</param>
    /// <returns>Luminosité normalisée (0 à 1).</returns>
    private float NormalizeLuminosity(float luminosity)
    {/*
        // Appliquer une compression de dynamique
        float compressedLuminosity = Mathf.Pow(luminosity, 1f / dynamicCompressionFactor);

        // Échelle logarithmique pour compresser les valeurs élevées
        float logLuminosity = Mathf.Log10(1f + compressedLuminosity);

        // Normaliser en fonction de maxExpectedLuminosity
        float normalizedLogLuminosity = logLuminosity / Mathf.Log10(1f + maxExpectedLuminosity);

        // Garantir que les étoiles faibles soient visibles
        return Mathf.Clamp01(normalizedLogLuminosity);*/
        return ApplyCompression(new float[]{luminosity}, 0,dynamicCompressionFactor,maxExpectedLuminosity)[0];
    }

    /// <summary>
/// Dessine les cercles de debug pour chaque objet céleste.
/// </summary>
private void DrawDebugCircles()
{
    // Effacer la texture de debug
    Color[] clearColors = new Color[_debugTexture.width * _debugTexture.height];
    for (int i = 0; i < clearColors.Length; i++)
    {
        clearColors[i] = new Color(0, 0, 0, 0); // Transparent
    }
    _debugTexture.SetPixels(clearColors);

    // Dessiner un cercle pour chaque objet céleste
    foreach (CelestialBody body in _celestialBodies)
    {
        if (body.distance > maxScanDistance) continue;

        // Calculer la position de l'objet sur la texture
        float azimuth = body.azimuth;
        float elevation = body.elevation;

        // Convertir les angles en coordonnées de texture
        int centerX = _debugTexture.width / 2;
        int centerY = _debugTexture.height / 2;

        // Calculer la position relative de l'objet
        float relX = (azimuth / (fieldOfView / 2f)) * (centerX);
        float relY = (elevation / (fieldOfView / 2f)) * (centerY);

        int objX = centerX + Mathf.RoundToInt(relX);
        int objY = centerY + Mathf.RoundToInt(relY);

        // Calculer le rayon du cercle en pixels
        float angularRadius = CalculateAngularRadiusFromCollider(body.gameObject);
        float pixelRadius = (angularRadius / fieldOfView ) * _debugTexture.width;

        // Dessiner le cercle
        DrawCircle(_debugTexture, objX, objY, Mathf.RoundToInt(pixelRadius), new Color(255,0,0,200));
    }

    _debugTexture.Apply();
}

/// <summary>
/// Dessine un cercle sur une texture.
/// </summary>
/// <param name="texture">Texture sur laquelle dessiner.</param>
/// <param name="centerX">Position X du centre du cercle.</param>
/// <param name="centerY">Position Y du centre du cercle.</param>
/// <param name="radius">Rayon du cercle en pixels.</param>
/// <param name="color">Couleur du cercle.</param>
private void DrawCircle(Texture2D texture, int centerX, int centerY, int radius, Color color)
{
    int radiusSquared = radius * radius;

    int startX = Mathf.Max(0, centerX - radius);
    int endX = Mathf.Min(texture.width, centerX + radius);
    int startY = Mathf.Max(0, centerY - radius);
    int endY = Mathf.Min(texture.height, centerY + radius);

    for (int y = startY; y < endY; y++)
    {
        for (int x = startX; x < endX; x++)
        {
            int dx = x - centerX;
            int dy = y - centerY;
            if (dx * dx + dy * dy <= radiusSquared)
            {
                texture.SetPixel(x, y, color);
            }
        }
    }
}

/// <summary>
/// Calcule le rayon angulaire de l'objet à partir de son collider.
/// </summary>
/// <param name="observerPosition">Position de l'observateur (ex: vaisseau).</param>
/// <returns>Rayon angulaire en degrés.</returns>
public float CalculateAngularRadiusFromCollider(GameObject target)
{
    // Obtenir le rayon du collider (supposons que c'est un SphereCollider)
    SphereCollider collider = target.GetComponent<SphereCollider>();
    if (collider == null)
    {
        Debug.LogError("Pas de SphereCollider sur cet objet !");
        return 0f;
    }

    float objectRadius = collider.radius * transform.lossyScale.x; // Rayon en unités Unity
    float distance = Vector3.Distance(transform.position, playerShip.position);

    // Calculer le rayon angulaire
    float angularRadius = Mathf.Atan2(objectRadius, distance) * Mathf.Rad2Deg;
    return angularRadius;
}





    // =========================================================================
    #endregion

    // =========================================================================
    #region COROUTINES
    // =========================================================================

/// <summary>
/// Coroutine pour effectuer le scan passif avec lissage des bords.
/// </summary>
private IEnumerator ScanRoutine()
{
    Color[] blockColors = new Color[blockSize * blockSize];
    int col = 0;

    // Tableau pour stocker les luminosités des blocs adjacents (pour le lissage)
    int integrator = 0;
    int maxIntegrator = 20;
    List<float[,]> integratorGrid = new List<float[,]>();
    integratorGrid.Add(new float[_scanResolution,_scanResolution]);
    
    while (_isScanning)
    {
        for (int sy = 0; sy < _scanResolution; sy++)
        {
            _currentScanLine = sy * blockSize;
            Spectrometer spcr = GameObject.FindAnyObjectByType<Spectrometer>();
            spcr.UpdateSpectrometry();
            for (int sx = 0; sx < _scanResolution; sx++)
            {
                FindAllCelestialBodies();
                if (_selectedBody != null)
                {   
                    offsetAzimuth = _selectedBody.azimuth;
                    offsetElevation = _selectedBody.elevation;
                }
                float azimuth = Mathf.Lerp(-fieldOfView / 2f / _zoomFactor, fieldOfView / 2f / _zoomFactor, (float)sx / _scanResolution) + offsetAzimuth;
                float elevation = Mathf.Lerp(-fieldOfView / 2f / _zoomFactor, fieldOfView / 2f / _zoomFactor, (float)sy / _scanResolution) + offsetElevation;
                // Calculer la luminosité dans cette direction
                float luminosity = CalculateDirectionalLuminosity(azimuth, elevation);
                integratorGrid[integrator][sx, sy] = luminosity;
                luminosity = 0;
                for(int i = 0; i< integratorGrid.Count; i++)
                {
                    luminosity += integratorGrid[i][sx,sy];
                }
                luminosity = luminosity / (float)(integrator+1f);

                // Convertir en couleur
                Color blockColor = GetIntensityColor(luminosity*sensorGain);
                for(int i = 0; i< blockColors.Length;i++)
                    {
                        blockColors[i] = blockColor;
                    }

                // Appliquer la couleur au bloc
                int startX = sx * blockSize;
                int startY = sy * blockSize;
                _scannedTexture.SetPixels(startX, startY, blockSize, blockSize, blockColors);
            }
            DrawScanLine(_currentScanLine, col % 2);
            // Mise à jour progressive de la texture
            _scannedTexture.Apply();
            DrawDebugCircles();
            yield return new WaitForSeconds(updateInterval);
        }
        col++;
        if(++integrator > maxIntegrator-1)
        {
            integrator = 19;
            integratorGrid.RemoveAt(0);
        }
        integratorGrid.Add(new float[_scanResolution,_scanResolution]);
    }
}

    // =========================================================================
    #endregion
}
