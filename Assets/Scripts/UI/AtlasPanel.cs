using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The persistent survey log (Game.State.Atlas): every record that has ever made it home, across every run
/// this session. Its own standalone draggable panel, not a SystemsDock tab — it's a cross-run log, not a
/// per-flight control. Newest entries first, capped to MaxRows like TrackPanel/DataPanel (no scroll widget
/// exists yet in this UI kit).
///
/// Entries never show their ground-truth isFalse flag — that would give away which ones are fake. A caught
/// entry (AtlasEntry.caught, set by a debrief review not wired up yet) is the only thing that visibly differs.
/// </summary>
public sealed class AtlasPanel
{
    private const int MaxRows = 12;

    private sealed class Row
    {
        public GameObject go;
        public TextMeshProUGUI system, name, kind, value, status;
    }

    private readonly List<Row> _rows = new List<Row>();
    private readonly List<AtlasEntry> _sorted = new List<AtlasEntry>();
    private TextMeshProUGUI _title, _empty;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image panel = UIKit.AddPanel(parent, "AtlasPanel", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 1f);
        prt.anchoredPosition = new Vector2(8f, -(t.statusBarHeight + 8f));
        prt.sizeDelta = new Vector2(640f, 0f);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        _title = UIKit.AddLabel(prt, Loc.Get("ui.atlas"), t.fontSizeBody, t.accent);
        _empty = UIKit.AddLabel(prt, Loc.Get("ui.atlas.none"), t.fontSizeSmall, t.textDim);

        RectTransform header = UIKit.Node("Header", prt);
        var hh = UIKit.HStack(header, 6f, 0);
        hh.childAlignment = TextAnchor.MiddleLeft;
        HeaderCell(header, Loc.Get("ui.atlas.col.system"), 90f);
        HeaderCell(header, Loc.Get("ui.atlas.col.name"), 150f);
        HeaderCell(header, Loc.Get("ui.atlas.col.kind"), 70f);
        HeaderCell(header, Loc.Get("ui.atlas.col.value"), 70f);
        HeaderCell(header, Loc.Get("ui.atlas.col.status"), 110f);

        for (int i = 0; i < MaxRows; i++) _rows.Add(BuildRow(prt));

        DraggablePanel.Attach(prt, "atlas");
        return prt.gameObject;
    }

    private static void HeaderCell(Transform parent, string text, float width)
    {
        UITheme t = UITheme.Current;
        TextMeshProUGUI l = UIKit.AddLabel(parent, text, t.fontSizeSmall, t.textDim);
        UIKit.Size(l.rectTransform, preferredWidth: width);
    }

    private Row BuildRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        var row = new Row();

        RectTransform rt = UIKit.Node("Row", parent);
        row.go = rt.gameObject;
        var h = UIKit.HStack(rt, 6f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;

        row.system = Cell(rt, 90f, t.text);
        row.name   = Cell(rt, 150f, t.text);
        row.kind   = Cell(rt, 70f, t.textDim);
        row.value  = Cell(rt, 70f, t.text);
        row.status = Cell(rt, 110f, t.textDim);

        row.go.SetActive(false);
        return row;
    }

    private static TextMeshProUGUI Cell(Transform parent, float width, Color color)
    {
        TextMeshProUGUI l = UIKit.AddLabel(parent, "", UITheme.Current.fontSizeSmall, color);
        UIKit.Size(l.rectTransform, preferredWidth: width);
        return l;
    }

    public void Refresh()
    {
        if (Game.State == null || _empty == null) return;
        UITheme t = UITheme.Current;
        IList<AtlasEntry> all = Game.State.Atlas.Entries;

        UIKit.SetText(_title, Loc.Get("ui.atlas.count", all.Count));
        _empty.gameObject.SetActive(all.Count == 0);

        // Newest first.
        _sorted.Clear();
        _sorted.AddRange(all);
        _sorted.Sort((a, b) => b.id.CompareTo(a.id));

        for (int i = 0; i < _rows.Count; i++)
        {
            Row r = _rows[i];
            if (i >= _sorted.Count) { if (r.go.activeSelf) r.go.SetActive(false); continue; }

            AtlasEntry e = _sorted[i];
            if (!r.go.activeSelf) r.go.SetActive(true);

            UIKit.SetText(r.system, e.systemId);
            UIKit.SetText(r.name, e.label);
            UIKit.SetText(r.kind, e.kind == DataKind.Stub ? Loc.Get("ui.data.kind.stub") : Loc.Get("ui.data.kind.raw"));
            UIKit.SetText(r.value, Loc.Get("ui.data.value", e.value));

            if (e.caught)
            {
                UIKit.SetText(r.status, Loc.Get("ui.atlas.wrong"));
                r.status.color = t.danger;
            }
            else
            {
                UIKit.SetText(r.status, Loc.Get("ui.atlas.run", e.runNumber));
                r.status.color = t.textDim;
            }
        }
    }
}
