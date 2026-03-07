using UnityEngine;
using System.Collections;

/// <summary>
/// Gère uniquement la mécanique orbitale d'un corps.
/// Ne connaît pas l'observateur — c'est CelestialBody qui s'en charge.
/// Le focus peut être une étoile, un barycentre, une planète (pour les lunes).
/// </summary>
public class OrbitalComponent : MonoBehaviour
{
    #region Keplerian Elements
    [Header("Éléments orbitaux kepleriens")]
    public float semiMajorAxis       = 10f;   // a  — demi-grand axe (unités de jeu)
    public float eccentricity        = 0f;    // e  — excentricité [0, 1[
    public float inclination         = 0f;    // i  — inclinaison (degrés)
    public float longitudeAscNode    = 0f;    // Ω  — longitude du nœud ascendant (degrés)
    public float argumentPeriapsis   = 0f;    // ω  — argument du périastre (degrés)
    public float meanAnomalyAtEpoch  = 0f;    // M₀ — anomalie moyenne initiale (degrés)
    public float orbitalPeriod       = 10f;   // T  — période orbitale (secondes de jeu)
    #endregion

    #region References
    [Header("Référence")]
    public Transform focus; // Barycentre ou corps central
    #endregion

    #region Private
    private float _meanAnomaly; // Anomalie moyenne courante (degrés)
    #endregion

    #region Unity Methods
    void OnEnable()
    {
        _meanAnomaly = meanAnomalyAtEpoch;
        StartCoroutine(OrbitRoutine());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }
    #endregion

    #region Public API
    public void Reset()
    {
        _meanAnomaly = meanAnomalyAtEpoch;
    }
    #endregion

    #region Orbit Coroutine
    private IEnumerator OrbitRoutine()
    {
        while (true)
        {
            if (focus != null)
            {
                _meanAnomaly += (360f / orbitalPeriod) * Time.deltaTime / 8760f;
                if (_meanAnomaly >= 360f) _meanAnomaly -= 360f;

                transform.position = focus.position + CalculateOrbitalPosition(_meanAnomaly);
            }
            yield return null;
        }
    }
    #endregion

    #region Orbital Mechanics
    /// <summary>
    /// Calcule la position 3D dans le référentiel du focus à partir de l'anomalie moyenne.
    /// Applique les 6 éléments kepleriens complets.
    /// </summary>
    public Vector3 CalculateOrbitalPosition(float meanAnomalyDeg)
    {
        float E = SolveEccentricAnomaly(meanAnomalyDeg, eccentricity);
        float nu = TrueAnomaly(E, eccentricity);          // anomalie vraie (degrés)
        float nuRad = nu * Mathf.Deg2Rad;

        // Distance radiale dans le plan orbital
        float r = semiMajorAxis * (1f - eccentricity * Mathf.Cos(E));

        // Position dans le plan orbital (repère périfocal)
        float xOrb = r * Mathf.Cos(nuRad);
        float yOrb = r * Mathf.Sin(nuRad);

        // Rotation vers le référentiel 3D via les 3 angles kepleriens
        // Ω (nœud ascendant), i (inclinaison), ω (argument du périastre)
        float cosO = Mathf.Cos(longitudeAscNode   * Mathf.Deg2Rad);
        float sinO = Mathf.Sin(longitudeAscNode   * Mathf.Deg2Rad);
        float cosI = Mathf.Cos(inclination        * Mathf.Deg2Rad);
        float sinI = Mathf.Sin(inclination        * Mathf.Deg2Rad);
        float cosW = Mathf.Cos(argumentPeriapsis  * Mathf.Deg2Rad);
        float sinW = Mathf.Sin(argumentPeriapsis  * Mathf.Deg2Rad);

        // Matrice de rotation périfocale → équatorial (ligne par ligne)
        float x = (cosO * cosW - sinO * sinW * cosI) * xOrb
                + (-cosO * sinW - sinO * cosW * cosI) * yOrb;

        float y = (sinI * sinW) * xOrb
                + (sinI * cosW) * yOrb;

        float z = (sinO * cosW + cosO * sinW * cosI) * xOrb
                + (-sinO * sinW + cosO * cosW * cosI) * yOrb;

        return new Vector3(x, y, z);
    }

    // Newton-Raphson — tout en radians
    private float SolveEccentricAnomaly(float meanAnomalyDeg, float e)
    {
        float M = meanAnomalyDeg * Mathf.Deg2Rad;
        float E = M;
        for (int i = 0; i < 100; i++)
        {
            float delta = E - e * Mathf.Sin(E) - M;
            if (Mathf.Abs(delta) < 1e-6f) break;
            E -= delta / (1f - e * Mathf.Cos(E));
        }
        return E; // radians
    }

    // Anomalie vraie depuis l'anomalie excentrique (reçoit radians, retourne degrés)
    private float TrueAnomaly(float E, float e)
    {
        return 2f * Mathf.Rad2Deg * Mathf.Atan2(
            Mathf.Sqrt(1f + e) * Mathf.Sin(E / 2f),
            Mathf.Sqrt(1f - e) * Mathf.Cos(E / 2f));
    }
    #endregion
}