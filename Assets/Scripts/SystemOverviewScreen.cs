using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class SystemOverviewScreen : ComputerScreen
{
    public Transform spaceEnvironment; // Référence au GameObject "spaceEnvironment"
    public TMP_Text systemTable; // Zone de texte pour afficher le tableau

    protected override void Initialize()
    {
        base.Initialize();
        UpdateSystemTable();
    }

    // Met à jour le tableau des objets célestes
    public void UpdateSystemTable()
    {
        if (spaceEnvironment == null)
        {
            UpdateStatus("Erreur : spaceEnvironment non assigné !");
            return;
        }

        // Récupère tous les enfants de spaceEnvironment (sauf le playerShip)
        List<GameObject> celestialBodies = new List<GameObject>();
        Transform playerShipTransform = null;

        foreach (Transform child in spaceEnvironment)
        {
            if (child.name == "PlayerShip")
            {
                playerShipTransform = child;
            }
            else
            {
                celestialBodies.Add(child.gameObject);
                foreach (Transform grandChild in child)
                {
                    celestialBodies.Add(grandChild.gameObject);
                }
            }
        }

        if (playerShipTransform == null)
        {
            UpdateStatus("Erreur : playerShip non trouvé dans spaceEnvironment !");
            return;
        }

        if (celestialBodies.Count == 0)
        {
            systemTable.text = "Aucun objet céleste détecté.";
            UpdateStatus("Aucun objet détecté.");
            return;
        }

        string tableContent = "<align=left>Nom\t\tAz\tEl\tDist\n";
        foreach (GameObject body in celestialBodies)
        {
            float azimuth, elevation, distance;
            (azimuth, elevation, distance) = CalculatePositionData(body.transform.position, playerShipTransform.position);
            body.GetComponent<CelestialBody>().EnterData(azimuth, elevation, distance);
            tableContent += $"{body.name}\t{azimuth:F1}°\t{elevation:F1}°\t{distance:F1} u\n";
        }
        systemTable.text = tableContent + "</align>";
        UpdateStatus("Objets détectés : " + celestialBodies.Count);
    }

    // Méthode appelée lors d'un clic sur un bouton
    public override void OnButtonClick(int buttonIndex)
    {
        switch (buttonIndex)
        {
            case 1: // Bouton "Scan"
                UpdateSystemTable();
                break;
            // Ajoute d'autres cas si nécessaire
        }
    }

    private (float azimuth, float elevation, float distance) CalculatePositionData(Vector3 targetPosition, Vector3 playerPosition)
{
    Vector3 relativePosition = targetPosition - playerPosition;
    float distance = relativePosition.magnitude;
    float azimuth = Mathf.Atan2(relativePosition.x, relativePosition.z) * Mathf.Rad2Deg;
    if(azimuth<0) azimuth += 360f;
    float elevation = Mathf.Atan2(relativePosition.y, new Vector2(relativePosition.x, relativePosition.z).magnitude) * Mathf.Rad2Deg;
    return (azimuth, elevation, distance);
}
}
