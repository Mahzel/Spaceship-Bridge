using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Virtual mass point that bodies orbit. Its position is driven by SystemManager from SystemData
/// (fixed for the root, or on its own Kepler orbit for a nested pair), so it is no longer recomputed
/// from the bodies every frame. Because the two members of a pair are exactly opposite with
/// sma proportional to the other's mass, their centre of mass is the barycenter at all times.
///
///   Barycenter_ABC  (fixed, system origin)
///   ├── Barycenter_AB   (orbits Barycenter_ABC)
///   │   ├── Star_A
///   │   └── Star_B
///   └── Star_C
/// </summary>
public class Barycenter : MonoBehaviour
{
    [Header("Étoiles gravitant autour de ce barycentre")]
    public List<CelestialBody> bodies = new();

    public float TotalMass { get; private set; }

    public void RecalculateMass()
    {
        TotalMass = 0f;
        if (bodies == null) return;
        foreach (CelestialBody body in bodies)
            if (body != null) TotalMass += body.mass;
    }

    /// <summary>
    /// a_A = separation * m_B / (m_A + m_B), a_B = separation * m_A / (m_A + m_B)
    /// </summary>
    public static (float semiMajorA, float semiMajorB) ComputeBinarySemiMajorAxes(
        float separation, float massA, float massB)
    {
        float total = massA + massB;
        return (separation * massB / total,
                separation * massA / total);
    }
}
