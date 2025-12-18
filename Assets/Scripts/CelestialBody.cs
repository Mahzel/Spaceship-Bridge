using UnityEngine;

public class CelestialBody : MonoBehaviour
{
    public float azimuth;   // Azimut (en degrés)
    public float elevation; // Élévation (en degrés)
    public float distance;  // Distance (en unités)

    // Constructeur implicite pour initialiser les valeurs
    public void EnterData(float az, float el, float dist)
    {
        azimuth = az;
        elevation = el;
        distance = dist;
    }
}
