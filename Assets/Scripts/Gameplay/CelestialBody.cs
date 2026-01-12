using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public class CelestialBody : MonoBehaviour
{
    // Coordonnées sphériques
    public float azimuth;   // Azimut (en degrés)
    public float elevation; // Élévation (en degrés)
    public float distance;  // Distance (en unités de jeu)
    public float angularSize; // Taille angulaire (en degrés)

    // Propriétés physiques
    public string bodyName;  // Nom de l'objet céleste
    public string bodyType;  // Type de l'objet (ex: "Rocheuse", "Gazeuse", "Glacée")
    public float temperature; // Température en Kelvin
    public float mass;        // Masse de l'objet
    public float radius;      // Rayon de l'objet (en unités de jeu)
    public float density;
    public float solRadius;
    public float starLuminosity = 0; // 0 pour les planètes, appliqué aux étoiles
    public List<ChemicalComposition> chemicalComposition; // Composition chimique de l'astre
    public Spectrum spectrum; // Spectre d'émission/absorption de l'astre

    // Paramètres d'orbite
    public float orbitalPeriod = 10f;         // Période orbitale en secondes
    public float orbitalInclination = 0f;    // Inclinaison de l'orbite en degrés
    public float orbitalRadius = 10f;         // Rayon de l'orbite en unités de jeu
    public float orbitalEccentricity = 0f;    // Excentricité de l'orbite (0 = cercle, 0.9 = très elliptique)
    public Transform centralBody;             // Corps central autour duquel l'objet orbite
    public float orbitalAngle;                // Angle orbital initial

    public float albedo;
    public float phase;
    public float apparentLuminosity;

    // Constante pour la conversion des unités
    private const float UA_TO_GAME_UNITS = 10f; // 1 UA = 10 unités de jeu

    // Variables internes pour le calcul des orbites
    private float meanAnomaly;                // Anomalie moyenne
    private float currentOrbitalAngle;        // Angle orbital actuel

    void Start()
    {
        StartCoroutine(Orbit());
    }

    void OnEnable()
    {
        StartCoroutine(Orbit());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    // Initialise les valeurs
    public void SetData(float az, float el, float dist)
    {
        azimuth = az;
        elevation = el;
        distance = dist;
    }

    public (float Az, float El, float Dist) GetData()
    {
        return (azimuth, elevation, distance);
    }

    // Calcule les données de position
    private (float azimuth, float elevation, float distance) CalculatePositionData()
    {
        GameObject playerShip = GameObject.FindGameObjectWithTag("PlayerShip");
        if (playerShip == null)
        {
            Debug.LogError("PlayerShip non trouvé !");
            return (0, 0, 0);
        }

        // Position relative de l'objet par rapport au vaisseau
        Vector3 relativePosition = transform.position - playerShip.transform.position;

        // Direction vers laquelle le vaisseau fait face (axe "forward")
        Vector3 shipForward = playerShip.transform.forward;
        shipForward.y = 0; // On ignore l'inclinaison verticale pour l'azimut
        shipForward.Normalize();

        // Projection de la position relative dans le plan horizontal (XZ)
        Vector3 relativePositionXZ = relativePosition;
        relativePositionXZ.y = 0;

        // Distance
        float distance = relativePosition.magnitude;

        // Azimut relatif (angle entre la direction du vaisseau et la position de l'objet)
        float azimuth = Vector3.SignedAngle(shipForward, relativePositionXZ, Vector3.up);

        // Élévation (angle vertical)
        float elevation = Mathf.Atan2(relativePosition.y, relativePositionXZ.magnitude) * Mathf.Rad2Deg;

        return (azimuth, -elevation, distance);
    }

    // Simule l'orbite
    IEnumerator Orbit()
    {
        while (true)
        {
            if (GameObject.FindGameObjectWithTag("PlayerShip") == null)
            {
                yield return null;
            }
            if (centralBody != null)
            {
                // Calculer l'anomalie moyenne (augmente linéairement avec le temps)
                meanAnomaly += (360f / orbitalPeriod) * Time.deltaTime / 10f;
                if (meanAnomaly >= 360f)
                {
                    meanAnomaly -= 360f;
                }

                // Calculer l'anomalie excentrique (pour les orbites elliptiques)
                float eccentricAnomaly = CalculateEccentricAnomaly(meanAnomaly, orbitalEccentricity);

                // Calculer l'anomalie vraie (position réelle sur l'orbite)
                float trueAnomaly = CalculateTrueAnomaly(eccentricAnomaly, orbitalEccentricity);

                // Calculer la distance radiale (pour les orbites elliptiques)
                float radialDistance = orbitalRadius * (1 - orbitalEccentricity * orbitalEccentricity) /
                                      (1 + orbitalEccentricity * Mathf.Cos(Mathf.Deg2Rad * trueAnomaly));

                // Position dans le plan orbital
                float x = radialDistance * Mathf.Cos(Mathf.Deg2Rad * trueAnomaly);
                float z = radialDistance * Mathf.Sin(Mathf.Deg2Rad * trueAnomaly);

                // Appliquer l'inclinaison orbitale
                float y = radialDistance * Mathf.Sin(Mathf.Deg2Rad * orbitalInclination) * Mathf.Sin(Mathf.Deg2Rad * trueAnomaly);

                // Position finale
                Vector3 orbitalPosition = new Vector3(x, y, z);

                // Positionner l'objet autour du corps central
                transform.position = centralBody.position + orbitalPosition;

                (float a, float e, float d) = CalculatePositionData();
                SetData(a, e, d);
                angularSize = Mathf.Atan2(radius, distance) * Mathf.Rad2Deg;
                phase = CalculatePhase(centralBody, GameObject.FindGameObjectWithTag("PlayerShip").transform);
                apparentLuminosity = CalculateLuminosity(centralBody, GameObject.FindGameObjectWithTag("PlayerShip").transform);
            }
            else
            {
                (float a, float e, float d) = CalculatePositionData();
                SetData(a, e, d);
                phase = 1;
                apparentLuminosity = CalculateLuminosity(transform, GameObject.FindGameObjectWithTag("PlayerShip").transform);
                angularSize = (radius / d) * Mathf.Rad2Deg;
            }
            yield return null;
        }
    }

    // Calcule l'anomalie excentrique à partir de l'anomalie moyenne (méthode de Newton-Raphson)
    private float CalculateEccentricAnomaly(float meanAnomaly, float eccentricity)
    {
        float eccentricAnomaly = meanAnomaly;
        float tolerance = 0.001f;
        int maxIterations = 100;
        int iterations = 0;

        while (iterations < maxIterations)
        {
            float delta = eccentricAnomaly - eccentricity * Mathf.Sin(Mathf.Deg2Rad * eccentricAnomaly) - meanAnomaly;
            if (Mathf.Abs(delta) < tolerance)
            {
                break;
            }
            float derivative = 1 - eccentricity * Mathf.Cos(Mathf.Deg2Rad * eccentricAnomaly);
            eccentricAnomaly -= delta / derivative;
            iterations++;
        }

        return eccentricAnomaly;
    }

    // Calcule l'anomalie vraie à partir de l'anomalie excentrique
    private float CalculateTrueAnomaly(float eccentricAnomaly, float eccentricity)
    {
        float trueAnomaly = 2 * Mathf.Rad2Deg * Mathf.Atan2(
            Mathf.Sqrt(1 + eccentricity) * Mathf.Sin(Mathf.Deg2Rad * eccentricAnomaly / 2),
            Mathf.Sqrt(1 - eccentricity) * Mathf.Cos(Mathf.Deg2Rad * eccentricAnomaly / 2)
        );
        return trueAnomaly;
    }

    // Calcule la phase de l'objet (0 = nouvelle phase, 1 = pleine phase)
    public float CalculatePhase(Transform star, Transform observer)
    {
        // Les étoiles ont toujours une phase de 1 (pleine luminosité)
        if (starLuminosity > 0)
        {
            return 1f;
        }

        // Calculer la phase pour les planètes
        Vector3 toStar = (star.position - transform.position).normalized;
        Vector3 toObserver = (observer.position - transform.position).normalized;
        float phaseAngle = Vector3.Angle(toStar, toObserver);

        // Normaliser la phase (1 = pleine phase, 0 = nouvelle phase)
        return 1 - Mathf.Clamp01(phaseAngle / 180f);
    }

    // Calcule la luminosité apparente de l'objet
    public float CalculateLuminosity(Transform star, Transform observer)
    {
        float phase = CalculatePhase(star, observer);
        float distanceToObserver = Vector3.Distance(transform.position, observer.position); // Convertir en UA

        // Pour les étoiles, la luminosité dépend de leur luminosité intrinsèque et de la distance
        if (starLuminosity > 0)
        {
            return starLuminosity / (distanceToObserver * distanceToObserver);
        }
        // Pour les planètes, la luminosité dépend de l'albedo, de la phase et de la distance
        else
        {
            CelestialBody starBody = star.GetComponent<CelestialBody>();
            if (starBody != null)
            {
                return starBody.starLuminosity * albedo * phase / (distanceToObserver * distanceToObserver);
            }
            else
            {
                return 0f;
            }
        }
    }
}
