using UnityEngine;

/// <summary>
/// DSP curve display. Now a single LineGraphic mesh instead of one GameObject per segment.
/// If no LineGraphic is assigned, one is created under graphContainer at runtime.
/// </summary>
public class DSPGraph : MonoBehaviour
{
    public RectTransform graphContainer; // Conteneur du graphique DSP
    public LineGraphic   line;           // Optionnel : créé automatiquement si absent

    [Header("Legacy (plus utilisé — peut être retiré)")]
    public GameObject lineSegmentPrefab;

    public int   dataPoints = 36;
    public float maxValue   = 100f;

    private float[] _normalized = new float[0];

    private void Awake() => EnsureLine();

    private void EnsureLine()
    {
        if (line != null || graphContainer == null) return;

        var go = new GameObject("DSPLine", typeof(RectTransform), typeof(LineGraphic));
        go.transform.SetParent(graphContainer, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        line = go.GetComponent<LineGraphic>();
        line.raycastTarget = false;
        line.color = new Color(0.5f, 1f, 0.5f, 1f);
    }

    // Dessine le graphique DSP avec les données fournies
    public void DrawDSPGraph(float[] dspData, int pixelSize)
    {
        EnsureLine();
        if (line == null) return;

        float graphWidth = graphContainer.rect.width;

        // dspData.Length reste la source de vérité, plafonnée à ce que le conteneur peut afficher.
        int maxDisplayPoints = Mathf.Max(1, (int)graphWidth / Mathf.Max(1, pixelSize));
        dataPoints = Mathf.Min(dspData.Length, maxDisplayPoints);

        if (dataPoints < 2) { line.Clear(); return; }

        // Normalisation dynamique : max réel plutôt qu'une constante
        float maxVal = 0f;
        for (int i = 0; i < dataPoints; i++)
            maxVal = Mathf.Max(maxVal, Mathf.Abs(dspData[i]));
        if (maxVal < 1e-9f) maxVal = 1f; // garde : données nulles

        if (_normalized.Length < dataPoints) _normalized = new float[dataPoints];
        for (int i = 0; i < dataPoints; i++)
            _normalized[i] = dspData[i] / maxVal;

        line.SetValues(_normalized, dataPoints);
    }
}
