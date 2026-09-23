using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.Object; // plain C# class (not a MonoBehaviour): FindObjectsByType isn't inherited here

/// <summary>
/// System overview: a row-list table of every detected celestial body (name / az / el / distance), replacing
/// the old ComputerScreen-driven table that read a scene-wired "spaceEnvironment" transform. Bodies are found
/// the same way every other screen finds them (FindObjectsByType&lt;CelestialBody&gt;), so no Inspector wiring
/// is needed. Rows are clickable: selecting one sets GameState.TargetBodyName (via Game.State.SetTarget), used
/// by the NODE tab's "plot a transfer to X" (ManeuverPlan.SolveHohmann matches it back to a NodeData by name,
/// which is exactly what CelestialBody.bodyName was assigned from - see SystemManager.Spawn). Otherwise purely
/// informational — unlike the imager/spectrometer, there's nothing to arm or power down, so Hide() is a no-op;
/// the table stays cheap to refresh even while off-screen.
/// </summary>
public sealed class SystemScreen
{
    private const int MaxRows = 16;
    private const float RefreshInterval = 0.5f;

    private sealed class Row
    {
        public GameObject go;
        public Button button;
        public TextMeshProUGUI name, cls, az, el, dist;
    }

    private readonly List<Row> _rows = new List<Row>();
    private readonly List<CelestialBody> _bodies = new List<CelestialBody>();
    private TextMeshProUGUI _empty;
    private float _accum;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform root = UIKit.Node("System", parent);
        UIKit.Size(root, flexibleWidth: 1f);
        var v = UIKit.VStack(root, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        BuildHeader(root);
        _empty = UIKit.AddLabel(root, Loc.Get("ui.system.none"), t.fontSizeSmall, t.textDim);
        for (int i = 0; i < MaxRows; i++) _rows.Add(BuildRow(root));

        RefreshBodies();
        Populate();

        return root.gameObject;
    }

    private void BuildHeader(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform row = UIKit.Node("Header", parent);
        UIKit.HStack(row, 6f, 0);

        TextMeshProUGUI name = UIKit.AddLabel(row, Loc.Get("ui.system.col.name"), t.fontSizeSmall, t.textDim);
        UIKit.Size(name.rectTransform, preferredWidth: 200f);
        TextMeshProUGUI cls = UIKit.AddLabel(row, Loc.Get("ui.system.col.class"), t.fontSizeSmall, t.textDim);
        UIKit.Size(cls.rectTransform, preferredWidth: 110f);
        TextMeshProUGUI az = UIKit.AddLabel(row, Loc.Get("ui.system.col.az"), t.fontSizeSmall, t.textDim);
        UIKit.Size(az.rectTransform, preferredWidth: 80f);
        TextMeshProUGUI el = UIKit.AddLabel(row, Loc.Get("ui.system.col.el"), t.fontSizeSmall, t.textDim);
        UIKit.Size(el.rectTransform, preferredWidth: 80f);
        TextMeshProUGUI dist = UIKit.AddLabel(row, Loc.Get("ui.system.col.dist"), t.fontSizeSmall, t.textDim);
        UIKit.Size(dist.rectTransform, preferredWidth: 100f);
    }

    private Row BuildRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        var row = new Row();

        RectTransform rt = UIKit.Node("Row", parent);
        row.go = rt.gameObject;
        UIKit.HStack(rt, 6f, 0);

        // Invisible raycastable background makes the whole row clickable (target selection), not just the text.
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f);
        bg.raycastTarget = true;
        row.button = rt.gameObject.AddComponent<Button>();

        row.name = UIKit.AddLabel(rt, "", t.fontSizeSmall, t.text);
        UIKit.Size(row.name.rectTransform, preferredWidth: 200f);
        row.cls = UIKit.AddLabel(rt, "", t.fontSizeSmall, t.textDim);
        UIKit.Size(row.cls.rectTransform, preferredWidth: 110f);
        row.az = UIKit.AddLabel(rt, "", t.fontSizeSmall, t.text);
        UIKit.Size(row.az.rectTransform, preferredWidth: 80f);
        row.el = UIKit.AddLabel(rt, "", t.fontSizeSmall, t.text);
        UIKit.Size(row.el.rectTransform, preferredWidth: 80f);
        row.dist = UIKit.AddLabel(rt, "", t.fontSizeSmall, t.text);
        UIKit.Size(row.dist.rectTransform, preferredWidth: 100f);

        row.go.SetActive(false);
        return row;
    }

    private void RefreshBodies()
    {
        CelestialBody[] found = FindObjectsByType<CelestialBody>(FindObjectsSortMode.None);
        _bodies.Clear();
        foreach (CelestialBody b in found)
            if (!string.IsNullOrEmpty(b.bodyName)) _bodies.Add(b);
        _bodies.Sort((a, b) => string.CompareOrdinal(a.bodyName, b.bodyName));
    }

    private void Populate()
    {
        _empty.gameObject.SetActive(_bodies.Count == 0);

        for (int i = 0; i < _rows.Count; i++)
        {
            Row r = _rows[i];
            if (i >= _bodies.Count)
            {
                if (r.go.activeSelf) r.go.SetActive(false);
                continue;
            }

            CelestialBody b = _bodies[i];
            if (!r.go.activeSelf) r.go.SetActive(true);

            UIKit.SetText(r.name, b.bodyName);
            UIKit.SetText(r.cls, !string.IsNullOrEmpty(b.surfaceClass) ? b.surfaceClass : b.bodyType);
            UIKit.SetText(r.az, Mathf.Repeat(b.azimuth, 360f).ToString("000.0") + "°");
            UIKit.SetText(r.el, b.elevation.ToString("+000.0;-000.0") + "°");
            UIKit.SetText(r.dist, b.distance.ToString("0.0") + " u");

            string name = b.bodyName;
            r.button.onClick.RemoveAllListeners();
            r.button.onClick.AddListener(() => Game.State?.SetTarget(name));

            bool selected = Game.State != null && Game.State.TargetBodyName == name;
            r.name.color = selected ? UITheme.Current.accent : UITheme.Current.text;
        }
    }

    /// <summary>Called by SensorConsole when another mode is selected. No-op: purely informational, nothing to stop.</summary>
    public void Hide() { }

    /// <summary>Called every frame by SensorConsole, regardless of whether this mode is the one showing.</summary>
    public void Refresh(float unscaledDeltaSeconds)
    {
        _accum += unscaledDeltaSeconds;
        if (_accum < RefreshInterval) return;
        _accum = 0f;
        RefreshBodies();
        Populate();
    }
}
