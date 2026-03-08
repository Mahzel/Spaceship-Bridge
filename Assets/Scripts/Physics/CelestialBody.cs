using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Ce que le corps EST et ce qu'un observateur VOIT.
/// Ne gère plus la mécanique orbitale — c'est OrbitalComponent qui s'en charge.
///
/// Toutes les propriétés d'observation (azimuth, elevation, distance,
/// apparentLuminosity, angularSize, phase) sont recalculées à partir
/// de la position courante du transform, quel que soit ce qui l'a déplacé
/// (OrbitalComponent, Barycenter, script custom, animation...).
/// </summary>
public class CelestialBody : MonoBehaviour
{
    #region Constants
    private const float UA_TO_GAME_UNITS = 100f;
    #endregion

    #region Physical Properties
    [Header("Propriétés physiques")]
    public string bodyName;
    public string bodyType;
    public float  temperature;
    public float  mass;
    public float  radius;
    public float  density;
    public float  solRadius;
    public float  starLuminosity = 0f; // 0 pour les planètes
    public float  albedo;

    public List<ChemicalComposition> chemicalComposition;
    public Spectrum spectrum;
    #endregion

    #region Observed Properties (read-only for sensors)
    [Header("Données observées — lecture seule")]
    public float azimuth;
    public float elevation;
    public float distance;
    public float angularSize;
    public float phase;
    public float apparentLuminosity;
    #endregion

    #region Private
    private Transform _playerShip;
    #endregion

    #region Unity Methods
    void OnEnable()
    {
        _playerShip = GameObject.FindGameObjectWithTag("PlayerShip")?.transform;
        StartCoroutine(UpdateObservables());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }
    #endregion

    #region Public API
    public (float Az, float El, float Dist) GetData() => (azimuth, elevation, distance);

    public void SetData(float az, float el, float dist)
    {
        azimuth   = az;
        elevation = el;
        distance  = dist;
    }

    /// <summary>
    /// Réinitialise toutes les données observées.
    /// À appeler lors de la réutilisation depuis le pool.
    /// </summary>
    public void Reset()
    {
        azimuth            = 0f;
        elevation          = 0f;
        distance           = 0f;
        angularSize        = 0f;
        phase              = 0f;
        apparentLuminosity = 0f;
    }
    #endregion

    #region Observation Update
    /// <summary>
    /// Met à jour les propriétés observées toutes les 100ms.
    /// La position du transform est mise à jour par OrbitalComponent (chaque frame),
    /// ici on en dérive ce qu'un capteur mesurerait.
    /// </summary>
    private IEnumerator UpdateObservables()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            if (_playerShip == null)
            {
                _playerShip = GameObject.FindGameObjectWithTag("PlayerShip")?.transform;
                continue;
            }

            (azimuth, elevation, distance) = ComputeSphericalCoordinates();
            angularSize = Mathf.Atan2(radius, distance) * Mathf.Rad2Deg;

            // Trouver l'étoile parente pour la phase et la luminosité
            // On remonte dans la hiérarchie : Planet → Star ou Barycenter → Star
            if (starLuminosity > 0f)
            {
                phase              = 1f;
                apparentLuminosity = ComputeStarLuminosity();
            }
            else
            {
                CelestialBody parentStar = FindParentStar();
                if (parentStar != null)
                {
                    phase              = ComputePhase(parentStar.transform);
                    apparentLuminosity = ComputePlanetLuminosity(parentStar);
                }
                else
                {
                    phase              = 0f;
                    apparentLuminosity = 0f;
                }
            }

        }
    }
    #endregion

    #region Coordinate Computation
    private (float az, float el, float dist) ComputeSphericalCoordinates()
    {
        Vector3 relative = transform.position - _playerShip.position;

        Vector3 shipForward = _playerShip.forward;
        shipForward.y = 0f;
        shipForward.Normalize();

        Vector3 relativeXZ = new Vector3(relative.x, 0f, relative.z);

        float dist = relative.magnitude;
        float az   = Vector3.SignedAngle(shipForward, relativeXZ, Vector3.up);
        float el   = Mathf.Atan2(relative.y, relativeXZ.magnitude) * Mathf.Rad2Deg;

        return (az, -el, dist);
    }
    #endregion

    #region Luminosity & Phase
    private float ComputeStarLuminosity()
    {
        float d = distance / UA_TO_GAME_UNITS;
        if (d <= 0f) return 0f;
        return starLuminosity / (d * d);
    }

    private float ComputePlanetLuminosity(CelestialBody star)
    {
        float dObserver = distance / UA_TO_GAME_UNITS;
        if (dObserver <= 0f) return 0f;

        float dStarToPlanet = Vector3.Distance(transform.position, star.transform.position)
                              / UA_TO_GAME_UNITS;
        if (dStarToPlanet <= 0f) return 0f;

        float fluxAtPlanet = star.starLuminosity / (dStarToPlanet * dStarToPlanet);
        float r = radius / UA_TO_GAME_UNITS;

        // Raw reflected flux — physically correct but ~8-9 orders of magnitude below
        // stellar flux. PLANET_LUMINOSITY_SENSOR_GAIN simulates the per-sector sensitivity
        // boost applied by real astronomical imagers to make planets visible alongside stars.
        return fluxAtPlanet * albedo * phase * (r * r) / (dObserver * dObserver)
               * GameConstants.PLANET_LUMINOSITY_SENSOR_GAIN;
    }

    /// <summary>
    /// Phase de Lambert : (sin α + (π − α) cos α) / π
    /// </summary>
    private float ComputePhase(Transform star)
    {
        Vector3 toStar     = (star.position     - transform.position).normalized;
        Vector3 toObserver = (_playerShip.position - transform.position).normalized;
        float alpha = Vector3.Angle(toStar, toObserver) * Mathf.Deg2Rad;
        return Mathf.Clamp01(
            (Mathf.Sin(alpha) + (Mathf.PI - alpha) * Mathf.Cos(alpha)) / Mathf.PI);
    }
    #endregion

    #region Hierarchy Helpers
    /// <summary>
    /// Remonte la hiérarchie pour trouver l'étoile parente.
    /// Fonctionne pour les cas simples (planète → étoile) et complexes
    /// (planète → barycentre → étoile binaire : on prend la plus proche).
    /// </summary>
    private CelestialBody FindParentStar()
    {
        // Cas simple : parent direct est une étoile
        if (transform.parent != null)
        {
            CelestialBody parent = transform.parent.GetComponent<CelestialBody>();
            if (parent != null && parent.starLuminosity > 0f)
                return parent;

            // Cas barycentre : chercher parmi les frères et sœurs
            Barycenter barycenter = transform.parent.GetComponent<Barycenter>();
            if (barycenter != null)
                return FindNearestStar(barycenter.bodies);
        }

        return null;
    }

    private CelestialBody FindNearestStar(List<CelestialBody> candidates)
    {
        CelestialBody nearest = null;
        float minDist = float.MaxValue;

        foreach (CelestialBody candidate in candidates)
        {
            if (candidate == null || candidate.starLuminosity <= 0f) continue;
            float d = Vector3.Distance(transform.position, candidate.transform.position);
            if (d < minDist)
            {
                minDist = d;
                nearest = candidate;
            }
        }

        return nearest;
    }
    #endregion
}