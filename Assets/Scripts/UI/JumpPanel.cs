using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Jump drive: a galaxy map (roadmap item 7, handoff-navigation-ui.md - scoped down, see its own progress
/// note) on the left, the existing nearby-systems list with distance/hydrogen cost/transit time/cost-to-get-
/// home-from-there on the right. Nothing is known about a destination except where it is. Home is always
/// listed when in range: jumping there ends the run as a return.
///
/// The map is Sol-centred in COORDINATES (Galaxy.cs: home sits at the origin, every other system's position
/// is a deterministic hash of the world seed and its cell - see its own doc comment), but the VIEWPORT centres
/// on the ship's own current galaxy position, panned/zoomed same as NavScreen's system map (same RectMask2D +
/// drag-to-pan + AutoFit pattern - see NavScreen.BuildMap's comment for why the mask has to sit on a node
/// other than the one MapCanvas itself is on). Read-only for now: it draws what Nearby() already returns and
/// where the jump-range ring sits, but selecting/jumping is still done through the list, not by clicking a
/// dot - deliberately not wired up this pass to keep the map itself low-risk (Galaxy/JumpDrive's own maths is
/// unchanged, already proven by the list that's worked all along).
///
/// NOT drawn yet, explicitly deferred: Atlas visited/catalogued/disputed state per system (the list doesn't
/// show it either - would need cross-referencing every nearby system's id against Atlas.Entries), route
/// preview, and the sector/galaxy zoom hierarchy the roadmap describes for a much later, far-out view.
/// </summary>
public sealed class JumpPanel
{
    private const int MaxRows = 9;
    private const float MinPxPerLy = 0.5f, MaxPxPerLy = 400f;

    private sealed class Row
    {
        public GameObject go;
        public TextMeshProUGUI name, dist, cost, time, back;
        public Button jump;
        public GalaxySystem target;
    }

    private readonly List<Row> _rows = new List<Row>();
    private TextMeshProUGUI _where, _empty;

    // Galaxy map.
    private RectTransform _mapRect;
    private MapCanvas _map;
    private Button _zoomIn, _zoomOut;
    private float _pixelsPerLy = 20f;
    private Vector2 _panOffsetPx;
    private bool _fitted;
    private readonly List<TextMeshProUGUI> _systemLabels = new List<TextMeshProUGUI>();

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform prt = UIKit.Node("Jump", parent);
        UIKit.Size(prt, flexibleWidth: 1f, flexibleHeight: 1f);
        // expandWidth:false - the sidebar is a fixed width, not an equal partner to split space with the map;
        // see NavScreen/GameShell's identical fix for why expandWidth:true would balloon it.
        var h = UIKit.HStack(prt, t.spacing * 2f, 0, expandWidth: false);
        h.childAlignment = TextAnchor.UpperLeft;

        BuildMap(prt);
        BuildSidebar(prt, t);

        return prt.gameObject;
    }

    private void BuildMap(Transform parent)
    {
        _mapRect = UIKit.Node("Map", parent);
        UIKit.Size(_mapRect, flexibleWidth: 1f, minHeight: 400f, flexibleHeight: 1f);
        // Same clip-and-pan treatment as NavScreen's system map: a RectMask2D on this node (a far-off system
        // outside the current zoom must run off the edge, not paint over the sidebar), MapCanvas on a CHILD
        // node (a mask doesn't clip a Graphic on its own same GameObject - Unity's clip search starts at the
        // graphic's PARENT).
        _mapRect.gameObject.AddComponent<RectMask2D>();

        RectTransform canvasRect = UIKit.Node("Canvas", _mapRect);
        UIKit.Stretch(canvasRect);
        _map = canvasRect.gameObject.AddComponent<MapCanvas>();
        _map.raycastTarget = true; // needed to receive the pan drag below
        var aim = canvasRect.gameObject.AddComponent<PointerAim>();
        aim.OnDragged = (delta, local, rt) => _panOffsetPx += delta;

        RectTransform zoomRow = UIKit.Node("Zoom", _mapRect);
        zoomRow.anchorMin = zoomRow.anchorMax = new Vector2(0f, 0f);
        zoomRow.pivot = new Vector2(0f, 0f);
        zoomRow.anchoredPosition = new Vector2(8f, 8f);
        zoomRow.sizeDelta = new Vector2(150f, 34f);
        UIKit.HStack(zoomRow, 6f, 0, expandWidth: true);
        _zoomOut = UIKit.AddButton(zoomRow, Loc.Get("ui.nav.zoomout"), () => Zoom(1f / 1.4f), 0f, 34f);
        _zoomIn = UIKit.AddButton(zoomRow, Loc.Get("ui.nav.zoomin"), () => Zoom(1.4f), 0f, 34f);
    }

    private void Zoom(float factor) => _pixelsPerLy = Mathf.Clamp(_pixelsPerLy * factor, MinPxPerLy, MaxPxPerLy);

    private void BuildSidebar(Transform parent, UITheme t)
    {
        // Same outer-LayoutElement / inner-Stretch+VStack decoupling as every other fixed-width sidebar in
        // this codebase (NavScreen, GameShell) - a VerticalLayoutGroup sharing a node with the LayoutElement
        // that's supposed to fix this column's width silently overrides it with its own children-derived size.
        RectTransform side = UIKit.Node("Sidebar", parent);
        UIKit.Size(side, preferredWidth: 450f, flexibleHeight: 1f);

        RectTransform inner = UIKit.Node("Inner", side);
        UIKit.Stretch(inner);
        var v = UIKit.VStack(inner, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;

        _where = UIKit.AddLabel(inner, "", t.fontSizeSmall, t.textDim);
        UIKit.Size(_where.rectTransform, minHeight: 20f);
        _empty = UIKit.AddLabel(inner, Loc.Get("ui.jump.none"), t.fontSizeSmall, t.textDim);

        for (int i = 0; i < MaxRows; i++) _rows.Add(BuildRow(inner));
    }

    private Row BuildRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        var row = new Row();

        RectTransform rt = UIKit.Node("Row", parent);
        row.go = rt.gameObject;
        var h = UIKit.HStack(rt, 6f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;

        row.name = Cell(rt, 95f, t.text);
        row.dist = Cell(rt, 65f, t.text);
        row.cost = Cell(rt, 60f, t.text);
        row.time = Cell(rt, 60f, t.text);
        row.back = Cell(rt, 75f, t.textDim);
        row.jump = UIKit.AddButton(rt, Loc.Get("ui.jump.go"), () => Go(row), 55f, 32f);

        row.go.SetActive(false);
        return row;
    }

    private static TextMeshProUGUI Cell(Transform parent, float width, Color color)
    {
        TextMeshProUGUI l = UIKit.AddLabel(parent, "", UITheme.Current.fontSizeSmall, color);
        UIKit.Size(l.rectTransform, preferredWidth: width);
        return l;
    }

    private static void Go(Row row)
    {
        if (Game.Jump != null) Game.Jump.Execute(row.target);
    }

    public void Refresh()
    {
        if (Game.Jump == null || Game.State == null || _where == null) return;
        UITheme t = UITheme.Current;
        JumpDrive drive = Game.Jump;
        GameState state = Game.State;

        UIKit.SetText(_where, Loc.Get("ui.jump.at", drive.CurrentSystemId, drive.DistanceFromHomeLy));

        IList<GalaxySystem> nearby = drive.Nearby();
        bool flying = Game.Run != null && Game.Run.Phase == RunPhase.Flight;
        _empty.gameObject.SetActive(nearby.Count == 0);

        // Home first, then the nearest others.
        string homeId = Galaxy.Home(state.WorldSeed).id;
        int shown = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < nearby.Count && shown < _rows.Count; i++)
            {
                bool isHome = nearby[i].id == homeId;
                if ((pass == 0) != isHome) continue;
                Fill(_rows[shown++], nearby[i], drive, state, flying, t);
            }
        }
        for (int i = shown; i < _rows.Count; i++)
            if (_rows[i].go.activeSelf) _rows[i].go.SetActive(false);

        DrawMap(drive, state, nearby, homeId, t);
    }

    private static void Fill(Row r, GalaxySystem s, JumpDrive drive, GameState state, bool flying, UITheme t)
    {
        JumpPlan plan = drive.Plan(s);
        bool affordable = drive.CanAfford(plan);
        if (!r.go.activeSelf) r.go.SetActive(true);
        r.target = s;

        UIKit.SetText(r.name, plan.isHome ? Loc.Get("ui.jump.home") : s.id);
        UIKit.SetText(r.dist, Loc.Get("ui.jump.dist", plan.distanceLy));
        UIKit.SetText(r.cost, Loc.Get("ui.jump.cost", plan.hydrogenCost));
        UIKit.SetText(r.time, Loc.Get("ui.jump.time", plan.days));
        UIKit.SetText(r.back, plan.isHome ? "" : Loc.Get("ui.jump.back", plan.returnCost));

        r.cost.color = affordable ? t.text : t.danger;
        // Warn when, after this jump, the hydrogen left would not pay for the way home.
        bool strands = !plan.isHome && (state.Hydrogen - plan.hydrogenCost) < plan.returnCost;
        r.back.color = strands ? t.warning : t.textDim;

        r.jump.interactable = flying && affordable;
    }

    private void DrawMap(JumpDrive drive, GameState state, IList<GalaxySystem> nearby, string homeId, UITheme t)
    {
        _map.Clear();

        // Guarded on a real size, not just "haven't fit yet": on the very first frame the JUMP tab is ever
        // shown, the map rect may still be at its pre-layout default size (no OnShown()-style forced rebuild
        // hook here, unlike NavScreen) - fitting against that would leave the zoom wrong until a manual +/-.
        if (!_fitted && _mapRect.rect.width > 1f)
        {
            float halfPx = Mathf.Max(60f, Mathf.Min(_mapRect.rect.width, _mapRect.rect.height) * 0.42f);
            _pixelsPerLy = Mathf.Clamp(halfPx / Mathf.Max(1f, state.Probe.jumpScanLy), MinPxPerLy, MaxPxPerLy);
            _panOffsetPx = Vector2.zero;
            _fitted = true;
        }

        Vector2 center = _mapRect.rect.center + _panOffsetPx;
        System.Func<double, double, Vector2> toScreen = (x, z) =>
            center + new Vector2((float)(x - drive.X), (float)(z - drive.Z)) * _pixelsPerLy;

        Vector2 shipScreen = toScreen(drive.X, drive.Z);
        _map.AddCircle(shipScreen, state.Probe.jumpScanLy * _pixelsPerLy, 1f, t.navPlane);
        _map.AddDot(shipScreen, 6f, t.accent);

        int shown = 0;
        for (int i = 0; i < nearby.Count; i++)
        {
            GalaxySystem s = nearby[i];
            bool isHome = s.id == homeId;
            Vector2 pos = toScreen(s.x, s.z);
            Color color = isHome ? t.navBody : t.navCatalogue;

            _map.AddDot(pos, isHome ? 6f : 4f, color);
            TextMeshProUGUI label = PooledLabel(shown, color);
            UIKit.SetText(label, isHome ? Loc.Get("ui.jump.home") : s.id);
            label.rectTransform.anchoredPosition = pos - center + new Vector2(6f, 6f);
            shown++;
        }
        for (int i = shown; i < _systemLabels.Count; i++) _systemLabels[i].gameObject.SetActive(false);

        _map.Rebuild();
    }

    private TextMeshProUGUI PooledLabel(int index, Color color)
    {
        while (_systemLabels.Count <= index)
        {
            TextMeshProUGUI l = UIKit.AddLabel(_mapRect, "", UITheme.Current.fontSizeSmall, color);
            RectTransform rt = l.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            _systemLabels.Add(l);
        }
        TextMeshProUGUI label = _systemLabels[index];
        label.color = color;
        label.gameObject.SetActive(true);
        return label;
    }
}
