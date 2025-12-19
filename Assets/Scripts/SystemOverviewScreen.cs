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
        systemTable.text = "<align=left>Nom\t\t\t\tAz\t\tEl\t\tDist\n"
            +"Update to populate system Overview."
            +"</align>";
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
            if(!child.gameObject.activeInHierarchy) continue;
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

        string tableContent = "<align=left>Nom\t\t\t\tAz\t\tEl\t\tDist\n";
        foreach (GameObject body in celestialBodies)
        {
            float azimuth, elevation, distance;
            (azimuth, elevation, distance) = body.GetComponent<CelestialBody>().GetData();
            tableContent += $"{body.name}\t{azimuth:000.0}°\t{elevation:000.0}°\t{distance:00.0} u\n";
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

}
