using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PROBE REFIT: full-screen overlay shown while the run phase is Refit (after a debrief, and before run #1).
/// One row per system with &lt; &gt; to pick its level, what that level gives, and its cost. The summed cost must
/// stay within the budget home lends at the current trust (Loadout.Budget) for LAUNCH to work.
/// </summary>
public sealed class RefitScreen
{
    private GameObject _root;
    private TextMeshProUGUI _budget, _warning;
    private Button _launch, _back;

    private sealed class Row
    {
        public ProbeSystem system;
        public TextMeshProUGUI level, cost, detail;
        public Button down, up;
    }
    private readonly Row[] _rows = new Row[Loadout.All.Length];

    private int _shownSerial = -1;
    private int _serial;

    public void Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image dim = UIKit.AddPanel(parent, "Refit", t.dimColor);
        dim.raycastTarget = true; // blocks clicks on everything below
        UIKit.Stretch(dim.rectTransform);
        _root = dim.gameObject;

        Image panel = UIKit.AddPanel(dim.transform, "Panel", t.panelColor);
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(1040f, 0f);

        var v = UIKit.VStack(prt, t.spacing, (int)(t.padding * 2f));
        v.childAlignment = TextAnchor.UpperCenter;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        UIKit.AddLabel(prt, Loc.Get("refit.title"), t.fontSizeTitle, t.accent, TextAlignmentOptions.Center);
        UIKit.AddLabel(prt, Loc.Get("refit.hint"), t.fontSizeSmall, t.textDim, TextAlignmentOptions.Center);
        _budget = UIKit.AddLabel(prt, "", t.fontSizeBody, t.text, TextAlignmentOptions.Center);
        UIKit.AddSpacer(prt, 6f);

        UIKit.AddLabel(prt, Loc.Get("refit.sensors"), t.fontSizeSmall, t.accentDim, TextAlignmentOptions.Left);
        foreach (ProbeSystem s in Loadout.All)
        {
            if (s == ProbeSystem.Battery)
            {
                UIKit.AddSpacer(prt, 4f);
                UIKit.AddLabel(prt, Loc.Get("refit.modules"), t.fontSizeSmall, t.accentDim, TextAlignmentOptions.Left);
            }
            if (s == ProbeSystem.NavComputer)
            {
                UIKit.AddSpacer(prt, 4f);
                UIKit.AddLabel(prt, Loc.Get("refit.navigation"), t.fontSizeSmall, t.accentDim, TextAlignmentOptions.Left);
            }
            _rows[(int)s] = BuildRow(prt, s);
        }

        UIKit.AddSpacer(prt, 6f);
        _warning = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.warning, TextAlignmentOptions.Center);
        _warning.textWrappingMode = TextWrappingModes.Normal;
        UIKit.AddSpacer(prt, 6f);

        RectTransform buttons = UIKit.Node("Buttons", prt);
        UIKit.HStack(buttons, 8f, 0, expandWidth: true);
        _back = UIKit.AddButton(buttons, Loc.Get("refit.back"), () => Game.Run?.BackToDebrief(), 0f, 48f);
        UIKit.AddButton(buttons, Loc.Get("refit.baseline"), () => Game.State?.Loadout.ResetToBaseline(), 0f, 48f);
        _launch = UIKit.AddButton(buttons, Loc.Get("refit.launch"), OnLaunch, 0f, 48f);

        if (Game.State != null) Game.State.Loadout.Changed += OnLoadoutChanged;
        _root.SetActive(false);
    }

    /// <summary>Called before the canvas is destroyed (UI rebuild).</summary>
    public void Shutdown()
    {
        if (Game.State != null) Game.State.Loadout.Changed -= OnLoadoutChanged;
    }

    private void OnLoadoutChanged() => _serial++;

    private Row BuildRow(RectTransform parent, ProbeSystem s)
    {
        UITheme t = UITheme.Current;
        var r = new Row { system = s };

        RectTransform row = UIKit.Node("Row " + s, parent);
        var h = UIKit.HStack(row, 8f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;

        TextMeshProUGUI name = UIKit.AddLabel(row, Loc.Get("refit.sys." + s.ToString().ToLowerInvariant()), t.fontSizeBody, t.text);
        UIKit.Size(name.rectTransform, preferredWidth: 170f);

        r.down = UIKit.AddButton(row, "<", () => Step(s, -1), 36f, 34f);
        r.level = UIKit.AddLabel(row, "", t.fontSizeBody, t.accent, TextAlignmentOptions.Center);
        UIKit.Size(r.level.rectTransform, preferredWidth: 90f);
        r.up = UIKit.AddButton(row, ">", () => Step(s, +1), 36f, 34f);

        r.cost = UIKit.AddLabel(row, "", t.fontSizeBody, t.text, TextAlignmentOptions.Right);
        UIKit.Size(r.cost.rectTransform, preferredWidth: 60f);

        r.detail = UIKit.AddLabel(row, "", t.fontSizeSmall, t.textDim);
        UIKit.Size(r.detail.rectTransform, flexibleWidth: 1f);
        return r;
    }

    private static void Step(ProbeSystem s, int delta)
    {
        Loadout lo = Game.State?.Loadout;
        if (lo != null) lo.Set(s, lo.Level(s) + delta);
    }

    private static void OnLaunch()
    {
        GameState st = Game.State;
        if (st == null || Game.Run == null || !st.LoadoutWithinBudget) return;
        Game.Run.BeginRun();
    }

    public void Refresh()
    {
        if (_root == null) return;

        RunController run = Game.Run;
        GameState st = Game.State;
        bool show = run != null && st != null && run.Phase == RunPhase.Refit;
        if (_root.activeSelf != show)
        {
            _root.SetActive(show);
            if (show) _shownSerial = -1; // trust may have changed since the last time: redraw everything
        }
        if (!show) return;

        _back.gameObject.SetActive(run.LastSummary != null);
        if (_shownSerial == _serial) return;
        _shownSerial = _serial;

        UITheme t = UITheme.Current;
        Loadout lo = st.Loadout;
        int budget = st.LoadoutBudget, used = lo.TotalCost;
        bool ok = used <= budget;
        UIKit.SetText(_budget, Loc.Get("refit.budget", st.Trust, used, budget));
        _budget.color = ok ? t.good : t.danger;
        _launch.interactable = ok;

        foreach (Row r in _rows)
        {
            int lvl = lo.Level(r.system);
            UIKit.SetText(r.level, LevelName(r.system, lvl));
            r.level.color = lvl > 0 ? t.accent : t.textDim;
            int cost = Loadout.Cost(r.system, lvl);
            UIKit.SetText(r.cost, cost > 0 ? cost.ToString() : "-");
            UIKit.SetText(r.detail, LoadoutSpecs.Describe(r.system, lvl, st.SpecAssets, st.BaseProbe));
            r.down.interactable = lvl > Loadout.MinLevel(r.system);
            r.up.interactable = lvl < Loadout.MaxLevel;
        }

        // Warnings: over budget, and loadouts that can't do what the player probably expects.
        var sb = new System.Text.StringBuilder();
        if (!ok) sb.Append(Loc.Get("refit.over", used - budget));
        if (lo.Fitted(ProbeSystem.Spectrometer) && !lo.Fitted(ProbeSystem.Imager) && !lo.Fitted(ProbeSystem.Radar))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Loc.Get("refit.warn.spectro"));
        }
        UIKit.SetText(_warning, sb.ToString());
    }

    private static string LevelName(ProbeSystem s, int level)
    {
        if (level <= 0)
        {
            // NavComputer behaves like a sensor for labeling purposes: there's no "stock" one, the ship simply
            // has no NAV screen without it. It stays out of Loadout.IsSensor (no SensorSpec, no waterfall-style
            // free minimum) so that check alone can't be reused here.
            bool none = Loadout.IsSensor(s) || s == ProbeSystem.NavComputer;
            return Loc.Get(none ? "refit.lvl.none" : "refit.lvl.stock");
        }
        return Loc.Get("refit.lvl." + level);
    }
}
