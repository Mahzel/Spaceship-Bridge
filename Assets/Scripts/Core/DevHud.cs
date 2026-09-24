using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Developer overlay (IMGUI), added by Game.Boot in the editor and development builds only. F1 toggles it.
///
/// It is deliberately OMNISCIENT: it reads the generated SystemData and the spawned transforms directly, so it
/// shows what the player's tracking-only UI no longer can. Tabs:
///   RUN      run / power / clock / ship orbit, debug time rate, power refill.
///   SYSTEM   every node of the current system with true bearing, elevation, range and brightness from the
///            ship, plus which player track (if any) sits on it. Sortable; click a row to inspect it.
///   INSPECT  the full NodeData of the selected node: physical data, orbital elements, composition,
///            atmosphere, spectrum, live observables, parent / children navigation.
///   TRACKS   ground truth for each player track: nearest real body and the error of every estimate
///            (bearing, elevation, range) against its sigma. For tuning the sensors.
/// Nothing here feeds back into gameplay except the explicit debug buttons.
/// </summary>
public class DevHud : MonoBehaviour
{
    private enum Tab { Run, System, Inspect, Tracks, Hardware }
    private enum Sort { Tree, Range, Bearing, Brightness, Name }

    private const float MatchDeg = 2f;         // a track "sits on" a body within this angle
    private const double EarthMassesPerSun = 332946.0;
    private const double JupiterMassesPerSun = 1047.57;
    private const double SecondsPerDay = 86400.0;

    private bool _open;
    private Tab _tab = Tab.System;
    private Sort _sort = Sort.Tree;
    private bool _showBarycenters;
    private int _selected = -1;
    private Rect _win = new Rect(20f, 90f, 900f, 560f);
    private bool _resizing;
    private Vector2 _size = new Vector2(900f, 560f);
    private Vector2 _scroll;
    private string _lastSystemId;

    // Per-frame snapshot of the system, built once (OnGUI runs several times per frame).
    private struct Row
    {
        public int node, depth;
        public NodeData n;
        public Transform t;
        public CelestialBody body;
        public float bearing, elevation, lum;
        public double rangeAu;
        public int trackId;       // -1 = none
        public float trackErrDeg;
    }
    private readonly List<Row> _rows = new List<Row>();
    private readonly List<int> _order = new List<int>();
    private int _snapFrame = -1;

    private GUIStyle _mono, _monoDim, _monoWarn, _monoGood, _header;

    // IMGUI clicks would also reach the uGUI console underneath (a click on a row could mark a waterfall
    // track). An invisible uGUI raycast target on its own top canvas, kept under the window, swallows them.
    private RectTransform _blocker;
    private float _guiScale = 1f;

    // -----------------------------------------------------------------------------------------------------
    private void Awake()
    {
        var go = new GameObject("DevHudBlocker", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var img = new GameObject("Block", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        img.transform.SetParent(go.transform, false);
        img.GetComponent<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, 0f);
        _blocker = (RectTransform)img.transform;
        _blocker.anchorMin = _blocker.anchorMax = _blocker.pivot = new Vector2(0f, 1f);
        _blocker.gameObject.SetActive(false);
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame && !InputMap.TextFieldFocused()) _open = !_open;

        if (_blocker == null) return;
        if (_blocker.gameObject.activeSelf != _open) _blocker.gameObject.SetActive(_open);
        if (_open)
        {
            _blocker.anchoredPosition = new Vector2(_win.x * _guiScale, -_win.y * _guiScale);
            _blocker.sizeDelta = new Vector2(_win.width * _guiScale, _win.height * _guiScale);
        }
    }

    private void OnGUI()
    {
        if (Game.State == null || Game.Run == null || Game.Clock == null) return;
        EnsureStyles();

        float s = Mathf.Max(1f, Screen.height / 1080f);
        _guiScale = s;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        if (!_open)
        {
            GUI.Label(new Rect(8f, Screen.height / s - 22f, 200f, 20f), "DEV [F1]", _monoDim);
            return;
        }

        Snapshot();
        _win.size = _size;
        _win = GUI.Window(0x0DE7, _win, DrawWindow, "DEV  (F1 to hide)");
        _win.size = _size; // GUI.Window returns the rect it was given (plus drag): apply the resize after it
        _win.x = Mathf.Clamp(_win.x, -_win.width + 80f, Screen.width / s - 80f);
        _win.y = Mathf.Clamp(_win.y, 0f, Screen.height / s - 30f);
    }

    private void EnsureStyles()
    {
        if (_mono != null) return;
        _mono = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false, fontSize = 12 };
        Font f = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New" }, 12);
        if (f != null) _mono.font = f;
        _mono.margin = new RectOffset(2, 2, 0, 0);
        _mono.padding = new RectOffset(2, 2, 1, 1);
        _monoDim  = new GUIStyle(_mono); _monoDim.normal.textColor  = new Color(0.6f, 0.6f, 0.6f);
        _monoWarn = new GUIStyle(_mono); _monoWarn.normal.textColor = new Color(1f, 0.45f, 0.35f);
        _monoGood = new GUIStyle(_mono); _monoGood.normal.textColor = new Color(0.5f, 1f, 0.5f);
        _header   = new GUIStyle(_mono) { fontStyle = FontStyle.Bold };
    }

    private void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
            if (GUILayout.Toggle(_tab == t, t.ToString().ToUpperInvariant(), GUI.skin.button, GUILayout.Width(90f))) _tab = t;
        GUILayout.FlexibleSpace();
        GUILayout.Label(Summary(), _monoDim);
        GUILayout.EndHorizontal();

        _scroll = GUILayout.BeginScrollView(_scroll);
        switch (_tab)
        {
            case Tab.Run:     DrawRun(); break;
            case Tab.System:  DrawSystem(); break;
            case Tab.Inspect: DrawInspect(); break;
            case Tab.Tracks:  DrawTracks(); break;
            case Tab.Hardware: DrawHardware(); break;
        }
        GUILayout.EndScrollView();

        // Resize handle (bottom-right corner), then drag by the title bar.
        Rect handle = new Rect(_win.width - 16f, _win.height - 16f, 16f, 16f);
        GUI.Label(handle, "◢", _monoDim);
        Event e = Event.current;
        if (e.type == EventType.MouseDown && handle.Contains(e.mousePosition)) { _resizing = true; e.Use(); }
        else if (_resizing && e.type == EventType.MouseDrag)
        {
            _size.x = Mathf.Max(520f, _size.x + e.delta.x);
            _size.y = Mathf.Max(240f, _size.y + e.delta.y);
            e.Use();
        }
        else if (e.rawType == EventType.MouseUp) _resizing = false;
        GUI.DragWindow(new Rect(0f, 0f, _win.width, 20f));
    }

    private string Summary()
    {
        SystemManager sm = SystemManager.Current;
        string sys = sm != null && sm.CurrentData != null ? sm.CurrentData.id : "-";
        return $"seed {Game.State.WorldSeed}  sys {sys}  run #{Game.Run.RunNumber} [{Game.Run.Phase}]  day {Game.Clock.SimSeconds / SecondsPerDay:F2}";
    }

    // -----------------------------------------------------------------------------------------------------
    #region Snapshot
    private void Snapshot()
    {
        if (_snapFrame == Time.frameCount) return;
        _snapFrame = Time.frameCount;
        _rows.Clear();

        SystemManager sm = SystemManager.Current;
        SystemData data = sm != null ? sm.CurrentData : null;
        if (data == null) return;
        if (data.id != _lastSystemId) { _lastSystemId = data.id; _selected = -1; }

        Transform ship = SensorSight.Ship();
        IList<Track> tracks = Game.State.Tracks.All;
        var depth = new int[data.nodes.Count];

        for (int i = 0; i < data.nodes.Count; i++)
        {
            NodeData n = data.nodes[i];
            depth[i] = n.parent >= 0 ? depth[n.parent] + 1 : 0;
            Transform t = sm.NodeTransform(i);
            var row = new Row { node = i, depth = depth[i], n = n, t = t, trackId = -1 };
            if (t != null)
            {
                row.body = n.kind == NodeKind.Barycenter ? null : t.GetComponent<CelestialBody>();
                if (ship != null)
                {
                    Vector3 rel = t.position - ship.position;
                    row.bearing = BearingMath.Wrap360(Vector3.SignedAngle(Vector3.forward, new Vector3(rel.x, 0f, rel.z), Vector3.up));
                    row.elevation = Mathf.Atan2(rel.y, new Vector2(rel.x, rel.z).magnitude) * Mathf.Rad2Deg;
                    row.rangeAu = rel.magnitude / GameConstants.GAME_UNITS_PER_UA;
                }
                if (row.body != null) row.lum = row.body.apparentLuminosity;
            }
            if (n.kind != NodeKind.Barycenter)
            {
                float best = MatchDeg;
                for (int k = 0; k < tracks.Count; k++)
                {
                    float d = TrackDistance(tracks[k], row.bearing, row.elevation);
                    if (d < best) { best = d; row.trackId = tracks[k].id; row.trackErrDeg = d; }
                }
            }
            _rows.Add(row);
        }
    }

    /// <summary>Angle between a track's direction and a true direction: great-circle when the track has an
    /// elevation, bearing-only otherwise.</summary>
    private static float TrackDistance(Track tr, float bearing, float elevation)
    {
        return tr.hasElevation
            ? SensorSight.AngularDistance(tr.bearing, tr.elevationDeg, bearing, elevation)
            : Mathf.Abs(BearingMath.Diff(tr.bearing, bearing));
    }

    private int RowOf(int node)
    {
        for (int i = 0; i < _rows.Count; i++) if (_rows[i].node == node) return i;
        return -1;
    }
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region RUN
    // -----------------------------------------------------------------------------------------------------
    #region HARDWARE
    private static readonly string[] LevelLabels = { "NONE", "MK I", "MK II", "MK III" };

    /// <summary>Probe tiers, applied at once (in flight: live refit, run kept). Budget shown but not enforced.</summary>
    private void DrawHardware()
    {
        GameState state = Game.State;
        RunController run = Game.Run;
        Loadout lo = state.Loadout;
        bool flying = run.Phase == RunPhase.Flight;

        int cost = lo.TotalCost, budget = state.LoadoutBudget;
        GUILayout.Label($"Loadout cost {cost} / budget {budget} at trust {state.Trust:F1}{(cost > budget ? "   (OVER: the refit would refuse to launch)" : "")}", cost > budget ? _monoWarn : _mono);
        GUILayout.Label(flying ? "In flight: changes apply at once (tracks, data and orbit kept; a changed waterfall/radar restarts)."
                               : "Not in flight: changes set the loadout the next launch will fit.", _monoDim);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Trust", _mono, GUILayout.Width(60f));
        foreach (int t in new[] { 0, 25, 50, 75, 100 })
            if (GUILayout.Button(t.ToString(), GUILayout.Width(50f))) state.DevSetTrust(t);
        if (GUILayout.Button("-5", GUILayout.Width(40f))) state.DevSetTrust(state.Trust - 5f);
        if (GUILayout.Button("+5", GUILayout.Width(40f))) state.DevSetTrust(state.Trust + 5f);
        GUILayout.EndHorizontal();
        GUILayout.Space(6f);

        bool changed = false;
        foreach (ProbeSystem s in Loadout.All)
        {
            if (s == ProbeSystem.Battery) GUILayout.Space(6f);
            int cur = lo.Level(s);
            GUILayout.BeginHorizontal();
            GUILayout.Label(s.ToString(), _mono, GUILayout.Width(110f));
            for (int lvl = 0; lvl <= Loadout.MaxLevel; lvl++)
            {
                if (lvl < Loadout.MinLevel(s)) { GUILayout.Space(74f); continue; }
                string label = lvl == 0 && !Loadout.IsSensor(s) ? "STOCK" : LevelLabels[lvl];
                if (GUILayout.Toggle(cur == lvl, label, GUI.skin.button, GUILayout.Width(70f)) && cur != lvl)
                {
                    lo.Set(s, lvl);
                    changed = true;
                }
            }
            GUILayout.Label($" {Loadout.Cost(s, cur),2} pt  " + LoadoutSpecs.Describe(s, cur, state.SpecAssets, state.BaseProbe), _monoDim);
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Baseline", GUILayout.Width(100f))) { lo.ResetToBaseline(); changed = true; }
        if (GUILayout.Button("All Mk I", GUILayout.Width(100f))) { SetAll(lo, 1); changed = true; }
        if (GUILayout.Button("All Mk II", GUILayout.Width(100f))) { SetAll(lo, 2); changed = true; }
        if (GUILayout.Button("All Mk III", GUILayout.Width(100f))) { SetAll(lo, 3); changed = true; }
        GUILayout.EndHorizontal();

        if (changed && flying) state.DevRefitLive();

        // What the probe is actually running on right now.
        GUILayout.Space(8f);
        GUILayout.Label("Fitted now", _header);
        Fitted("Waterfall", state.GetSpec<WaterfallSpec>());
        Fitted("Imager", state.GetSpec<ImagerSpec>());
        Fitted("Spectrometer", state.GetSpec<SpectrometerSpec>());
        Fitted("Radar", state.GetSpec<RadarSpec>());
        ProbeSpec p = state.Probe;
        GUILayout.Label($"  power {state.PowerStored:F0}/{state.PowerCapacity:F0}   H2 {state.Hydrogen:F1}/{state.HydrogenCapacity:F0}   storage {state.StorageUsed:F1}/{state.StorageCapacity:F0}   tx {p.txRangeLy:F1} ly @ {p.txRate:F1}/d", _mono);
    }

    private void Fitted(string label, SensorSpec spec)
    {
        GUILayout.Label($"  {label,-13} {(spec != null ? spec.name : "-")}", spec != null ? _mono : _monoDim);
    }

    private static void SetAll(Loadout lo, int level)
    {
        foreach (ProbeSystem s in Loadout.All) lo.Set(s, level);
    }
    #endregion

    private void DrawRun()
    {
        GameState state = Game.State;
        RunController run = Game.Run;
        GameClock clock = Game.Clock;

        SystemManager sm = SystemManager.Current;
        float flux = sm != null ? sm.StellarFluxAtShip() : 0f;
        GUILayout.Label($"Run #{run.RunNumber}  [{run.Phase}]   elapsed {run.RunElapsedDays:F2} d   flux {flux:F4} S☉", _mono);

        float net = run.IncomePerDay - run.LoadPerDay;
        GUILayout.Label($"Power {state.PowerStored:F1} / {state.PowerCapacity:F0}   net {net:+0.00;-0.00}/d  (+{run.IncomePerDay:F2} income, -{run.LoadPerDay:F2} load)", _mono);
        GUILayout.Label($"Clock {clock.SimSeconds:F0} s  warp {clock.WarpFactor:0.##} sim-s/s  ladder {GameClock.WarpLabel(clock.WarpLadderIndex())}{(clock.Paused ? "  PAUSED" : "")}", _mono);

        ShipState ship = state.Ship;
        GUILayout.Label($"Ship  pos ({ship.x / 100.0:F3}, {ship.y / 100.0:F3}, {ship.z / 100.0:F3}) AU   v {ship.Speed:F3} km/s   hdg {ship.headingDeg:F1}°", _mono);

        ShipOrbit o = state.ShipOrbit;
        if (o.HasTrajectory)
        {
            GUILayout.Label($"Orbit around {o.PrimaryName} (node {o.PrimaryIndex})  {(o.Hyperbolic ? "HYPERBOLIC" : "bound")}", _mono);
            GUILayout.Label($"  a {o.SemiMajorAxis / 100.0:F4} AU  e {o.Eccentricity:F5}  i {o.Elements.inclination:F2}°  ν {o.TrueAnomalyDeg:F1}°", _mono);
            GUILayout.Label($"  peri {o.PeriapsisGame / 100f:F4} AU   apo {(o.Hyperbolic ? "∞" : (o.ApoapsisGame / 100f).ToString("F4"))} AU   T {o.Elements.orbitalPeriod / SecondsPerDay:F2} d", _mono);
        }
        else GUILayout.Label("Orbit: none", _monoDim);

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        bool fast = clock.BaseRate > 1.0;
        if (GUILayout.Button(fast ? $"Base rate x{GameConstants.TIME_MULTIPLIER:0} (DEBUG) → real time" : "Base rate x1 → debug x" + GameConstants.TIME_MULTIPLIER.ToString("0"), GUILayout.Width(330f)))
            clock.BaseRate = fast ? 1.0 : GameConstants.TIME_MULTIPLIER;
        if (GUILayout.Button("Refill power", GUILayout.Width(120f)))
            state.ChangePower(state.PowerCapacity);
        GUILayout.EndHorizontal();
    }
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region SYSTEM
    private void DrawSystem()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Sort:", _monoDim, GUILayout.Width(40f));
        foreach (Sort so in System.Enum.GetValues(typeof(Sort)))
            if (GUILayout.Toggle(_sort == so, so.ToString(), GUI.skin.button, GUILayout.Width(84f))) _sort = so;
        GUILayout.Space(12f);
        _showBarycenters = GUILayout.Toggle(_showBarycenters, " barycenters");
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (_rows.Count == 0) { GUILayout.Label("No system loaded.", _monoDim); return; }

        _order.Clear();
        for (int i = 0; i < _rows.Count; i++)
            if (_showBarycenters || _rows[i].n.kind != NodeKind.Barycenter) _order.Add(i);
        switch (_sort)
        {
            case Sort.Range:      _order.Sort((a, b) => _rows[a].rangeAu.CompareTo(_rows[b].rangeAu)); break;
            case Sort.Bearing:    _order.Sort((a, b) => _rows[a].bearing.CompareTo(_rows[b].bearing)); break;
            case Sort.Brightness: _order.Sort((a, b) => _rows[b].lum.CompareTo(_rows[a].lum)); break;
            case Sort.Name:       _order.Sort((a, b) => string.CompareOrdinal(_rows[a].n.name, _rows[b].n.name)); break;
        }

        GUILayout.Label(string.Format("{0,-26} {1,-14} {2,-12} {3,7} {4,7} {5,10} {6,10} {7,-10}",
            "NAME", "TYPE", "CLASS", "BRG°", "EL°", "RANGE AU", "APP.LUM", "TRACK"), _header);

        for (int k = 0; k < _order.Count; k++)
        {
            Row r = _rows[_order[k]];
            string indent = _sort == Sort.Tree ? new string(' ', r.depth * 2) : "";
            string name = Clip(indent + r.n.name, 26);
            string type = r.n.kind == NodeKind.Barycenter ? "(barycenter)" : Clip(r.n.bodyType, 14);
            string trk = r.trackId >= 0 ? $"T{r.trackId} {r.trackErrDeg:F2}°" : "";
            string line = string.Format("{0,-26} {1,-14} {2,-12} {3,7:F2} {4,7:F2} {5,10:F4} {6,10:G3} {7,-10}",
                name, type, Clip(r.n.surfaceClass, 12), r.bearing, r.elevation, r.rangeAu, r.lum, trk);

            GUIStyle st = r.node == _selected ? _monoGood : (r.n.kind == NodeKind.Barycenter ? _monoDim : _mono);
            if (GUILayout.Button(line, st)) { _selected = r.node; _tab = Tab.Inspect; _scroll = Vector2.zero; }
        }
    }

    private static string Clip(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region INSPECT
    private void DrawInspect()
    {
        int ri = RowOf(_selected);
        if (ri < 0) { GUILayout.Label("Nothing selected: click a row in SYSTEM.", _monoDim); return; }
        Row r = _rows[ri];
        NodeData n = r.n;
        SystemData data = SystemManager.Current.CurrentData;

        GUILayout.BeginHorizontal();
        GUILayout.Label($"<b>{n.name}</b>   node {n.index}   {n.kind}", _mono);
        GUILayout.FlexibleSpace();
        if (n.kind != NodeKind.Barycenter && GUILayout.Button("Set NAV target", GUILayout.Width(120f)))
            Game.State.SetTarget(n.name);
        GUILayout.EndHorizontal();

        // Hierarchy
        GUILayout.BeginHorizontal();
        GUILayout.Label("Parent:", _monoDim, GUILayout.Width(70f));
        if (n.parent >= 0) { if (GUILayout.Button(data.nodes[n.parent].name, GUILayout.ExpandWidth(false))) _selected = n.parent; }
        else GUILayout.Label("(root)", _monoDim);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Children:", _monoDim, GUILayout.Width(70f));
        int shown = 0;
        for (int i = 0; i < data.nodes.Count; i++)
        {
            if (data.nodes[i].parent != n.index) continue;
            if (shown > 0 && shown % 6 == 0) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); GUILayout.Space(74f); }
            if (GUILayout.Button(data.nodes[i].name, GUILayout.ExpandWidth(false))) _selected = i;
            shown++;
        }
        if (shown == 0) GUILayout.Label("none", _monoDim);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Live geometry
        Section("OBSERVED FROM SHIP (truth)");
        Line($"bearing {r.bearing:F3}°   elevation {r.elevation:F3}°   range {r.rangeAu:F5} AU ({r.rangeAu * 149.598:F2} Gm)");
        if (r.body != null)
            Line($"apparent lum {r.body.apparentLuminosity:G4}   angular size {r.body.angularSize:G4}   phase {r.body.phase:F3}");
        Line(r.trackId >= 0 ? $"covered by track T{r.trackId} ({r.trackErrDeg:F2}° off)" : "no track on it");

        if (n.kind != NodeKind.Barycenter)
        {
            Section("PHYSICAL");
            Line($"type {n.bodyType}   surface class {Or(n.surfaceClass)}");
            if (n.kind == NodeKind.Star)
            {
                Line($"mass {n.mass:G4} M☉   radius {n.radiusSol:G4} R☉   luminosity {n.starLuminosity:G4} L☉");
                Line($"T_eff {n.temperature:F0} K   [Fe/H] {n.metallicity:+0.00;-0.00} dex   age {n.ageGyr:F2} Gyr");
            }
            else
            {
                Line($"mass {n.mass * EarthMassesPerSun:G4} M⊕  ({n.mass * JupiterMassesPerSun:G3} MJ, {n.mass:G3} M☉)");
                Line($"radius {(n.radiusSol > 0f ? (n.radiusSol * 109.08f).ToString("G4") + " R⊕" : "-")}   density {n.density:G4}   albedo {n.albedo:F3}");
                Line($"T_eq {n.temperature:F0} K   T_surface {n.surfaceTemperature:F0} K  (greenhouse +{n.surfaceTemperature - n.temperature:F0} K)");
            }
            Line($"render radius {n.radiusGame:G4} game units", _monoDim);
        }

        Section("ORBIT (relative to parent)");
        if (n.hasOrbit)
        {
            OrbitElements o = n.orbit;
            double aAu = o.semiMajorAxis / GameConstants.GAME_UNITS_PER_UA;
            Line($"a {aAu:F5} AU   e {o.eccentricity:F5}   i {o.inclination:F3}°");
            Line($"Ω {o.longitudeAscNode:F3}°   ω {o.argumentPeriapsis:F3}°   M0 {o.meanAnomalyAtEpoch:F3}°   M now {KeplerOrbit.MeanAnomalyDeg(o, Game.Clock.SimSeconds):F3}°");
            Line($"peri {aAu * (1.0 - o.eccentricity):F5} AU   apo {aAu * (1.0 + o.eccentricity):F5} AU   period {o.orbitalPeriod / SecondsPerDay:F3} d ({o.orbitalPeriod / GameConstants.SECONDS_PER_YEAR:F4} yr)");
        }
        else Line("none (root)", _monoDim);

        if (n.composition != null && n.composition.Count > 0)
        {
            Section("BULK COMPOSITION");
            Line(Composition(n.composition));
        }

        if (n.atmosphere != null && n.kind != NodeKind.Barycenter)
        {
            Section("ATMOSPHERE");
            Atmosphere a = n.atmosphere;
            Line(a.isEnvelope ? "deep H/He envelope (no surface)" : $"surface pressure {a.surfacePressureAtm:G4} atm");
            if (a.composition != null && a.composition.Count > 0) Line(Composition(a.composition));
            else Line("airless", _monoDim);
        }

        if (n.spectrum != null)
        {
            Section("SPECTRUM");
            int em = n.spectrum.emissionLines != null ? n.spectrum.emissionLines.Count : 0;
            int ab = n.spectrum.absorptionLines != null ? n.spectrum.absorptionLines.Count : 0;
            Line($"{em} emission lines, {ab} absorption lines");
            if (em > 0) Line("emission:   " + TopLines(n.spectrum.emissionLines, 8));
            if (ab > 0) Line("absorption: " + TopLines(n.spectrum.absorptionLines, 8));
        }
    }

    private void Section(string title) { GUILayout.Space(6f); GUILayout.Label(title, _header); }
    private void Line(string s, GUIStyle st = null) { GUILayout.Label("  " + s, st ?? _mono); }
    private static string Or(string s) { return string.IsNullOrEmpty(s) ? "-" : s; }

    private static string Composition(List<ChemicalComposition> list)
    {
        var sorted = new List<ChemicalComposition>(list);
        sorted.Sort((a, b) => b.percentage.CompareTo(a.percentage));
        var sb = new StringBuilder();
        for (int i = 0; i < sorted.Count; i++)
        {
            if (i > 0) sb.Append(i % 8 == 0 ? "\n  " : "  ");
            sb.Append(sorted[i].element).Append(' ').Append(sorted[i].percentage.ToString("G3")).Append('%');
        }
        return sb.ToString();
    }

    private static string TopLines(List<SpectralLine> lines, int max)
    {
        var sorted = new List<SpectralLine>(lines);
        sorted.Sort((a, b) => b.intensity.CompareTo(a.intensity));
        var sb = new StringBuilder();
        for (int i = 0; i < sorted.Count && i < max; i++)
        {
            if (i > 0) sb.Append("  ");
            sb.Append(string.IsNullOrEmpty(sorted[i].species) ? "?" : sorted[i].species)
              .Append(' ').Append(sorted[i].wavelength.ToString("F1")).Append("nm");
        }
        if (sorted.Count > max) sb.Append("  …");
        return sb.ToString();
    }
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region TRACKS
    private void DrawTracks()
    {
        IList<Track> tracks = Game.State.Tracks.All;
        if (tracks.Count == 0) { GUILayout.Label("No tracks.", _monoDim); return; }
        double now = Game.Clock.SimSeconds;

        GUILayout.Label("Errors are estimate − truth; red when beyond 3σ. Truth = nearest body to the track direction.", _monoDim);
        GUILayout.Label(string.Format("{0,-12} {1,-5} {2,-22} {3,9} {4,16} {5,24} {6,-16}",
            "TRACK", "STAT", "TRUTH (nearest)", "ΔBRG°", "ΔEL° (σ)", "RANGE AU est±σ / true", "ID"), _header);

        foreach (Track tr in tracks)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].n.kind == NodeKind.Barycenter) continue;
                float d = TrackDistance(tr, _rows[i].bearing, _rows[i].elevation);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) { GUILayout.Label($"T{tr.id}  (no bodies)", _monoDim); continue; }
            Row r = _rows[best];

            float dBrg = BearingMath.Diff(tr.bearing, r.bearing);
            string el = "-";
            bool elBad = false;
            if (tr.hasElevation)
            {
                float sig = TrackManager.AgedElevationSigma(tr, now);
                float dEl = tr.elevationDeg - r.elevation;
                el = $"{dEl:+0.00;-0.00} ({sig:F2})";
                elBad = Mathf.Abs(dEl) > 3f * sig;
            }

            string rng = "-";
            bool rngBad = false;
            RangeEstimate est = TrackManager.BestRange(tr, now);
            if (est.valid)
            {
                double e = est.range / GameConstants.GAME_UNITS_PER_UA, s = est.rangeSigma / GameConstants.GAME_UNITS_PER_UA;
                rng = $"{e:F3}±{s:F3} / {r.rangeAu:F3}";
                rngBad = System.Math.Abs(e - r.rangeAu) > 3.0 * s;
            }

            string id = tr.info.identified
                ? (tr.info.catalogName == r.n.name ? "ok " : "WRONG ") + Or(tr.info.catalogName)
                : $"dwell {tr.info.specDwellSeconds:F0}s";
            bool idBad = tr.info.identified && tr.info.catalogName != r.n.name;

            string line = string.Format("{0,-12} {1,-5} {2,-22} {3,9:+0.00;-0.00} {4,16} {5,24} {6,-16}",
                Clip($"T{tr.id} {tr.name}", 12), tr.Locked ? "LOCK" : "srch", Clip(r.n.name, 16) + $" {bestD:F1}°",
                dBrg, el, rng, Clip(id, 16));
            GUIStyle st = elBad || rngBad || idBad || bestD > MatchDeg ? _monoWarn : _mono;
            if (GUILayout.Button(line, st)) { _selected = r.node; _tab = Tab.Inspect; _scroll = Vector2.zero; }
        }
    }
    #endregion
}
