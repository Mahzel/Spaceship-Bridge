using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class DSPGraph : MonoBehaviour
{
    public RectTransform graphContainer; // Conteneur du graphique DSP
    public GameObject lineSegmentPrefab; // Prefab pour un segment de ligne
    public int dataPoints = 36;          // Nombre de points de données (0° à 360°)
    public float maxValue = 100f;        // Valeur maximale pour la normalisation

    private List<GameObject> lineSegments = new List<GameObject>();

    // Dessine le graphique DSP avec les données fournies
    public void DrawDSPGraph(float[] dspData, int pixelSize)
    {
        ClearGraph();

        float graphHeight = graphContainer.rect.height;
        float graphWidth  = graphContainer.rect.width;

        // Utiliser la taille réelle de dspData comme source de vérité,
        // en la plafonnant au nombre de points que le conteneur peut afficher.
        int maxDisplayPoints = Mathf.Max(1, (int)(graphWidth) / pixelSize);
        dataPoints = Mathf.Min(dspData.Length, maxDisplayPoints);

        if (dataPoints < 2) return;

        Vector2[] points = new Vector2[dataPoints];

        // Normalisation dynamique : on utilise le max réel plutôt qu'une constante
        float maxVal = 0f;
        for (int i = 0; i < dataPoints; i++)
            maxVal = Mathf.Max(maxVal, Mathf.Abs(dspData[i]));
        if (maxVal < 1e-9f) maxVal = 1f; // guard : données nulles

        for (int i = 0; i < dataPoints; i++)
        {
            float x = (float)i / (dataPoints - 1) * graphWidth;
            float y = (dspData[i] / maxVal) * graphHeight;
            points[i] = new Vector2(x, y);
        }

        for (int i = 0; i < points.Length - 1; i++)
        {
            GameObject segment = Instantiate(lineSegmentPrefab, graphContainer);
            lineSegments.Add(segment);

            RectTransform segmentRect = segment.GetComponent<RectTransform>();
            segmentRect.anchorMin = Vector2.zero;
            segmentRect.anchorMax = Vector2.zero;
            segmentRect.pivot     = Vector2.zero;

            segmentRect.localPosition = points[i];
            Vector2 direction = points[i + 1] - points[i];
            segmentRect.sizeDelta = new Vector2(direction.magnitude, 1f);
            segmentRect.rotation  = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }
    }

    // Efface le graphique actuel
    private void ClearGraph()
    {
        foreach (GameObject segment in lineSegments)
        {
            Destroy(segment);
        }
        lineSegments.Clear();
    }
}