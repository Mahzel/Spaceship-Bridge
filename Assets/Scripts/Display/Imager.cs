using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using Unity.Mathematics.Geometry;

public class Imager : MonoBehaviour
{
    public RawImage display; // Référence au RawImage pour l'affichage
    public Transform playerShip; // Référence au vaisseau du joueur
    public float maxScanDistance = 500f; // Distance maximale de détection
    public float fieldOfView; // Champ de vision en degrés (horizontal et vertical)
    public int blockSize = 2; // Taille d'un bloc de pixels (ex: 10x10)
    public float updateInterval = 0.05f; // Intervalle de mise à jour
    public Slider fovSlider;
    public Slider resSlider;
    public Slider gainSlider;

    private Texture2D _scannedTexture;
    private bool _isScanning;
    private Coroutine _scanCoroutine;
    private int _displayWidth, _displayHeight; // Taille d'affichage
    private int _scanResolution; // Résolution du scan (calculée automatiquement)

    void Start()
    {
        fieldOfView = fovSlider.value;
        blockSize = (int)resSlider.value;
        // Récupérer la taille d'affichage
        _displayWidth = (int)display.rectTransform.rect.width;
        _displayHeight = (int)display.rectTransform.rect.height;

        // Calculer la résolution du scan en fonction de la taille des blocs
        _scanResolution = Mathf.Min(_displayWidth, _displayHeight) / blockSize;

        // Créer la texture
        _scannedTexture = new Texture2D(_displayWidth, _displayHeight);
        display.texture = _scannedTexture;
        ClearImage();
        playerShip = GameObject.FindGameObjectWithTag("PlayerShip").transform;
    }

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

    public void fovChanged()
    {
        if(_isScanning)
        {
            StopScanning();
            fieldOfView = fovSlider.value;
            StartScanning();
        }
        else fieldOfView = fovSlider.value;

    }

    public void resChanged()
    {
        if(_isScanning)
        {
            StopScanning();
            blockSize = (int)resSlider.value;
            _scanResolution = Mathf.Min(_displayWidth, _displayHeight) / blockSize;
            _scannedTexture = new Texture2D(_displayWidth, _displayHeight);
            display.texture = _scannedTexture;
            StartScanning();
        }
        else
        {
            blockSize = (int)resSlider.value;
            _scanResolution = Mathf.Min(_displayWidth, _displayHeight) / blockSize;
            _scannedTexture = new Texture2D(_displayWidth, _displayHeight);
            display.texture = _scannedTexture; 
        }
    }

    private void ClearImage()
    {
        Color[] clearColors = new Color[_displayWidth * _displayHeight];
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = Color.black; // Couleur noire (ou une autre couleur)
        }
        _scannedTexture.SetPixels(clearColors);
        _scannedTexture.Apply();
    }

    public void StopScanning()
    {
        if (_isScanning)
        {
            _isScanning = false;
            if (_scanCoroutine != null)
                StopCoroutine(_scanCoroutine);
        }
    }

    private IEnumerator ScanRoutine()
    {
        // Tableau pour stocker les couleurs d'un bloc
        Color[] blockColors = new Color[blockSize * blockSize];

        while (_isScanning)
        {
            // Balayer chaque bloc du champ de vision
            for (int sy = 0; sy < _scanResolution; sy++)
            {
                for (int sx = 0; sx < _scanResolution; sx++)
                {
                    // Calculer la direction du rayon pour ce bloc
                    float angleX = Mathf.Lerp(-fieldOfView / 2, fieldOfView / 2, (float)sx / _scanResolution) * Mathf.Deg2Rad;
                    float angleY = Mathf.Lerp(-fieldOfView / 2, fieldOfView / 2, (float)sy / _scanResolution) * Mathf.Deg2Rad;

                    Vector3 rayDirection = playerShip.forward;
                    rayDirection = Quaternion.AngleAxis(angleX * Mathf.Rad2Deg, playerShip.up) * rayDirection;
                    rayDirection = Quaternion.AngleAxis(angleY * Mathf.Rad2Deg, playerShip.right) * rayDirection;
                    rayDirection.Normalize();

                    // Lancer un Raycast
                    RaycastHit hit;
                    bool isHit = Physics.Raycast(playerShip.position, rayDirection, out hit, maxScanDistance);

                    // Déterminer la couleur du bloc en fonction de la détection
                    Color blockColor;
                    if (isHit)
                    {
                        CelestialBody celestialBody = hit.collider.GetComponent<CelestialBody>();
                        if (celestialBody.centralBody != null)
                        {
                            // Normaliser la luminosité pour l'intensité (par exemple, entre 0 et 1)
                            float maxLuminosity = celestialBody.centralBody.GetComponent<CelestialBody>().apparentLuminosity; // À ajuster selon tes besoins
                            float intensity = Mathf.Clamp01(celestialBody.apparentLuminosity*gainSlider.value); // Facteur d'échelle à ajuster

                            blockColor = GetIntensityColor(intensity);
                        }
                        else
                        {
                            // Objet sans CelestialBody (ex: vaisseau, astéroïde)
                            float intensity = Mathf.Clamp01(celestialBody.apparentLuminosity*gainSlider.value);
                            blockColor = GetIntensityColor(intensity);
                        }
                    }
                    else
                    {
                        // Bruit de fond faible
                        blockColor = GetIntensityColor(Random.Range(0f,0.00001f)*gainSlider.value);
                    }

                    // Remplir le tableau de couleurs pour le bloc
                    for (int i = 0; i < blockColors.Length; i++)
                    {
                        blockColors[i] = blockColor;
                    }

                    // Appliquer le bloc à la texture
                    int startX = sx * blockSize;
                    int startY = sy * blockSize;
                    _scannedTexture.SetPixels(startX, startY, blockSize, blockSize, blockColors);
                }
                yield return new WaitForSeconds(updateInterval);
                _scannedTexture.Apply();
            }

            // Appliquer toutes les modifications à la texture
        }
    }

    // Convertir l'intensité en couleur (noir → vert → blanc)
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
}
