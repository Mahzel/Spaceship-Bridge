using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The persistent survey log (Game.State.Atlas), the ATLAS major mode's sole content (UI shell rework - no
/// longer a draggable floating panel), browsed in three levels:
///   SYSTEMS  every system with data (home first), with body / entry counts and value;
///   SYSTEM   that system's known bodies as a tree (moons indented under their planet; a parent nobody surveyed
///            is shown dimmed, for context), then an "unidentified contacts" group;
///   BODY     what is known about it (spectrometer identification, or the full catalogue for home bodies) and
///            the survey entries filed under it.
/// A breadcrumb + BACK climbs up; long lists page with prev / next.
///
/// The tree comes from regenerating the system (SystemFactory is deterministic), but only bodies the Atlas
/// already names are shown, plus their ancestors. Entries still never reveal their ground-truth isFalse flag.
/// </summary>
public sealed class AtlasPanel
{
    private const int PageRows = 12;       // rows built; lists page by RowsPerPage
    private const int BodyPageRows = 5;    // the body page also shows its details block: fewer rows keep it compact
    private int RowsPerPage => _level == Level.Body ? BodyPageRows : PageRows;
    private const float W0 = 190f, W1 = 120f, W2 = 60f, W3 = 70f, W4 = 120f;

    private enum Level { Systems, System, Body, Unidentified }

    private sealed class Row
    {
        public GameObject go;
        public Button button;
        public TextMeshProUGUI c0, c1, c2, c3, c4;
    }

    /// <summary>One line of the current list, whatever the level.</summary>
    private struct Item
    {
        public string c0, c1, c2, c3, c4;
        public bool dim, warn, clickable;
        public string key; // system id / body name / "" for the unidentified group
    }

    private readonly List<Row> _rows = new List<Row>();
    private readonly List<Item> _items = new List<Item>();
    private TextMeshProUGUI _title, _crumb, _empty, _details, _pageLabel;
    private TextMeshProUGUI _h0, _h1, _h2, _h3, _h4;
    private Button _back, _prev, _next, _recall;
    private TextMeshProUGUI _recallLabel, _recallFlash;
    private GameObject _pager;

    private Level _level = Level.Systems;
    private string _system, _body;
    private int _page;

    // RECALL target for the current Level.Body page (set fresh by BuildBody every Refresh) - see
    // GhostContact.FromCatalogued/FromEntry. _recallData/_recallNodeIndex for the precise catalogued-orbit
    // path, _recallEntry for the rough coasted-fix path; at most one of the two is set at a time.
    private SystemData _recallData;
    private int _recallNodeIndex = -1;
    private AtlasEntry _recallEntry;
    private string _recallName;
    private float _flashHold;

    // Regenerated systems, for the body tree and body details (deterministic per world seed).
    private readonly Dictionary<string, SystemData> _systems = new Dictionary<string, SystemData>();
    private int _systemsSeed = int.MinValue;

    // -----------------------------------------------------------------------------------------------------
    #region Build
    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        // Fills the content rect the ATLAS major mode is given (GameShell) - no longer an absolute-anchored,
        // auto-sized, draggable floating window.
        Image panel = UIKit.AddPanel(parent, "AtlasPanel", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        UIKit.Stretch(prt);

        var v = UIKit.VStack(prt, t.spacing * 0.5f, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;

        RectTransform top = UIKit.Node("Top", prt);
        UIKit.HStack(top, 8f, 0).childAlignment = TextAnchor.MiddleLeft;
        _back = UIKit.AddButton(top, Loc.Get("menu.back"), Back, 70f, 28f);
        _title = UIKit.AddLabel(top, Loc.Get("ui.atlas"), t.fontSizeBody, t.accent);
        _crumb = UIKit.AddLabel(top, "", t.fontSizeSmall, t.textDim);

        _empty = UIKit.AddLabel(prt, Loc.Get("ui.atlas.none"), t.fontSizeSmall, t.textDim);
        _details = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.text);
        _details.textWrappingMode = TextWrappingModes.Normal;
        UIKit.Size(_details.rectTransform, preferredWidth: 610f);

        RectTransform recallRow = UIKit.Node("Recall", prt);
        UIKit.HStack(recallRow, 8f, 0).childAlignment = TextAnchor.MiddleLeft;
        _recall = UIKit.AddButton(recallRow, Loc.Get("ui.atlas.recall"), OnRecallClicked, 220f, 30f);
        _recallLabel = _recall.GetComponentInChildren<TextMeshProUGUI>();
        _recallFlash = UIKit.AddLabel(recallRow, "", t.fontSizeSmall, t.textDim);

        RectTransform header = UIKit.Node("Header", prt);
        UIKit.HStack(header, 6f, 0).childAlignment = TextAnchor.MiddleLeft;
        _h0 = HeaderCell(header, W0); _h1 = HeaderCell(header, W1); _h2 = HeaderCell(header, W2);
        _h3 = HeaderCell(header, W3); _h4 = HeaderCell(header, W4);

        for (int i = 0; i < PageRows; i++) _rows.Add(BuildRow(prt, i));

        RectTransform pager = UIKit.Node("Pager", prt);
        _pager = pager.gameObject;
        UIKit.HStack(pager, 8f, 0).childAlignment = TextAnchor.MiddleLeft;
        _prev = UIKit.AddButton(pager, "<", () => { _page--; }, 40f, 26f);
        _pageLabel = UIKit.AddLabel(pager, "", t.fontSizeSmall, t.textDim, TextAlignmentOptions.Center);
        UIKit.Size(_pageLabel.rectTransform, preferredWidth: 110f);
        _next = UIKit.AddButton(pager, ">", () => { _page++; }, 40f, 26f);

        return prt.gameObject;
    }

    private static TextMeshProUGUI HeaderCell(Transform parent, float width)
    {
        UITheme t = UITheme.Current;
        TextMeshProUGUI l = UIKit.AddLabel(parent, "", t.fontSizeSmall, t.textDim);
        UIKit.Size(l.rectTransform, preferredWidth: width);
        return l;
    }

    private Row BuildRow(Transform parent, int index)
    {
        UITheme t = UITheme.Current;
        var row = new Row();

        RectTransform rt = UIKit.Node("Row", parent);
        row.go = rt.gameObject;
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = Color.white;
        row.button = rt.gameObject.AddComponent<Button>();
        row.button.targetGraphic = bg;
        ColorBlock cb = row.button.colors;
        cb.normalColor = new Color(0f, 0f, 0f, 0f);
        cb.highlightedColor = t.buttonHover;
        cb.pressedColor = t.buttonPressed;
        cb.selectedColor = new Color(0f, 0f, 0f, 0f);
        cb.disabledColor = new Color(0f, 0f, 0f, 0f);
        row.button.colors = cb;
        int captured = index;
        row.button.onClick.AddListener(() => OnRowClicked(captured));

        var h = UIKit.HStack(rt, 6f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;
        row.c0 = Cell(rt, W0); row.c1 = Cell(rt, W1); row.c2 = Cell(rt, W2); row.c3 = Cell(rt, W3); row.c4 = Cell(rt, W4);
        UIKit.Size(rt, minHeight: t.fontSizeSmall + 8f);

        row.go.SetActive(false);
        return row;
    }

    private static TextMeshProUGUI Cell(Transform parent, float width)
    {
        TextMeshProUGUI l = UIKit.AddLabel(parent, "", UITheme.Current.fontSizeSmall, UITheme.Current.text);
        UIKit.Size(l.rectTransform, preferredWidth: width);
        return l;
    }
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region Navigation
    private void OnRowClicked(int rowIndex)
    {
        int i = _page * RowsPerPage + rowIndex;
        if (i < 0 || i >= _items.Count || !_items[i].clickable) return;
        Item it = _items[i];
        switch (_level)
        {
            case Level.Systems: _system = it.key; _level = Level.System; break;
            case Level.System:
                if (string.IsNullOrEmpty(it.key)) _level = Level.Unidentified;
                else { _body = it.key; _level = Level.Body; }
                break;
        }
        _page = 0;
        Refresh();
    }

    /// <summary>Seeds a fresh, aimable track from the current body page's RECALL target (set by BuildBody
    /// every Refresh) - a precise recall off a catalogued orbit, or a rough one coasted forward from the
    /// freshest survey fix on file. See GhostContact's own doc comment for what each does and doesn't know.</summary>
    private void OnRecallClicked()
    {
        Track tr = _recallNodeIndex >= 0 ? GhostContact.FromCatalogued(_recallData, _recallNodeIndex, _recallName)
                 : _recallEntry != null ? GhostContact.FromEntry(_recallEntry, _recallName)
                 : null;
        UIKit.SetText(_recallFlash, Loc.Get(tr != null ? "ui.atlas.recall.done" : "ui.atlas.recall.full", tr?.name ?? ""));
        _flashHold = 4f;
    }

    private void Back()
    {
        switch (_level)
        {
            case Level.System: _level = Level.Systems; break;
            case Level.Body:
            case Level.Unidentified: _level = Level.System; break;
        }
        _page = 0;
        Refresh();
    }
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region Refresh
    public void Refresh()
    {
        if (Game.State == null || _empty == null) return;
        IList<AtlasEntry> all = Game.State.Atlas.Entries;
        if (Game.State.WorldSeed != _systemsSeed) { _systems.Clear(); _systemsSeed = Game.State.WorldSeed; }

        // A New Game can empty what we were looking at.
        if (_level != Level.Systems && !HasSystem(all, _system)) { _level = Level.Systems; _page = 0; }

        _items.Clear();
        string details = "";
        switch (_level)
        {
            case Level.Systems:      BuildSystems(all); break;
            case Level.System:       BuildSystem(all, _system); break;
            case Level.Body:         details = BuildBody(all, _system, _body); break;
            case Level.Unidentified: BuildUnidentified(all, _system); break;
        }

        UIKit.SetText(_title, _level == Level.Systems ? Loc.Get("ui.atlas.count", all.Count) : Loc.Get("ui.atlas"));
        UIKit.SetText(_crumb, Crumb());
        _back.gameObject.SetActive(_level != Level.Systems);
        _empty.gameObject.SetActive(_level == Level.Systems && _items.Count == 0);
        _details.gameObject.SetActive(details.Length > 0);
        UIKit.SetText(_details, details);
        SetHeader();

        if (_level != Level.Body) { _recallData = null; _recallNodeIndex = -1; _recallEntry = null; }
        bool canRecall = _recallNodeIndex >= 0 || _recallEntry != null;
        _recall.gameObject.SetActive(_level == Level.Body);
        _recall.interactable = canRecall;
        UIKit.SetText(_recallLabel, Loc.Get(_recallNodeIndex >= 0 ? "ui.atlas.recall"
                                          : _recallEntry != null ? "ui.atlas.recall.rough" : "ui.atlas.recall.none"));
        if (_flashHold > 0f) _flashHold -= Time.unscaledDeltaTime; else UIKit.SetText(_recallFlash, "");

        int pages = Mathf.Max(1, (_items.Count + RowsPerPage - 1) / RowsPerPage);
        _page = Mathf.Clamp(_page, 0, pages - 1);
        _pager.SetActive(pages > 1);
        UIKit.SetText(_pageLabel, Loc.Get("ui.atlas.page", _page + 1, pages));
        _prev.interactable = _page > 0;
        _next.interactable = _page < pages - 1;

        UITheme t = UITheme.Current;
        for (int r = 0; r < _rows.Count; r++)
        {
            Row row = _rows[r];
            int i = _page * RowsPerPage + r;
            bool show = r < RowsPerPage && i < _items.Count;
            if (row.go.activeSelf != show) row.go.SetActive(show);
            if (!show) continue;
            Item it = _items[i];
            row.button.interactable = it.clickable;
            Color main = it.warn ? t.danger : (it.dim ? t.textDim : t.text);
            Set(row.c0, it.c0, main);
            Set(row.c1, it.c1, t.textDim);
            Set(row.c2, it.c2, main);
            Set(row.c3, it.c3, main);
            Set(row.c4, it.c4, it.warn ? t.danger : t.textDim);
        }
    }

    private static void Set(TextMeshProUGUI l, string text, Color c)
    {
        UIKit.SetText(l, text ?? "");
        if (l.color != c) l.color = c;
    }

    private string Crumb()
    {
        switch (_level)
        {
            case Level.System:       return "> " + _system;
            case Level.Body:         return "> " + _system + " > " + _body;
            case Level.Unidentified: return "> " + _system + " > " + Loc.Get("ui.atlas.unidentified");
            default:                 return "";
        }
    }

    private void SetHeader()
    {
        switch (_level)
        {
            case Level.Systems:
                Header("ui.atlas.col.system", "ui.atlas.col.bodies", "ui.atlas.col.entries", "ui.atlas.col.value", "ui.atlas.col.status"); break;
            case Level.System:
                Header("ui.atlas.col.name", "ui.atlas.col.kind", "ui.atlas.col.entries", "ui.atlas.col.value", "ui.atlas.col.status"); break;
            default:
                Header("ui.atlas.col.contact", "ui.atlas.col.kind", "ui.atlas.col.run", "ui.atlas.col.value", "ui.atlas.col.status"); break;
        }
    }

    private void Header(string k0, string k1, string k2, string k3, string k4)
    {
        UIKit.SetText(_h0, Loc.Get(k0)); UIKit.SetText(_h1, Loc.Get(k1)); UIKit.SetText(_h2, Loc.Get(k2));
        UIKit.SetText(_h3, Loc.Get(k3)); UIKit.SetText(_h4, Loc.Get(k4));
    }
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region Levels
    private static bool HasSystem(IList<AtlasEntry> all, string id)
    {
        for (int i = 0; i < all.Count; i++) if (all[i].systemId == id) return true;
        return false;
    }

    private void BuildSystems(IList<AtlasEntry> all)
    {
        var order = new List<string>();
        var entries = new Dictionary<string, int>();
        var value = new Dictionary<string, float>();
        var bodies = new Dictionary<string, HashSet<string>>();
        var lastRun = new Dictionary<string, int>();
        for (int i = 0; i < all.Count; i++)
        {
            AtlasEntry e = all[i];
            if (!entries.ContainsKey(e.systemId))
            {
                order.Add(e.systemId);
                entries[e.systemId] = 0; value[e.systemId] = 0f;
                bodies[e.systemId] = new HashSet<string>(); lastRun[e.systemId] = 0;
            }
            entries[e.systemId]++;
            value[e.systemId] += e.value;
            if (!string.IsNullOrEmpty(e.bodyName)) bodies[e.systemId].Add(e.bodyName);
            if (e.runNumber > lastRun[e.systemId]) lastRun[e.systemId] = e.runNumber;
        }
        // Home first, then in order of discovery (entries are appended in time order).
        if (order.Remove(SolSystem.Id)) order.Insert(0, SolSystem.Id);

        foreach (string id in order)
        {
            _items.Add(new Item
            {
                c0 = id, c1 = bodies[id].Count.ToString(), c2 = entries[id].ToString(),
                c3 = Loc.Get("ui.data.value", value[id]),
                c4 = id == SolSystem.Id ? Loc.Get("ui.atlas.home")
                   : lastRun[id] > 0 ? Loc.Get("ui.atlas.run", lastRun[id]) : "",
                key = id, clickable = true,
            });
        }
    }

    private void BuildSystem(IList<AtlasEntry> all, string systemId)
    {
        // Entries per body; unidentified ones counted apart.
        var perBody = new Dictionary<string, List<AtlasEntry>>();
        int unidentified = 0; float unidValue = 0f;
        for (int i = 0; i < all.Count; i++)
        {
            AtlasEntry e = all[i];
            if (e.systemId != systemId) continue;
            if (string.IsNullOrEmpty(e.bodyName)) { unidentified++; unidValue += e.value; continue; }
            if (!perBody.TryGetValue(e.bodyName, out var list)) perBody[e.bodyName] = list = new List<AtlasEntry>();
            list.Add(e);
        }

        SystemData data = GetSystem(systemId);
        if (data != null) AddTree(data, perBody);
        else
            foreach (var kv in perBody) _items.Add(BodyItem(kv.Key, 0, null, kv.Value));

        if (unidentified > 0)
            _items.Add(new Item
            {
                c0 = Loc.Get("ui.atlas.unidentified"), c2 = unidentified.ToString(),
                c3 = Loc.Get("ui.data.value", unidValue), key = "", clickable = true, dim = true,
            });
    }

    /// <summary>Depth-first over the system's nodes (parents first), showing named bodies and the ancestors
    /// needed to place them. Barycenters are skipped (their children move up a level).</summary>
    private void AddTree(SystemData data, Dictionary<string, List<AtlasEntry>> perBody)
    {
        int n = data.nodes.Count;
        var children = new List<int>[n];
        for (int i = 0; i < n; i++) children[i] = new List<int>();
        var roots = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int p = data.nodes[i].parent;
            if (p >= 0) children[p].Add(i); else roots.Add(i);
        }
        var needed = new bool[n];
        var byName = new HashSet<string>(perBody.Keys);
        for (int i = 0; i < n; i++)
        {
            if (!byName.Contains(data.nodes[i].name)) continue;
            for (int a = i; a >= 0 && !needed[a]; a = data.nodes[a].parent) needed[a] = true;
        }
        var placed = new HashSet<string>();
        foreach (int r in roots) Walk(data, r, 0, children, needed, perBody, placed);

        // Anything named that the regenerated system doesn't contain (shouldn't happen): list it flat.
        foreach (var kv in perBody)
            if (!placed.Contains(kv.Key)) _items.Add(BodyItem(kv.Key, 0, null, kv.Value));
    }

    private void Walk(SystemData data, int i, int depth, List<int>[] children, bool[] needed,
                      Dictionary<string, List<AtlasEntry>> perBody, HashSet<string> placed)
    {
        if (!needed[i]) return;
        NodeData node = data.nodes[i];
        int childDepth = depth;
        if (node.kind != NodeKind.Barycenter)
        {
            perBody.TryGetValue(node.name, out var list);
            _items.Add(BodyItem(node.name, depth, node, list));
            placed.Add(node.name);
            childDepth = depth + 1;
        }
        foreach (int c in children[i]) Walk(data, c, childDepth, children, needed, perBody, placed);
    }

    private static Item BodyItem(string name, int depth, NodeData node, List<AtlasEntry> entries)
    {
        var it = new Item { c0 = new string(' ', depth * 3) + ShortName(name), key = name };
        if (entries == null || entries.Count == 0)
        {
            it.dim = true; it.clickable = false; // an ancestor shown only to place its moons
            it.c4 = Loc.Get("ui.atlas.notsurveyed");
            return it;
        }
        it.clickable = true;
        it.c1 = node != null ? KindOf(node) : "";
        int count = 0; float value = 0f; bool catalogued = false, caught = false; int run = 0;
        foreach (AtlasEntry e in entries)
        {
            if (e.catalogued) catalogued = true; else count++;
            value += e.value;
            if (e.caught && !e.corrected) caught = true;
            if (e.runNumber > run) run = e.runNumber;
        }
        it.c2 = count.ToString();
        it.c3 = Loc.Get("ui.data.value", value);
        it.warn = caught;
        it.c4 = caught ? Loc.Get("ui.atlas.wrong")
              : catalogued ? Loc.Get("ui.atlas.catalogued")
              : Loc.Get("ui.atlas.run", run);
        return it;
    }

    /// <summary>Drops the system id prefix a generated body name carries ("XX-1-23-45678 A-3" -> "A-3").</summary>
    private static string ShortName(string name)
    {
        int sp = name.IndexOf(' ');
        return sp > 0 && SystemFactory.IsValidSystemID(name.Substring(0, sp)) ? name.Substring(sp + 1) : name;
    }

    private static string KindOf(NodeData n)
    {
        if (n.kind == NodeKind.Star) return n.bodyType;
        return string.IsNullOrEmpty(n.surfaceClass) ? n.bodyType : n.surfaceClass;
    }

    private string BuildBody(IList<AtlasEntry> all, string systemId, string bodyName)
    {
        bool catalogued = false;
        AtlasEntry bestFix = null; // most recent entry under this body with a usable position fix
        for (int i = 0; i < all.Count; i++)
        {
            AtlasEntry e = all[i];
            if (e.systemId != systemId || e.bodyName != bodyName) continue;
            if (e.catalogued) { catalogued = true; continue; }
            _items.Add(EntryItem(e));
            if (e.recordedRange.valid && (bestFix == null || e.recordedTime > bestFix.recordedTime)) bestFix = e;
        }
        if (_items.Count == 0) _items.Add(new Item { c0 = Loc.Get("ui.atlas.nosurvey"), dim = true });

        SystemData data = GetSystem(systemId);
        NodeData node = null;
        int nodeIndex = -1;
        if (data != null)
            for (int i = 0; i < data.nodes.Count; i++)
                if (data.nodes[i].name == bodyName) { node = data.nodes[i]; nodeIndex = i; break; }

        // RECALL target for this body page: a catalogued body with a known orbit gets a precise recall (exact
        // position, per GhostContact.FromCatalogued); otherwise the freshest survey fix, if any, gets a rough
        // coasted-forward recall (GhostContact.FromEntry) - see AtlasPanel's own RECALL handler.
        if (catalogued && node != null && node.hasOrbit) { _recallData = data; _recallNodeIndex = nodeIndex; _recallEntry = null; }
        else if (bestFix != null) { _recallData = null; _recallNodeIndex = -1; _recallEntry = bestFix; }
        else { _recallData = null; _recallNodeIndex = -1; _recallEntry = null; }
        _recallName = ShortName(bodyName);

        return node != null ? Details(data, node, catalogued) : bodyName;
    }

    private void BuildUnidentified(IList<AtlasEntry> all, string systemId)
    {
        for (int i = 0; i < all.Count; i++)
        {
            AtlasEntry e = all[i];
            if (e.systemId == systemId && string.IsNullOrEmpty(e.bodyName)) _items.Add(EntryItem(e));
        }
    }

    private static Item EntryItem(AtlasEntry e)
    {
        return new Item
        {
            c0 = e.label,
            c1 = e.kind == DataKind.Stub ? Loc.Get("ui.data.kind.stub") : Loc.Get("ui.data.kind.raw"),
            c2 = e.runNumber.ToString(),
            c3 = Loc.Get("ui.data.value", e.value),
            c4 = e.corrected ? Loc.Get("ui.atlas.corrected")
               : e.caught ? Loc.Get("ui.atlas.wrong")
               : Loc.Get(e.confidence == Confidence.Confirmed ? "ui.atlas.conf.c" : "ui.atlas.conf.t"),
            warn = e.caught && !e.corrected,
        };
    }

    /// <summary>What is known about a body. Identified bodies: what the spectrometer measured (type, class,
    /// temperatures, composition, atmosphere). Catalogued (home) bodies: the full catalogue, orbit included.</summary>
    private static string Details(SystemData data, NodeData n, bool catalogued)
    {
        var sb = new StringBuilder();
        sb.Append("<b>").Append(n.name).Append("</b>   ");
        if (n.kind == NodeKind.Star)
        {
            sb.Append(Loc.Get("ui.atlas.d.star", n.bodyType, n.temperature, n.metallicity, n.ageGyr));
        }
        else
        {
            sb.Append(Loc.Get("ui.atlas.d.body", KindOf(n), n.temperature, n.surfaceTemperature));
            if (n.parent >= 0 && data.nodes[n.parent].kind == NodeKind.Planet)
                sb.Append("   ").Append(Loc.Get("ui.atlas.d.moonof", data.nodes[n.parent].name));
        }
        if (catalogued)
        {
            sb.Append('\n').Append(Loc.Get("ui.atlas.d.phys", n.mass / GameConstants.EARTH_MASS_SOLAR, n.radiusSol * 109.08f, n.albedo));
            if (n.hasOrbit)
                sb.Append('\n').Append(Loc.Get("ui.atlas.d.orbit",
                    Loc.Distance(n.orbit.semiMajorAxis / GameConstants.GAME_UNITS_PER_UA),
                    n.orbit.eccentricity, n.orbit.inclination, n.orbit.orbitalPeriod / 86400.0));
        }
        if (n.composition != null && n.composition.Count > 0)
            sb.Append('\n').Append(Loc.Get("ui.atlas.d.comp", Top(n.composition, 6)));
        if (n.atmosphere != null && n.kind != NodeKind.Star)
        {
            Atmosphere a = n.atmosphere;
            if (a.isEnvelope) sb.Append('\n').Append(Loc.Get("ui.atlas.d.envelope", Top(a.composition, 5)));
            else if (a.surfacePressureAtm > 0f && a.composition.Count > 0)
                sb.Append('\n').Append(Loc.Get("ui.atlas.d.atmo", a.surfacePressureAtm, Top(a.composition, 5)));
            else sb.Append('\n').Append(Loc.Get("ui.atlas.d.airless"));
        }
        return sb.ToString();
    }

    private static string Top(List<ChemicalComposition> list, int max)
    {
        var sorted = new List<ChemicalComposition>(list);
        sorted.Sort((x, y) => y.percentage.CompareTo(x.percentage));
        var sb = new StringBuilder();
        for (int i = 0; i < sorted.Count && i < max; i++)
        {
            if (i > 0) sb.Append("  ");
            sb.Append(sorted[i].element).Append(' ').Append(sorted[i].percentage.ToString("G3")).Append('%');
        }
        return sb.ToString();
    }

    private SystemData GetSystem(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_systems.TryGetValue(id, out SystemData d)) return d;
        d = SystemFactory.IsValidSystemID(id) ? SystemFactory.Generate(Game.State.WorldSeed, id) : null;
        _systems[id] = d;
        return d;
    }
    #endregion
}
