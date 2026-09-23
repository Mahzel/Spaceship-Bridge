using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-right list of every track, searching or locked: select (click the name), bearing, fitted bearing rate,
/// quality, drop. A searching (not yet locked) track's name shows red, matching its tick color everywhere
/// else. New tracks are never created here — mark a bearing on the waterfall/DSP to seed one; this panel just
/// lets the player set the name the NEXT mark will use, and manage/rename/drop what already exists.
/// Reads Game.State.Tracks; refreshed by GameUI.
/// </summary>
public sealed class TrackPanel
{
    private const int MaxRows = 8;

    private sealed class Row
    {
        public GameObject go;
        public Button name, drop;
        public TextMeshProUGUI nameLabel, bearing, rate, quality, range;
        public int trackId;
    }

    private readonly List<Row> _rows = new List<Row>();
    private readonly List<Track> _all = new List<Track>();
    private TextMeshProUGUI _empty;
    private TMP_InputField _rename;
    private TMP_InputField _nextNameField;

    public void Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image panel = UIKit.AddPanel(parent, "TrackPanel", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
        prt.anchoredPosition = new Vector2(-8f, -(t.statusBarHeight + 8f));
        prt.sizeDelta = new Vector2(640f, 0f);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        UIKit.AddLabel(prt, Loc.Get("ui.tracks"), t.fontSizeBody, t.accent);
        _empty = UIKit.AddLabel(prt, Loc.Get("ui.tracks.none"), t.fontSizeSmall, t.textDim);

        RectTransform markRow = UIKit.Node("MarkRow", prt);
        var mh = UIKit.HStack(markRow, 6f, 0);
        mh.childAlignment = TextAnchor.MiddleLeft;
        UIKit.AddLabel(markRow, Loc.Get("ui.track.nextname"), t.fontSizeSmall, t.textDim);
        _nextNameField = UIKit.AddInputField(markRow, Loc.Get("ui.track.nextnamehint"), 16, OnNextNameEdit, 160f, 34f);
        UIKit.AddLabel(markRow, Loc.Get("ui.track.markhint"), t.fontSizeSmall, t.textDim);

        for (int i = 0; i < MaxRows; i++) _rows.Add(BuildRow(prt));

        RectTransform nameRow = UIKit.Node("NameRow", prt);
        var nh = UIKit.HStack(nameRow, 6f, 0);
        nh.childAlignment = TextAnchor.MiddleLeft;
        UIKit.AddLabel(nameRow, Loc.Get("ui.track.name"), t.fontSizeSmall, t.textDim);
        _rename = UIKit.AddInputField(nameRow, Loc.Get("ui.track.namehint"), 16, OnRename, 240f, 34f);
        DraggablePanel.Attach(prt, "tracks");
    }

    private Row BuildRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        var row = new Row();

        RectTransform rt = UIKit.Node("Row", parent);
        row.go = rt.gameObject;
        var h = UIKit.HStack(rt, 6f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;

        row.name = UIKit.AddButton(rt, "", () => Select(row), 64f, 34f);
        row.nameLabel = row.name.GetComponentInChildren<TextMeshProUGUI>();

        row.bearing = UIKit.AddLabel(rt, "", t.fontSizeSmall, t.text);
        UIKit.Size(row.bearing.rectTransform, preferredWidth: 90f);
        row.rate = UIKit.AddLabel(rt, "", t.fontSizeSmall, t.text);
        UIKit.Size(row.rate.rectTransform, preferredWidth: 120f);
        row.quality = UIKit.AddLabel(rt, "", t.fontSizeSmall, t.textDim);
        UIKit.Size(row.quality.rectTransform, preferredWidth: 50f);

        row.range = UIKit.AddLabel(rt, "", t.fontSizeSmall, t.text);
        UIKit.Size(row.range.rectTransform, preferredWidth: 150f);

        row.drop = UIKit.AddButton(rt, Loc.Get("ui.track.drop"), () => Drop(row), 34f, 34f);

        row.go.SetActive(false);
        return row;
    }

    private static void OnRename(string value)
    {
        if (Game.State == null) return;
        TrackManager tm = Game.State.Tracks;
        value = value == null ? "" : value.Trim();
        if (tm.SelectedId != 0 && value.Length > 0) tm.Rename(tm.SelectedId, value);
    }

    private static void OnNextNameEdit(string value)
    {
        if (Game.State != null) Game.State.Tracks.PendingName = value == null ? "" : value.Trim();
    }

    private static void Select(Row row)
    {
        if (Game.State != null) Game.State.Tracks.SelectedId = row.trackId;
    }

    private static void Drop(Row row)
    {
        if (Game.State != null) Game.State.Tracks.Drop(row.trackId);
    }

    public void Refresh()
    {
        if (Game.State == null || _empty == null) return;
        UITheme t = UITheme.Current;
        TrackManager tm = Game.State.Tracks;
        _all.Clear();
        _all.AddRange(tm.All);

        _empty.gameObject.SetActive(_all.Count == 0);

        if (!_nextNameField.isFocused)
        {
            string pending = tm.PendingName ?? "";
            if (_nextNameField.text != pending) _nextNameField.SetTextWithoutNotify(pending);
        }

        Track selected = null;
        for (int i = 0; i < _all.Count; i++)
            if (_all[i].id == tm.SelectedId) selected = _all[i];
        _rename.interactable = selected != null;
        if (!_rename.isFocused)
        {
            string n = selected != null ? selected.name : "";
            if (_rename.text != n) _rename.SetTextWithoutNotify(n);
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            Row r = _rows[i];
            if (i >= _all.Count)
            {
                if (r.go.activeSelf) r.go.SetActive(false);
                continue;
            }

            Track tr = _all[i];
            if (!r.go.activeSelf) r.go.SetActive(true);
            r.trackId = tr.id;

            bool locked = tr.status == TrackStatus.Confirmed;

            UIKit.SetText(r.nameLabel, tr.name);
            r.nameLabel.color = locked ? t.text : t.danger;
            UIKit.SetButtonActive(r.name, tr.id == tm.SelectedId);
            UIKit.SetText(r.bearing, Loc.Get("ui.track.bearing", tr.bearing));
            UIKit.SetText(r.rate, tr.hasRate ? Loc.Get("ui.track.rate", tr.rateDegPerDay) : Loc.Get("ui.track.norate"));
            UIKit.SetText(r.quality, locked ? Loc.Get("ui.track.quality", tr.quality * 100f) : Loc.Get("ui.track.searching"));

            RangeEstimate re = tr.range;
            if (locked && re.Observable)
            {
                float au = (float)(re.range / GameConstants.GAME_UNITS_PER_UA);
                float pct = (float)(100.0 * re.rangeSigma / re.range);
                UIKit.SetText(r.range, Loc.Get("ui.track.range", au, pct));
            }
            else UIKit.SetText(r.range, Loc.Get("ui.track.norange"));
        }
    }
}
