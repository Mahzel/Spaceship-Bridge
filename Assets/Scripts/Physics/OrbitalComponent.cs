using UnityEngine;

/// <summary>
/// Read-only mirror of a body's orbit, kept on the GameObject so it can be inspected in the editor.
/// It no longer moves anything: SystemManager evaluates every position each frame from SystemData
/// and the GameClock, parents first, so there is no coroutine ordering or one-frame lag.
/// </summary>
public class OrbitalComponent : MonoBehaviour
{
    [Header("Éléments orbitaux (copie de SystemData — lecture seule)")]
    public OrbitElements elements;

    /// <summary>Offset from the parent at the given simulated time (also works for the future).</summary>
    public Vector3 OffsetAt(double simSeconds) => KeplerOrbit.OffsetAt(elements, simSeconds);

    public double PeriodYears => elements.orbitalPeriod / GameConstants.SECONDS_PER_YEAR;
}
