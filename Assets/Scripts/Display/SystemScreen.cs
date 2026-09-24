using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// System overview built from TRACKS, not from the scene: one row per contact in Game.State.Tracks, showing only
/// what the sensors have measured. It never looks at CelestialBody. The omniscient table that used to live here
/// belongs in the dev overlay.
///
/// A row fills in as the contact is characterised (TrackManager.LevelOf):
///   Bearing    - just marked: bearing only, status SEARCH until the tracker locks it;
///   Rate       - locked, with a fitted bearing rate;
///   Ranged     - a usable range from TMA (after a manoeuvre) or radar, with its 1-sigma;
///   Identified - spectrometer dwell complete: class, and composition / atmosphere or stellar data.
/// Clicking a row selects that track everywhere (Track panel, radar TRACK mode, SEL on the imager and
/// spectrometer, the NODE tab and the NAV map's TRANSFER section). A track becomes a usable transfer target
/// as soon as it has a range (OrbitFit.TryFit off TrackManager.BestRange) - identification is NOT required:
/// the planner only needs orbital parameters, and a bad fit just means a bad burn. Purely informational
/// otherwise, so Hide() is a no-op.
/// </summary>
public sealed class SystemScreen
{
    private const int MaxRows = 16;
    private const float RefreshInterval = 0.5f;

    private sealed class Row
    {
        public GameObject go;
        public Button button;
        public TextMeshProUGUI name, status, brg, range, cls, detail;
        public int trackId;
    }

    private readonly List<Row> _rows = new List<Row>();
    private TextMeshProUGUI _empty, _details;
    private float _accum = RefreshInterval;
    private readonly StringBuilder _sb = new StringBuilder();

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform root = UIKit.Node("System", parent);
        UIKit.Size(root, flexibleWidth: 1f);
        var v = UIKit.VStack(root, t.spacing * 0.5f, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        BuildHeader(root);
        _empty = UIKit.AddLabel(root, Loc.Get("ui.system.none"), t.fontSizeSmall, t.textDim);
        for (int i = 0; i < MaxRows; i++) _rows.Add(BuildRow(root));

        UIKit.AddSpacer(root, 6f);
        _details = UIKit.AddLabel(root, "", t.fontSizeSmall, t.text);
        _details.textWrappingMode = TextWrappingModes.Normal;
        _details.richText = true;

        return root.gameObject;
    }

    // Column widths: name, status, bearing, range, class, detail.
    private static readonly float[] Widths = { 110f, 80f, 80f, 170f, 130f, 280f };

    private void BuildHeader(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform row = UIKit.Node("Header", parent);
        UIKit.HStack(row, 6f, 0);
        string[] keys = { "ui.system.col.name", "ui.system.col.status", "ui.system.col.brg",
                          "ui.system.col.range", "ui.system.col.class", "ui.system.col.detail" };
        for (int i = 0; i < keys.Length; i++)
        {
            TextMeshProUGUI l = UIKit.AddLabel(row, Loc.Get(keys[i]), t.fontSizeSmall, t.textDim);
            UIKit.Size(l.rectTransform, preferredWidth: Widths[i]);
        }
    }

    private Row BuildRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        var row = new Row();

        RectTransform rt = UIKit.Node("Row", parent);
        row.go = rt.gameObject;
        UIKit.HStack(rt, 6f, 0);

        // Invisible raycastable background makes the whole row clickable, not just the text.
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f);
        bg.raycastTarget = true;
        row.button = rt.gameObject.AddComponent<Button>();
        row.button.onClick.AddListener(() => Select(row.trackId));

        row.name   = Cell(rt, 0, t.text);
        row.status = Cell(rt, 1, t.textDim);
        row.brg    = Cell(rt, 2, t.text);
        row.range  = Cell(rt, 3, t.text);
        row.cls    = Cell(rt, 4, t.text);
        row.detail = Cell(rt, 5, t.textDim);

        row.go.SetActive(false);
        return row;
    }

    private static TextMeshProUGUI Cell(Transform row, int col, Color color)
    {
        TextMeshProUGUI l = UIKit.AddLabel(row, "", UITheme.Current.fontSizeSmall, color);
        UIKit.Size(l.rectTransform, preferredWidth: Widths[col]);
        l.overflowMode = TextOverflowModes.Ellipsis;
        return l;
    }

    private static void Select(int trackId)
    {
        if (Game.State == null || trackId == 0) return;
        TrackManager tm = Game.State.Tracks;
        tm.SelectedId = trackId;
        Track tr = tm.Find(trackId);
        // TargetBodyName is display-only now (NodePanel/NavScreen re-derive the actual orbit from the
        // selected track via OrbitFit) - the track's own name, not a catalog match, so it's set as soon as
        // there's a track to select at all, no identification required.
        Game.State.SetTarget(tr != null ? tr.name : null);
    }

    /// <summary>Called by SensorConsole when another mode is selected. No-op: purely informational.</summary>
    public void Hide() { }

    /// <summary>Called every frame by SensorConsole, regardless of whether this mode is the one showing.</summary>
    public void Refresh(float unscaledDeltaSeconds)
    {
        _accum += unscaledDeltaSeconds;
        if (_accum < RefreshInterval) return;
        _accum = 0f;
        Populate();
    }

    private void Populate()
    {
        if (Game.State == null) return;
        UITheme t = UITheme.Current;
        TrackManager tm = Game.State.Tracks;
        IList<Track> all = tm.All;

        _empty.gameObject.SetActive(all.Count == 0);

        Track selected = null;
        for (int i = 0; i < _rows.Count; i++)
        {
            Row r = _rows[i];
            if (i >= all.Count)
            {
                if (r.go.activeSelf) r.go.SetActive(false);
                continue;
            }

            Track tr = all[i];
            if (!r.go.activeSelf) r.go.SetActive(true);
            r.trackId = tr.id;
            bool isSel = tr.id == tm.SelectedId;
            if (isSel) selected = tr;

            TrackLevel level = TrackManager.LevelOf(tr);
            TrackInfo info = tr.info;

            UIKit.SetText(r.name, tr.name);
            r.name.color = isSel ? t.accent : (tr.Locked ? t.text : t.danger);

            UIKit.SetText(r.status, tr.Locked ? Loc.Get("ui.system.lock") : Loc.Get("ui.system.search"));
            r.status.color = tr.Locked ? t.textDim : t.danger;

            UIKit.SetText(r.brg, Loc.Get("ui.system.brg", tr.bearing));
            UIKit.SetText(r.range, RangeText(tr));

            if (level == TrackLevel.Identified)
            {
                string cls = !string.IsNullOrEmpty(info.surfaceClass) ? info.surfaceClass : info.bodyType;
                UIKit.SetText(r.cls, info.blended ? cls + "?" : cls);
                r.cls.color = info.blended ? t.warning : t.text;
                UIKit.SetText(r.detail, ShortDetail(info));
            }
            else
            {
                UIKit.SetText(r.cls, info.specDwellSeconds > 0f ? Loc.Get("ui.system.analyzing") : Loc.Get("ui.system.unknown"));
                r.cls.color = t.textDim;
                UIKit.SetText(r.detail, "");
            }
        }

        UIKit.SetText(_details, selected != null ? LongDetail(selected) : Loc.Get("ui.system.selecthint"));
    }

    private static string RangeText(Track tr)
    {
        RangeEstimate re = tr.range;
        bool usable = re.valid && (re.Observable || tr.radarFix.valid);
        if (!usable) return Loc.Get("ui.system.norange");
        double au = re.range / GameConstants.GAME_UNITS_PER_UA;
        double sig = re.rangeSigma / GameConstants.GAME_UNITS_PER_UA;
        return Loc.Get("ui.system.range", au, sig);
    }

    private string ShortDetail(TrackInfo info)
    {
        if (info.isStar)
            return Loc.Get("ui.system.star.short", info.temperatureK, info.metallicity, info.ageGyr);

        Atmosphere a = info.atmosphere;
        if (a == null || a.composition == null || a.composition.Count == 0 || (!a.isEnvelope && a.surfacePressureAtm <= 0f))
            return Loc.Get("ui.system.airless", info.surfaceTemperatureK > 0f ? info.surfaceTemperatureK : info.temperatureK);

        _sb.Length = 0;
        AppendTopGases(_sb, a.composition, 2);
        if (a.isEnvelope) _sb.Append("  ").Append(Loc.Get("ui.system.envelope"));
        else _sb.Append("  ").Append(Loc.Get("ui.system.pressure", a.surfacePressureAtm));
        return _sb.ToString();
    }

    private string LongDetail(Track tr)
    {
        TrackInfo info = tr.info;
        _sb.Length = 0;
        _sb.Append("<b>").Append(tr.name).Append("</b>   ");
        _sb.Append(Loc.Get("ui.system.brg", tr.bearing));
        if (tr.hasRate) _sb.Append("   ").Append(Loc.Get("ui.track.rate", tr.rateDegPerDay));
        _sb.Append("   ").Append(RangeText(tr));
        if (tr.hasElevation)
        {
            double now = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
            _sb.Append("   ").Append(Loc.Get("ui.system.el", tr.elevationDeg, TrackManager.AgedElevationSigma(tr, now), tr.elevationSource));
        }
        else _sb.Append("   ").Append(Loc.Get("ui.system.noel"));
        if (tr.hasRadarRate && tr.radarFix.valid)
            _sb.Append("   ").Append(Loc.Get("ui.system.rrate", tr.radarRangeRateKmS));
        _sb.Append('\n');

        if (!info.identified)
        {
            if (!tr.Locked) _sb.Append(Loc.Get("ui.system.hint.lock"));
            else if (info.specDwellSeconds > 0f) _sb.Append(Loc.Get("ui.system.hint.dwell"));
            else _sb.Append(Loc.Get("ui.system.hint.spec"));
            return _sb.ToString();
        }

        string cls = !string.IsNullOrEmpty(info.surfaceClass) ? info.surfaceClass : info.bodyType;
        _sb.Append(Loc.Get("ui.system.class", cls));
        if (info.blended) _sb.Append("   <color=#").Append(ColorUtility.ToHtmlStringRGB(UITheme.Current.warning))
                             .Append('>').Append(Loc.Get("ui.system.blend")).Append("</color>");
        _sb.Append('\n');

        if (info.isStar)
        {
            _sb.Append(Loc.Get("ui.system.star.long", info.temperatureK, info.metallicity, info.ageGyr)).Append('\n');
        }
        else
        {
            _sb.Append(Loc.Get("ui.system.temps", info.temperatureK, info.surfaceTemperatureK)).Append('\n');
            Atmosphere a = info.atmosphere;
            if (a == null || a.composition == null || a.composition.Count == 0 || (!a.isEnvelope && a.surfacePressureAtm <= 0f))
                _sb.Append(Loc.Get("ui.system.atmo.none"));
            else
            {
                _sb.Append(Loc.Get(a.isEnvelope ? "ui.system.atmo.envelope" : "ui.system.atmo", a.surfacePressureAtm)).Append(' ');
                AppendTopGases(_sb, a.composition, 5);
            }
            _sb.Append('\n');
        }

        if (info.composition.Count > 0)
        {
            _sb.Append(Loc.Get("ui.system.comp")).Append(' ');
            AppendTopGases(_sb, info.composition, 6);
        }
        return _sb.ToString();
    }

    private static void AppendTopGases(StringBuilder sb, List<ChemicalComposition> list, int max)
    {
        // Largest first, without disturbing the source list.
        var sorted = new List<ChemicalComposition>(list);
        sorted.Sort((x, y) => y.percentage.CompareTo(x.percentage));
        int n = Mathf.Min(max, sorted.Count);
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(' ');
            // A literal '%' rather than a "P" format: "P" takes its glyph from the culture (see SpectrometerScreen).
            sb.Append(sorted[i].element).Append(' ').Append(sorted[i].percentage.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
        }
    }
}
