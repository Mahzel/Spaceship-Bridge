using UnityEngine;
using System.Collections;

public class CelestialBody : MonoBehaviour
{
    public float azimuth;   // Azimut (en degrés)
    public float elevation; // Élévation (en degrés)
    public float distance;  // Distance (en unités)

    public void Start()
    {
        UpdateData();
        StartCoroutine(UpdateData());
    }

    // Constructeur implicite pour initialiser les valeurs
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

    private (float a, float e, float d) CalculatePositionData()
    {
        Vector3 relativePosition = gameObject.transform.position - GameObject.FindGameObjectsWithTag("PlayerShip")[0].transform.position;
        float d = relativePosition.magnitude;
        float a = Mathf.Atan2(relativePosition.x, relativePosition.z) * Mathf.Rad2Deg;
        if(a<0) a += 360f;
        float e = Mathf.Atan2(relativePosition.y, new Vector2(relativePosition.x, relativePosition.z).magnitude) * Mathf.Rad2Deg;
        return (a, e, d);
    }

    IEnumerator UpdateData()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);
            (float a,float e,float d) = CalculatePositionData();
            SetData(a,e,d);
        }
    }
}
