using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

/// <summary>
/// Gère le système de scan et d'imagerie pour le projet Spaceship Bridge.
/// Ce script permet de détecter les objets célestes et d'afficher une représentation visuelle
/// avec une mire de visée et un système de zoom.
/// </summary>
public class Imager : MonoBehaviour
{
    // =========================================================================
    #region VARIABLES PUBLIQUES (CONFIGURATION DANS L'INSPECTEUR)
    // =========================================================================

    #region Display Settings
    /// <summary> Référence au RawImage pour l'affichage du scan. </summary>
    public RawImage display;

    /// <summary> Référence au vaisseau du joueur. </summary>
    public Transform playerShip;

    /// <summary> Référence à l'étoile pour calculer la phase des objets célestes. </summary>
    public Transform star;
    #endregion

    #region Scan Settings
    /// <summary> Distance maximale de détection (en unités Unity). </summary>
    public float maxScanDistance = 500f;

    /// <summary> Champ de vision en degrés (horizontal et vertical). </summary>
    public float fieldOfView;

    /// <summary> Taille d'un bloc de pixels (ex: 2x2, 10x10). </summary>
    public int blockSize = 2;

    /// <summary> Intervalle de mise à jour du scan (en secondes). </summary>
    public float updateInterval = 0.05f;
    #endregion

    #region UI Controls
    /// <summary> Slider pour ajuster le champ de vision (FOV). </summary>
    public Slider fovSlider;

    /// <summary> Slider pour ajuster la résolution (taille des blocs). </summary>
    public Slider resSlider;

    /// <summary> Slider pour ajuster le gain (intensité du signal). </summary>
    public Slider gainSlider;

    /// <summary> Référence directe au bouton Zoom. </summary>
    public Button zoomButton;
    public TMP_Text fovValue;
    public TMP_Text gainValue;
    public TMP_Text resValue;
    #endregion

    #region Crosshair Settings
    /// <summary> Sprite de la mire normale (avec carré central). </summary>
    public Sprite normalCrosshairSprite;

    /// <summary> Sprite de la mire en mode zoom (croix simple). </summary>
    public Sprite zoomCrosshairSprite;

    /// <summary> Référence à l'Image UI pour la mire. </summary>
    public Image crosshairImage;
    #endregion
    #endregion
    // =========================================================================
    #region VARIABLES PRIVÉES
    // =========================================================================

    /// <summary> Texture utilisée pour afficher le scan. </summary>
    private Texture2D _scannedTexture;

    /// <summary> Indique si le scan est en cours. </summary>
    private bool _isScanning;

    /// <summary> Coroutine pour le scan en cours. </summary>
    private Coroutine _scanCoroutine;

    /// <summary> Largeur et hauteur de l'affichage. </summary>
    private int _displayWidth, _displayHeight;

    /// <summary> Résolution du scan (nombre de blocs). </summary>
    private int _scanResolution;

    /// <summary> Ligne actuelle du balayage. </summary>
    private int _currentScanLine;

    /// <summary> Indique si le mode zoom est activé. </summary>
    private bool isZoomed = false;

    /// <summary> Facteur de zoom (1 = pas de zoom, 10 = zoom x10). </summary>
    private float fovFactor = 1f;
    private float gain;
    #endregion
    // =========================================================================
    #region MÉTHODES UNITY (MONOBEHAVIOUR)
    // =========================================================================

    /// <summary>
    /// Initialisation au démarrage.
    /// </summary>
    private void Start()
    {
        // Initialisation des paramètres de base
        fieldOfView = fovSlider.value/fovFactor;
        fovValue.text = "FOV : "+fieldOfView+"°";
        blockSize = (int)resSlider.value;
        gain = gainSlider.value;
        gainValue.text = "Gain x "+gain;

        // Récupérer la taille d'affichage
        _displayWidth = (int)display.rectTransform.rect.width;
        _displayHeight = (int)display.rectTransform.rect.height;
        _scanResolution = Mathf.Min(_displayWidth, _displayHeight) / blockSize;
        resValue.text = "Res : "+(fieldOfView/_scanResolution).ToString("F3")+"°/px";

        // Initialisation de la texture
        _scannedTexture = new Texture2D(_displayWidth, _displayHeight);
        display.texture = _scannedTexture;
        ClearImage();

        // Trouver le vaisseau du joueur
        playerShip = GameObject.FindGameObjectWithTag("PlayerShip").transform;

        // Initialisation du bouton Zoom
        InitializeZoomButton();
        StartScanning();
    }
    #endregion

    // =========================================================================
    #region MÉTHODES PUBLIQUES (APPELÉES PAR L'UI)
    // =========================================================================

    /// <summary> Démarre ou arrête le scan. </summary>
    public void StartScanning()
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

    /// <summary> Active ou désactive le mode zoom. </summary>
    public void ToggleZoom()
    {
        isZoomed = !isZoomed;
        fovFactor = isZoomed ? 10f : 1f;
        fovChanged();

        // Mise à jour de la mire
        if (crosshairImage != null)
        {
            crosshairImage.sprite = isZoomed ? zoomCrosshairSprite : normalCrosshairSprite;
        }

        // Mise à jour du bouton Zoom
        UpdateZoomButton();
    }

    /// <summary> Met à jour le champ de vision (FOV) en fonction du slider. </summary>
    public void fovChanged()
    {
        fieldOfView = fovSlider.value / fovFactor;
        fovValue.text = "FOV : "+fieldOfView+"°";
        resValue.text = "Res : "+(fieldOfView/_scanResolution).ToString("F3")+"°/px";
        if (_isScanning)
        {
            StopScanning();
            StartScanning();
        }
    }

    /// <summary> Met à jour la résolution en fonction du slider. </summary>
    public void resChanged()
    {
        int previous = blockSize;
        if((int)resSlider.value%2==0)
        {
            blockSize = (int)resSlider.value+1;
        }
        else
        {
            blockSize = (int)resSlider.value;
        }
        if(blockSize == previous) return;
        _scanResolution = Mathf.Min(_displayWidth, _displayHeight) / blockSize;
        resValue.text = "Res : "+(fieldOfView/_scanResolution).ToString("F3")+"°/px";

        if (_isScanning)
        {
            StopScanning();
            _scannedTexture = new Texture2D(_displayWidth, _displayHeight);
            display.texture = _scannedTexture;
            StartScanning();
        }
        else
        {
            _scannedTexture = new Texture2D(_displayWidth, _displayHeight);
            display.texture = _scannedTexture;
        }
    }

    public void gainChanged()
    {
        gain = gainSlider.value;
        gainValue.text = "Gain x "+gain;
    }
    #endregion

    // =========================================================================
    #region MÉTHODES PRIVÉES (UTILITAIRES)
    // =========================================================================

    /// <summary> Efface l'image du scan. </summary>
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

    /// <summary> Arrête le scan en cours. </summary>
    public void StopScanning()
    {
        if (_isScanning)
        {
            _isScanning = false;
            if (_scanCoroutine != null)
                StopCoroutine(_scanCoroutine);
        }
    }

    /// <summary> Initialise le bouton Zoom. </summary>
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

    /// <summary> Met à jour l'apparence du bouton Zoom. </summary>
    private void UpdateZoomButton()
    {
        if (zoomButton != null)
        {
            TextMeshProUGUI buttonText = zoomButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = isZoomed ? "Zoom On" : "Zoom Off";
            }

            ColorBlock colors = zoomButton.colors;
            colors.normalColor = isZoomed ? Color.green : Color.red;
            zoomButton.colors = colors;
        }
    }
    #endregion

    // =========================================================================
    #region COROUTINES
    // =========================================================================

    /// <summary> Coroutine pour le scan en cours. </summary>
    private IEnumerator ScanRoutine()
    {
        Color[] blockColors = new Color[blockSize * blockSize];
        RaycastHit[] raycastHits = new RaycastHit[1];
        int col = 0;

        while (_isScanning)
        {
            for (int sy = 0; sy < _scanResolution; sy++)
            {
                _currentScanLine = sy * blockSize;

                for (int sx = 0; sx < _scanResolution; sx++)
                {
                    // Calcul de la direction du rayon
                    float angleX = Mathf.Lerp(-fieldOfView / 2, fieldOfView / 2, (float)sx / _scanResolution) * Mathf.Deg2Rad;
                    float angleY = Mathf.Lerp(-fieldOfView / 2, fieldOfView / 2, (float)sy / _scanResolution) * Mathf.Deg2Rad;

                    Vector3 rayDirection = playerShip.forward;
                    rayDirection = Quaternion.AngleAxis(angleX * Mathf.Rad2Deg, playerShip.up) * rayDirection;
                    rayDirection = Quaternion.AngleAxis(angleY * Mathf.Rad2Deg, playerShip.right) * rayDirection;
                    rayDirection.Normalize();

                    // Lancer un Raycast
                    int hitCount = Physics.RaycastNonAlloc(playerShip.position, rayDirection, raycastHits, maxScanDistance);

                    // Déterminer la couleur du bloc
                    Color blockColor;
                    if (hitCount > 0)
                    {
                        CelestialBody celestialBody = raycastHits[0].collider.GetComponent<CelestialBody>();
                        if (celestialBody != null)
                        {
                            float finalIntensity = celestialBody.apparentLuminosity *gain;
                            blockColor = GetIntensityColor(finalIntensity);
                        }
                        else
                        {
                            float intensity = Mathf.Clamp01((1f - (raycastHits[0].distance / maxScanDistance)) * gain);
                            blockColor = GetIntensityColor(intensity);
                        }
                    }
                    else
                    {
                        blockColor = GetIntensityColor(Random.Range(0f, 0.0001f) * gain);
                    }

                    // Appliquer la couleur au bloc
                    for (int i = 0; i < blockColors.Length; i++)
                    {
                        blockColors[i] = blockColor;
                    }

                    int startX = sx * blockSize;
                    int startY = sy * blockSize;
                    _scannedTexture.SetPixels(startX, startY, blockSize, blockSize, blockColors);
                }

                yield return new WaitForSeconds(updateInterval);
                DrawScanLine(_currentScanLine, col % 2);
                _scannedTexture.Apply();
            }
            col++;
        }
    }
    #endregion

    // =========================================================================
    #region MÉTHODES DE DESSIN
    // =========================================================================

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
    /// Convertit une intensité en couleur (noir → vert → blanc).
    /// </summary>
    /// <param name="intensity">Intensité (0 à 1).</param>
    /// <returns>Couleur correspondante.</returns>
    private Color GetIntensityColor(float intensity)
    {
        if (intensity < 0.5f)
        {
            // Dégradé noir → vert
            float t = intensity * 2f;
            return new Color(0, t, 0);
        }
        else
        {
            // Dégradé vert → blanc
            float t = (intensity - 0.5f) * 2f;
            return new Color(t, 1, t);
        }
    }
    #endregion
}
