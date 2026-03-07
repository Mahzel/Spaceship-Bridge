using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Point de masse virtuel autour duquel orbitent plusieurs corps.
/// Sa position est recalculée chaque frame comme le centre de masse pondéré
/// de tous ses corps enfants.
///
/// Peut lui-même avoir un OrbitalComponent pour orbiter autour d'un autre
/// barycentre — ce qui permet des systèmes hiérarchiques arbitraires :
///
///   Barycenter_ABC  (immobile, centre du système)
///   ├── Barycenter_AB   (OrbitalComponent → focus: Barycenter_ABC)
///   │   ├── Star_A      (OrbitalComponent → focus: Barycenter_AB)
///   │   └── Star_B      (OrbitalComponent → focus: Barycenter_AB)
///   └── Star_C          (OrbitalComponent → focus: Barycenter_ABC)
///
/// Les planètes peuvent orbiter autour de n'importe quel barycentre ou étoile.
/// </summary>
[RequireComponent(typeof(OrbitalComponent))] // facultatif — retirez si c'est la racine
public class Barycenter : MonoBehaviour
{
    [Header("Corps en orbite autour de ce barycentre")]
    public List<CelestialBody> bodies = new List<CelestialBody>();

    // Masse totale — exposée pour que le générateur puisse calculer les orbites
    public float TotalMass { get; private set; }

    void Update()
    {
        UpdatePosition();
    }

    /// <summary>
    /// Recalcule la position du barycentre comme centre de masse des corps enfants.
    /// Appelé automatiquement dans Update, mais peut aussi être appelé manuellement
    /// par le générateur lors de l'initialisation.
    /// </summary>
    public void UpdatePosition()
    {
        if (bodies == null || bodies.Count == 0) return;

        Vector3 weightedSum = Vector3.zero;
        TotalMass = 0f;

        foreach (CelestialBody body in bodies)
        {
            if (body == null) continue;
            weightedSum += body.transform.position * body.mass;
            TotalMass   += body.mass;
        }

        if (TotalMass > 0f)
            transform.position = weightedSum / TotalMass;
    }

    /// <summary>
    /// Calcule le demi-grand axe de chaque corps autour du barycentre commun,
    /// depuis la séparation totale et les masses.
    /// a_A = separation * m_B / (m_A + m_B)
    /// a_B = separation * m_A / (m_A + m_B)
    /// </summary>
    public static (float semiMajorA, float semiMajorB) ComputeBinarySemiMajorAxes(
        float separation, float massA, float massB)
    {
        float total = massA + massB;
        return (separation * massB / total,
                separation * massA / total);
    }
}