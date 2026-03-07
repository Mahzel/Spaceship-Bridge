using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CelestialBody : MonoBehaviour
{
    #region Constants
    private const float SOLAR_RADIUS_IN_AU = 0.00465f; // 1 rayon solaire = 0.00465 UA
    private const float UA_TO_GAME_UNITS = 100f; // 1 UA = 100 unités de jeu
    #endregion

    #region Public Fields
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
    public float solRadius;   // Rayon en rayons solaires
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
    #endregion

    #region Private Fields
    private float meanAnomaly;                // Anomalie moyenne
    private float currentOrbitalAngle;        // Angle orbital actuel
    private Transform playerShip;
    #endregion

    #region Unity Methods
    void OnEnable()
    {
        playerShip = GameObject.FindGameObjectWithTag("PlayerShip").transform;
        StartCoroutine(Orbit());
        StartCoroutine(UpdateData());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }
    #endregion

    #region Data Management
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
    #endregion

    #region Position Calculations
    // Calcule les données de position
    private (float azimuth, float elevation, float distance) CalculatePositionData()
    {
        if (playerShip == null)
        {
            Debug.LogError("PlayerShip non trouvé !");
            return (0, 0, 0);
        }

        // Position relative de l'objet par rapport au vaisseau
        Vector3 relativePosition = transform.position - playerShip.position;

        // Direction vers laquelle le vaisseau fait face (axe "forward")
        Vector3 shipForward = playerShip.forward;
        shipForward.y = 0; // On ignore l'inclinaison verticale pour l'azimut
        shipForward.Normalize();

        // Projection de la position relative dans le plan horizontal (XZ)
        Vector3 relativePositionXZ = relativePosition;
        relativePositionXZ.y = 0;

        // Distance
        float dist = relativePosition.magnitude;

        // Azimut relatif (angle entre la direction du vaisseau et la position de l'objet)
        float az = Vector3.SignedAngle(shipForward, relativePositionXZ, Vector3.up);

        // Élévation (angle vertical)
        float el = Mathf.Atan2(relativePosition.y, relativePositionXZ.magnitude) * Mathf.Rad2Deg;

        return (az, -el, dist);
    }
    #endregion

    #region Orbital Mechanics
    // Simule l'orbite
    IEnumerator Orbit()
    {
        while (true)
        {
            if (playerShip == null)
            {
                yield return null;
                continue;
            }

            if (centralBody != null)
            {
                // Calculer l'anomalie moyenne (augmente linéairement avec le temps)
                meanAnomaly += (360f / orbitalPeriod) * Time.deltaTime / 8760f;
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

                // Position dans le plan orbital (avant inclinaison)
                float x = radialDistance * Mathf.Cos(Mathf.Deg2Rad * trueAnomaly);
                float zFlat = radialDistance * Mathf.Sin(Mathf.Deg2Rad * trueAnomaly);

                // Rotation du plan orbital autour de l'axe X pour appliquer l'inclinaison
                float incRad = Mathf.Deg2Rad * orbitalInclination;
                float y = zFlat * Mathf.Sin(incRad);
                float z = zFlat * Mathf.Cos(incRad);

                // Position finale
                Vector3 orbitalPosition = new Vector3(x, y, z);

                // Positionner l'objet autour du corps central
                transform.position = centralBody.position + orbitalPosition;

                (float a, float e, float d) = CalculatePositionData();
                SetData(a, e, d);
                angularSize = Mathf.Atan2(radius, distance) * Mathf.Rad2Deg;
                phase = CalculatePhase(centralBody, playerShip);
                apparentLuminosity = CalculateLuminosity(centralBody, playerShip);
            }
            else
            {
                (float a, float e, float d) = CalculatePositionData();
                SetData(a, e, d);
                phase = 1;
                apparentLuminosity = CalculateLuminosity(transform, playerShip);
                angularSize = Mathf.Atan2(radius, distance) * Mathf.Rad2Deg;
            }
            yield return null;
        }
    }

    // Calcule l'anomalie excentrique à partir de l'anomalie moyenne (méthode de Newton-Raphson).
    // Tout est traité en radians pour respecter l'équation de Kepler : E - e*sin(E) = M
    private float CalculateEccentricAnomaly(float meanAnomalyDeg, float eccentricity)
    {
        float M = meanAnomalyDeg * Mathf.Deg2Rad; // Conversion initiale en radians
        float E = M;                               // Valeur initiale
        float tolerance = 1e-6f;

        for (int i = 0; i < 100; i++)
        {
            float delta = E - eccentricity * Mathf.Sin(E) - M;
            if (Mathf.Abs(delta) < tolerance) break;
            float derivative = 1f - eccentricity * Mathf.Cos(E);
            E -= delta / derivative;
        }

        return E; // Retourne en radians
    }

    // Calcule l'anomalie vraie à partir de l'anomalie excentrique (reçoit en radians, retourne en degrés)
    private float CalculateTrueAnomaly(float eccentricAnomalyRad, float eccentricity)
    {
        float trueAnomaly = 2f * Mathf.Atan2(
            Mathf.Sqrt(1f + eccentricity) * Mathf.Sin(eccentricAnomalyRad / 2f),
            Mathf.Sqrt(1f - eccentricity) * Mathf.Cos(eccentricAnomalyRad / 2f)
        );
        return trueAnomaly * Mathf.Rad2Deg; // Retourne en degrés pour le reste du pipeline
    }
    #endregion

    #region Luminosity and Phase Calculations
    // Calcule la phase de l'objet selon la loi de Lambert
    // (0 = nouvelle phase / côté nuit, 1 = pleine phase / pleine lune)
    public float CalculatePhase(Transform star, Transform observer)
    {
        if (starLuminosity > 0) return 1f; // Les étoiles sont toujours en pleine phase

        Vector3 toStar     = (star.position     - transform.position).normalized;
        Vector3 toObserver = (observer.position - transform.position).normalized;
        float alpha = Vector3.Angle(toStar, toObserver) * Mathf.Deg2Rad; // Angle de phase en radians

        // Fonction de phase de Lambert : (sin(α) + (π - α)*cos(α)) / π
        float lambertPhase = (Mathf.Sin(alpha) + (Mathf.PI - alpha) * Mathf.Cos(alpha)) / Mathf.PI;
        return Mathf.Clamp01(lambertPhase);
    }

    // Calcule la luminosité apparente de l'objet
    public float CalculateLuminosity(Transform body, Transform observer)
    {
        float distanceToObserver = this.distance / UA_TO_GAME_UNITS;
        if (distanceToObserver <= 0f) return 0f;

        // Pour les étoiles : L_app = L_intrinseque / d²
        if (starLuminosity > 0)
        {
            return starLuminosity / (distanceToObserver * distanceToObserver);
        }
        // Pour les planètes : flux reçu de l'étoile * albedo * phase * section / d_observateur²
        else
        {
            CelestialBody starBody = body.gameObject.GetComponentInParent<CelestialBody>();
            if (starBody != null)
            {
                // Distance étoile → planète en UA
                float distStarToPlanet = Vector3.Distance(transform.position, body.position) / UA_TO_GAME_UNITS;
                if (distStarToPlanet <= 0f) return 0f;

                // Flux solaire reçu par la planète
                float fluxAtPlanet = starBody.starLuminosity / (distStarToPlanet * distStarToPlanet);

                // Luminosité réfléchie vers l'observateur
                float phase = CalculatePhase(body, observer);
                float radiusInUA = radius / UA_TO_GAME_UNITS;
                return fluxAtPlanet * albedo * phase * (radiusInUA * radiusInUA)
                       / (distanceToObserver * distanceToObserver);
            }
            else
            {
                return 0f;
            }
        }
    }

    // Réinitialise l'état orbital (à appeler lors de la réutilisation depuis le pool)
    public void Reset()
    {
        meanAnomaly = 0f;
        currentOrbitalAngle = 0f;
        azimuth = 0f;
        elevation = 0f;
        distance = 0f;
        angularSize = 0f;
        phase = 0f;
        apparentLuminosity = 0f;
    }
    #endregion

    #region Data Update
    // Met à jour les données de position
    IEnumerator UpdateData()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);
            (float a, float e, float d) = CalculatePositionData();
            SetData(a, e, d);
        }
    }
    #endregion
}