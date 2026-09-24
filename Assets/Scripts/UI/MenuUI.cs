using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Which menu page is showing. Kept outside MenuUI (in GameUI) so a UI rebuild after a theme or
/// text-size change reopens the same page.</summary>
public sealed class MenuState
{
    public enum View { None, Main, NewGame, Pause, Settings, Confirm, SaveGame, LoadGame }
    public enum Tab { Display, Time, Controls }

    public View view = View.None;
    public View backFromSettings = View.Main;   // where BACK leads from Settings / New Game
    public View backFromConfirm = View.Pause;
    public Tab tab = Tab.Display;
    public string confirmKey;                   // Loc key of the question
    public Action confirmYes;
    public string confirmArg;                   // {0} in the confirm question (a slot name)
    public bool pausedBeforeMenu;               // clock state to restore when the pause menu closes
    public string seedText;                     // New Game seed field, kept across rebuilds
    public string saveName;                     // Save page name field
    public View backFromSlots = View.Main;      // where BACK leads from Save / Load
    public int slotPage;
    public string slotMessage;                  // last save/load result, shown on the slot pages

    public bool Open { get { return view != View.None; } }
}

/// <summary>
/// Full-screen menus, code-built like everything else: main menu (shown whenever no run is active), pause menu
/// (Menu key during a run), New Game (seed), Settings (Display / Time / Controls, applied and saved as you
/// change them) and a yes/no confirmation. While open, gameplay input is blocked (InputMap.Blocked).
/// GameUI owns this, decides when it shows, and forwards the Menu key (Back()).
/// </summary>
public sealed class MenuUI
{
    private readonly MenuState _s;
    private GameObject _root;
    private readonly Dictionary<MenuState.View, GameObject> _pages = new Dictionary<MenuState.View, GameObject>();

    // New Game
    private TMP_InputField _seedField;
    private TextMeshProUGUI _newGameWarning;
    // Confirm
    private TextMeshProUGUI _confirmText;
    // Main
    private TextMeshProUGUI _lastSeed;
    // Settings
    private readonly Dictionary<MenuState.Tab, GameObject> _tabs = new Dictionary<MenuState.Tab, GameObject>();
    private readonly Dictionary<MenuState.Tab, Button> _tabButtons = new Dictionary<MenuState.Tab, Button>();
    private TextMeshProUGUI _scaleLabel, _warpLabel;
    private Button[] _textButtons, _themeButtons;
    private Button _startPausedBtn, _focusPauseBtn;
    private TextMeshProUGUI _startPausedLbl, _focusPauseLbl;
    private readonly List<(GameAction action, int slot, Button button, TextMeshProUGUI label)> _bindButtons =
        new List<(GameAction, int, Button, TextMeshProUGUI)>();
    private TextMeshProUGUI _bindHint;

    // Key capture (Controls tab)
    public bool Capturing { get; private set; }
    private GameAction _capAction;
    private int _capSlot;

    public MenuUI(MenuState state) { _s = state; }

    // ---------------------------------------------------------------------------------------------------------
    #region Build
    public void Build(Transform parent)
    {
        UITheme t = UITheme.Current;
        Image dim = UIKit.AddPanel(parent, "Menu", new Color(0f, 0f, 0f, 0.82f));
        dim.raycastTarget = true; // blocks everything underneath
        UIKit.Stretch(dim.rectTransform);
        _root = dim.gameObject;

        _pages[MenuState.View.Main]     = BuildMain(dim.transform);
        _pages[MenuState.View.NewGame]  = BuildNewGame(dim.transform);
        _pages[MenuState.View.Pause]    = BuildPause(dim.transform);
        _pages[MenuState.View.Settings] = BuildSettings(dim.transform);
        _pages[MenuState.View.Confirm]  = BuildConfirm(dim.transform);
        _pages[MenuState.View.SaveGame] = BuildSlots(dim.transform, true);
        _pages[MenuState.View.LoadGame] = BuildSlots(dim.transform, false);

        _root.SetActive(false);
    }

    private RectTransform Page(Transform parent, string name, float width)
    {
        UITheme t = UITheme.Current;
        Image panel = UIKit.AddPanel(parent, name, t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(width, 0f);
        var v = UIKit.VStack(prt, t.spacing, (int)(t.padding * 2f));
        v.childAlignment = TextAnchor.UpperCenter;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return prt;
    }

    private static Button Big(Transform parent, string key, UnityEngine.Events.UnityAction onClick)
    {
        return UIKit.AddButton(parent, Loc.Get(key), onClick, 0f, 48f);
    }

    private GameObject BuildMain(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform p = Page(parent, "Main", 520f);
        UIKit.AddLabel(p, Loc.Get("menu.title"), t.fontSizeTitle, t.accent, TextAlignmentOptions.Center);
        UIKit.AddLabel(p, Loc.Get("menu.subtitle"), t.fontSizeSmall, t.textDim, TextAlignmentOptions.Center);
        UIKit.AddSpacer(p, 12f);
        _continue = Big(p, "menu.continue", ContinueLatest);
        Big(p, "menu.newgame", () => OpenNewGame(MenuState.View.Main));
        Big(p, "menu.load", () => OpenSlots(MenuState.View.LoadGame, MenuState.View.Main));
        Big(p, "menu.settings", () => OpenSettings(MenuState.View.Main));
        Big(p, "menu.quit", Game.Quit);
        UIKit.AddSpacer(p, 6f);
        _lastSeed = UIKit.AddLabel(p, "", t.fontSizeSmall, t.textDim, TextAlignmentOptions.Center);
        return p.gameObject;
    }

    private GameObject BuildNewGame(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform p = Page(parent, "NewGame", 560f);
        UIKit.AddLabel(p, Loc.Get("menu.newgame"), t.fontSizeTitle, t.accent, TextAlignmentOptions.Center);
        UIKit.AddLabel(p, Loc.Get("menu.seed.hint"), t.fontSizeSmall, t.textDim, TextAlignmentOptions.Center);

        RectTransform row = UIKit.Node("SeedRow", p);
        var h = UIKit.HStack(row, 8f, 0);
        h.childAlignment = TextAnchor.MiddleCenter;
        UIKit.AddLabel(row, Loc.Get("menu.seed"), t.fontSizeBody, t.textDim);
        _seedField = UIKit.AddInputField(row, "0", 10, v => _s.seedText = v, 220f, 40f);
        _seedField.contentType = TMP_InputField.ContentType.IntegerNumber;
        _seedField.onValueChanged.AddListener(v => _s.seedText = v);
        UIKit.AddButton(row, Loc.Get("menu.seed.random"), () =>
        {
            _s.seedText = UnityEngine.Random.Range(1, 1000000).ToString();
            _seedField.SetTextWithoutNotify(_s.seedText);
        }, 110f, 40f);

        _newGameWarning = UIKit.AddLabel(p, Loc.Get("menu.newgame.warn"), t.fontSizeSmall, t.warning, TextAlignmentOptions.Center);
        UIKit.AddSpacer(p, 8f);
        Big(p, "menu.start", StartNewGame);
        Big(p, "menu.back", Back);
        return p.gameObject;
    }

    private GameObject BuildPause(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform p = Page(parent, "Pause", 460f);
        UIKit.AddLabel(p, Loc.Get("menu.paused"), t.fontSizeTitle, t.accent, TextAlignmentOptions.Center);
        UIKit.AddSpacer(p, 8f);
        Big(p, "menu.resume", Close);
        Big(p, "menu.save", () => OpenSlots(MenuState.View.SaveGame, MenuState.View.Pause));
        Big(p, "menu.load", () => OpenSlots(MenuState.View.LoadGame, MenuState.View.Pause));
        Big(p, "menu.settings", () => OpenSettings(MenuState.View.Pause));
        Big(p, "menu.newgame", () => OpenNewGame(MenuState.View.Pause));
        Big(p, "menu.mainmenu", () => Confirm("menu.confirm.abandon", () => { Game.ReturnToMainMenu(); _s.view = MenuState.View.Main; }));
        Big(p, "menu.quit", () => Confirm("menu.confirm.quit", Game.Quit));
        return p.gameObject;
    }

    private GameObject BuildConfirm(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform p = Page(parent, "Confirm", 520f);
        _confirmText = UIKit.AddLabel(p, "", t.fontSizeBody, t.warning, TextAlignmentOptions.Center);
        _confirmText.textWrappingMode = TextWrappingModes.Normal;
        RectTransform row = UIKit.Node("Buttons", p);
        UIKit.HStack(row, 12f, 0, expandWidth: true);
        UIKit.AddButton(row, Loc.Get("menu.yes"), () =>
        {
            Action yes = _s.confirmYes;
            _s.view = _s.backFromConfirm;
            _s.confirmYes = null;
            if (yes != null) yes();
        }, 0f, 48f);
        UIKit.AddButton(row, Loc.Get("menu.no"), Back, 0f, 48f);
        return p.gameObject;
    }

    private GameObject BuildSettings(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform p = Page(parent, "Settings", 760f);
        UIKit.AddLabel(p, Loc.Get("menu.settings"), t.fontSizeTitle, t.accent, TextAlignmentOptions.Center);

        RectTransform tabs = UIKit.Node("Tabs", p);
        UIKit.HStack(tabs, 4f, 0, expandWidth: true);
        foreach (MenuState.Tab tab in Enum.GetValues(typeof(MenuState.Tab)))
        {
            MenuState.Tab captured = tab;
            _tabButtons[tab] = UIKit.AddButton(tabs, Loc.Get("menu.tab." + tab.ToString().ToLowerInvariant()),
                                               () => { StopCapture(); _s.tab = captured; }, 0f, 38f);
        }

        _tabs[MenuState.Tab.Display]  = BuildDisplayTab(p);
        _tabs[MenuState.Tab.Time]     = BuildTimeTab(p);
        _tabs[MenuState.Tab.Controls] = BuildControlsTab(p);

        UIKit.AddSpacer(p, 6f);
        Big(p, "menu.back", Back);
        return p.gameObject;
    }

    private RectTransform Row(Transform parent, string labelKey)
    {
        UITheme t = UITheme.Current;
        RectTransform row = UIKit.Node("Row", parent);
        var h = UIKit.HStack(row, 8f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;
        TextMeshProUGUI l = UIKit.AddLabel(row, Loc.Get(labelKey), t.fontSizeBody, t.textDim);
        UIKit.Size(l.rectTransform, preferredWidth: 240f);
        return row;
    }

    private GameObject BuildDisplayTab(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform tab = UIKit.Node("Display", parent);
        UIKit.VStack(tab, t.spacing, 0);

        RectTransform scale = Row(tab, "set.uiscale");
        UIKit.AddButton(scale, "-", () => EditScale(-0.05f), 36f, 36f);
        _scaleLabel = UIKit.AddLabel(scale, "", t.fontSizeBody, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_scaleLabel.rectTransform, preferredWidth: 90f);
        UIKit.AddButton(scale, "+", () => EditScale(0.05f), 36f, 36f);

        RectTransform text = Row(tab, "set.textsize");
        _textButtons = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            TextSize ts = (TextSize)i;
            _textButtons[i] = UIKit.AddButton(text, Loc.Get("set.text." + ts.ToString().ToLowerInvariant()),
                                              () => { Settings.Data.textSize = ts; Settings.Save(); }, 100f, 36f);
        }

        RectTransform theme = Row(tab, "set.theme");
        var names = Enum.GetValues(typeof(ThemePreset));
        _themeButtons = new Button[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            ThemePreset tp = (ThemePreset)i;
            _themeButtons[i] = UIKit.AddButton(theme, Loc.Get("set.theme." + tp.ToString().ToLowerInvariant()),
                                               () => { Settings.Data.theme = tp; Settings.Save(); }, 104f, 36f);
            TextMeshProUGUI lbl = _themeButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.fontSize = t.fontSizeSmall;
        }
        UIKit.AddLabel(tab, Loc.Get("set.display.hint"), t.fontSizeSmall, t.textDim);
        return tab.gameObject;
    }

    private GameObject BuildTimeTab(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform tab = UIKit.Node("Time", parent);
        UIKit.VStack(tab, t.spacing, 0);

        RectTransform warp = Row(tab, "set.defaultwarp");
        UIKit.AddButton(warp, "-", () => { Settings.Data.defaultWarp--; Settings.Save(); }, 36f, 36f);
        _warpLabel = UIKit.AddLabel(warp, "", t.fontSizeBody, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_warpLabel.rectTransform, preferredWidth: 90f);
        UIKit.AddButton(warp, "+", () => { Settings.Data.defaultWarp++; Settings.Save(); }, 36f, 36f);

        RectTransform sp = Row(tab, "set.startpaused");
        _startPausedBtn = UIKit.AddButton(sp, "", () => { Settings.Data.startPaused = !Settings.Data.startPaused; Settings.Save(); }, 100f, 36f);
        _startPausedLbl = _startPausedBtn.GetComponentInChildren<TextMeshProUGUI>();

        RectTransform fp = Row(tab, "set.focuspause");
        _focusPauseBtn = UIKit.AddButton(fp, "", () => { Settings.Data.pauseOnFocusLoss = !Settings.Data.pauseOnFocusLoss; Settings.Save(); }, 100f, 36f);
        _focusPauseLbl = _focusPauseBtn.GetComponentInChildren<TextMeshProUGUI>();

        UIKit.AddLabel(tab, Loc.Get("set.time.hint"), t.fontSizeSmall, t.textDim);
        return tab.gameObject;
    }

    private GameObject BuildControlsTab(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform tab = UIKit.Node("Controls", parent);
        UIKit.VStack(tab, 4f, 0);

        foreach (GameAction a in Enum.GetValues(typeof(GameAction)))
        {
            RectTransform row = Row(tab, "act." + a.ToString().ToLowerInvariant());
            for (int slot = 0; slot < Settings.Slots; slot++)
            {
                GameAction ca = a; int cs = slot;
                Button b = UIKit.AddButton(row, "", () => BeginCapture(ca, cs), 190f, 32f);
                TextMeshProUGUI lbl = b.GetComponentInChildren<TextMeshProUGUI>();
                if (lbl != null) lbl.fontSize = t.fontSizeSmall;
                _bindButtons.Add((a, slot, b, lbl));
            }
        }
        _bindHint = UIKit.AddLabel(tab, "", t.fontSizeSmall, t.textDim);
        UIKit.AddButton(tab, Loc.Get("set.resetkeys"), () =>
        {
            StopCapture();
            Settings.Data.bindings = Settings.DefaultBindings();
            Settings.Save();
        }, 0f, 36f);
        return tab.gameObject;
    }
    #endregion

    // ---------------------------------------------------------------------------------------------------------
    #region Navigation
    public void OpenMain() { _s.view = MenuState.View.Main; }

    public void OpenPause()
    {
        if (Game.Clock != null)
        {
            _s.pausedBeforeMenu = Game.Clock.Paused;
            Game.Clock.SetPaused(true);
        }
        _s.view = MenuState.View.Pause;
    }

    /// <summary>Closes the pause menu and puts the clock back the way it was.</summary>
    public void Close()
    {
        StopCapture();
        bool wasPause = _s.view != MenuState.View.None;
        _s.view = MenuState.View.None;
        if (wasPause && Game.Clock != null && Game.Run != null && Game.Run.Phase == RunPhase.Flight)
            Game.Clock.SetPaused(_s.pausedBeforeMenu);
    }

    /// <summary>Menu key / BACK: one level up. From the pause menu it resumes; the main menu stays.</summary>
    public void Back()
    {
        if (Capturing) { StopCapture(); return; }
        switch (_s.view)
        {
            case MenuState.View.Settings: _s.view = _s.backFromSettings; break;
            case MenuState.View.NewGame:  _s.view = _s.backFromSettings; break;
            case MenuState.View.SaveGame:
            case MenuState.View.LoadGame: _s.view = _s.backFromSlots; break;
            case MenuState.View.Confirm:  _s.view = _s.backFromConfirm; _s.confirmYes = null; break;
            case MenuState.View.Pause:    Close(); break;
        }
    }

    private void OpenSettings(MenuState.View from) { _s.backFromSettings = from; _s.view = MenuState.View.Settings; }

    private void OpenNewGame(MenuState.View from)
    {
        _s.backFromSettings = from;
        if (string.IsNullOrEmpty(_s.seedText))
            _s.seedText = (Game.State != null ? Game.State.WorldSeed : GameConstants.DEFAULT_BASE_SEED).ToString();
        _s.view = MenuState.View.NewGame;
    }

    private void Confirm(string questionKey, Action yes)
    {
        _s.backFromConfirm = _s.view;
        _s.confirmKey = questionKey;
        _s.confirmYes = yes;
        _s.view = MenuState.View.Confirm;
    }

    private void StartNewGame()
    {
        int seed;
        if (!int.TryParse(_s.seedText, out seed)) seed = GameConstants.DEFAULT_BASE_SEED;
        _s.view = MenuState.View.None;
        Game.NewGame(seed);
    }

    private void EditScale(float d)
    {
        Settings.Data.uiScale = Mathf.Round((Settings.Data.uiScale + d) * 20f) / 20f;
        Settings.Save();
    }
    #endregion

    // ---------------------------------------------------------------------------------------------------------
    #region Save / load pages
    private const int SlotRows = 8;
    private Button _continue;
    private SaveMeta _latest;
    private float _latestCheckedAt = -100f;
    private List<SaveMeta> _slots = new List<SaveMeta>();

    private sealed class SlotRow { public GameObject go; public Button pick, delete; public TextMeshProUGUI label; }
    private readonly Dictionary<bool, List<SlotRow>> _slotRows = new Dictionary<bool, List<SlotRow>>();
    private readonly Dictionary<bool, TextMeshProUGUI> _slotMsg = new Dictionary<bool, TextMeshProUGUI>();
    private readonly Dictionary<bool, TextMeshProUGUI> _slotPageLbl = new Dictionary<bool, TextMeshProUGUI>();
    private TMP_InputField _saveNameField;

    private GameObject BuildSlots(Transform parent, bool saving)
    {
        UITheme t = UITheme.Current;
        RectTransform p = Page(parent, saving ? "SaveGame" : "LoadGame", 720f);
        UIKit.AddLabel(p, Loc.Get(saving ? "menu.save" : "menu.load"), t.fontSizeTitle, t.accent, TextAlignmentOptions.Center);

        if (saving)
        {
            RectTransform row = UIKit.Node("NameRow", p);
            UIKit.HStack(row, 8f, 0).childAlignment = TextAnchor.MiddleCenter;
            UIKit.AddLabel(row, Loc.Get("menu.slotname"), t.fontSizeBody, t.textDim);
            _saveNameField = UIKit.AddInputField(row, Loc.Get("menu.slotname.hint"), 40, v => _s.saveName = v, 360f, 40f);
            _saveNameField.onValueChanged.AddListener(v => _s.saveName = v);
            UIKit.AddButton(row, Loc.Get("menu.save.do"), SaveNow, 110f, 40f);
        }

        var rows = new List<SlotRow>();
        for (int i = 0; i < SlotRows; i++)
        {
            int index = i;
            RectTransform rt = UIKit.Node("Slot", p);
            UIKit.HStack(rt, 6f, 0, expandWidth: false).childAlignment = TextAnchor.MiddleLeft;
            var r = new SlotRow { go = rt.gameObject };
            r.pick = UIKit.AddButton(rt, "", () => OnSlotPicked(saving, index), 580f, 34f);
            r.label = r.pick.GetComponentInChildren<TextMeshProUGUI>();
            r.label.fontSize = t.fontSizeSmall;
            r.label.alignment = TextAlignmentOptions.MidlineLeft;
            r.label.margin = new Vector4(8f, 0f, 8f, 0f);
            r.delete = UIKit.AddButton(rt, Loc.Get("menu.delete"), () => OnSlotDelete(index), 90f, 34f);
            TextMeshProUGUI dl = r.delete.GetComponentInChildren<TextMeshProUGUI>();
            dl.fontSize = t.fontSizeSmall;
            rows.Add(r);
        }
        _slotRows[saving] = rows;

        RectTransform pager = UIKit.Node("Pager", p);
        UIKit.HStack(pager, 8f, 0).childAlignment = TextAnchor.MiddleCenter;
        UIKit.AddButton(pager, "<", () => { _s.slotPage = Mathf.Max(0, _s.slotPage - 1); }, 40f, 28f);
        _slotPageLbl[saving] = UIKit.AddLabel(pager, "", t.fontSizeSmall, t.textDim, TextAlignmentOptions.Center);
        UIKit.Size(_slotPageLbl[saving].rectTransform, preferredWidth: 120f);
        UIKit.AddButton(pager, ">", () => { _s.slotPage++; }, 40f, 28f);

        _slotMsg[saving] = UIKit.AddLabel(p, "", t.fontSizeSmall, t.warning, TextAlignmentOptions.Center);
        Big(p, "menu.back", Back);
        return p.gameObject;
    }

    private void OpenSlots(MenuState.View which, MenuState.View from)
    {
        _s.backFromSlots = from;
        _s.slotPage = 0;
        _s.slotMessage = null;
        _slots = SaveSystem.List();
        if (which == MenuState.View.SaveGame && string.IsNullOrEmpty(_s.saveName))
            _s.saveName = Loc.Get("menu.slotname.default", Game.State != null ? Game.State.WorldSeed : 0);
        _s.view = which;
    }

    private SaveMeta SlotAt(int rowIndex)
    {
        int i = _s.slotPage * SlotRows + rowIndex;
        return i >= 0 && i < _slots.Count ? _slots[i] : null;
    }

    private void RefreshSlots(bool saving)
    {
        if (saving && _saveNameField != null && !_saveNameField.isFocused && _saveNameField.text != (_s.saveName ?? ""))
            _saveNameField.SetTextWithoutNotify(_s.saveName ?? "");

        int pages = Mathf.Max(1, (_slots.Count + SlotRows - 1) / SlotRows);
        _s.slotPage = Mathf.Clamp(_s.slotPage, 0, pages - 1);
        UIKit.SetText(_slotPageLbl[saving], Loc.Get("ui.atlas.page", _s.slotPage + 1, pages));
        List<SlotRow> rows = _slotRows[saving];
        for (int r = 0; r < rows.Count; r++)
        {
            SaveMeta m = SlotAt(r);
            bool show = m != null || (r == 0 && _slots.Count == 0);
            if (rows[r].go.activeSelf != show) rows[r].go.SetActive(show);
            if (!show) continue;
            if (m == null)
            {
                UIKit.SetText(rows[r].label, Loc.Get("menu.noslots"));
                rows[r].pick.interactable = false;
                rows[r].delete.gameObject.SetActive(false);
                continue;
            }
            rows[r].pick.interactable = true;
            rows[r].delete.gameObject.SetActive(true);
            UIKit.SetText(rows[r].label, SlotText(m));
        }
        UIKit.SetText(_slotMsg[saving], _s.slotMessage ?? "");
    }

    private static string SlotText(SaveMeta m)
    {
        string when = m.savedAtUtc;
        if (DateTime.TryParse(m.savedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
            when = dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        string phase = m.phase == RunPhase.Debrief ? Loc.Get("menu.slot.debrief")
                     : m.phase == RunPhase.Refit ? Loc.Get("menu.slot.refit") : "";
        return Loc.Get("menu.slot", m.slotName, m.runNumber, m.day, m.systemId, when, phase);
    }

    private void OnSlotPicked(bool saving, int rowIndex)
    {
        SaveMeta m = SlotAt(rowIndex);
        if (m == null) return;
        if (saving) { _s.saveName = m.slotName; return; } // pick a slot to overwrite: fills the name
        bool inRun = Game.Run != null && Game.Run.Phase != RunPhase.Idle;
        if (inRun) Confirm("menu.confirm.load", () => LoadSlot(m.slotName));
        else LoadSlot(m.slotName);
    }

    private void OnSlotDelete(int rowIndex)
    {
        SaveMeta m = SlotAt(rowIndex);
        if (m == null) return;
        _s.confirmArg = m.slotName;
        Confirm("menu.confirm.delete", () =>
        {
            SaveSystem.Delete(m.slotName);
            _slots = SaveSystem.List();
            _latest = null;
            _s.slotMessage = Loc.Get("menu.deleted", m.slotName);
        });
    }

    private void SaveNow()
    {
        string name = (_s.saveName ?? "").Trim();
        if (name.Length == 0) { _s.slotMessage = Loc.Get("menu.slotname.empty"); return; }
        if (!Game.CanSave) { _s.slotMessage = Loc.Get("menu.save.nothing"); return; }
        if (SaveSystem.Exists(name))
        {
            _s.confirmArg = name;
            Confirm("menu.confirm.overwrite", () => DoSave(name));
        }
        else DoSave(name);
    }

    private void DoSave(string name)
    {
        bool ok = SaveSystem.Save(name, out string err);
        _slots = SaveSystem.List();
        _latest = null;
        _s.slotMessage = ok ? Loc.Get("menu.saved", name) : Loc.Get("menu.save.failed", err);
    }

    private void LoadSlot(string name)
    {
        if (SaveSystem.Load(name, out string err))
        {
            StopCapture();
            _s.view = MenuState.View.None; // the game comes back paused
            _s.pausedBeforeMenu = true;
        }
        else
        {
            _s.view = MenuState.View.LoadGame;
            _s.slotMessage = Loc.Get("menu.load.failed", err);
        }
    }

    private void ContinueLatest()
    {
        SaveMeta m = SaveSystem.Latest();
        if (m == null) return;
        _s.backFromSlots = MenuState.View.Main;
        LoadSlot(m.slotName);
        if (_s.view == MenuState.View.LoadGame) _slots = SaveSystem.List(); // failed: show the list with the error
    }
    #endregion

    // ---------------------------------------------------------------------------------------------------------
    #region Key capture
    private void BeginCapture(GameAction a, int slot)
    {
        Capturing = true;
        _capAction = a;
        _capSlot = slot;
    }

    private void StopCapture() { Capturing = false; }

    /// <summary>While capturing: Escape cancels, Backspace clears the slot, any other key is bound (and removed
    /// from wherever else it was bound, so one key never triggers two actions).</summary>
    private void UpdateCapture()
    {
        Keyboard kb = Keyboard.current;
        if (!Capturing || kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame) { StopCapture(); return; }
        if (kb.backspaceKey.wasPressedThisFrame)
        {
            InputMap.SetKey(_capAction, _capSlot, Key.None);
            StopCapture();
            Settings.Save();
            return;
        }
        foreach (var k in kb.allKeys)
        {
            if (k == null || !k.wasPressedThisFrame) continue;
            Key key = k.keyCode;
            if (key == Key.None) continue;
            foreach (GameAction a in Enum.GetValues(typeof(GameAction)))
                for (int s = 0; s < Settings.Slots; s++)
                    if (InputMap.GetKey(a, s) == key) InputMap.SetKey(a, s, Key.None);
            InputMap.SetKey(_capAction, _capSlot, key);
            StopCapture();
            Settings.Save();
            return;
        }
    }
    #endregion

    // ---------------------------------------------------------------------------------------------------------
    /// <summary>Called every frame by GameUI.</summary>
    public void Refresh()
    {
        if (_root == null) return;
        bool open = _s.Open;
        if (_root.activeSelf != open) _root.SetActive(open);
        InputMap.Blocked = open;
        if (!open) { Capturing = false; return; }

        foreach (var kv in _pages)
            if (kv.Value.activeSelf != (kv.Key == _s.view)) kv.Value.SetActive(kv.Key == _s.view);

        switch (_s.view)
        {
            case MenuState.View.Main:
                int seed = Game.State != null ? Game.State.WorldSeed : GameConstants.DEFAULT_BASE_SEED;
                UIKit.SetText(_lastSeed, Loc.Get("menu.lastseed", seed));
                if (_latest == null && Time.unscaledTime > _latestCheckedAt + 2f) { _latest = SaveSystem.Latest(); _latestCheckedAt = Time.unscaledTime; }
                if (_continue.gameObject.activeSelf != (_latest != null)) _continue.gameObject.SetActive(_latest != null);
                break;
            case MenuState.View.SaveGame:
            case MenuState.View.LoadGame:
                RefreshSlots(_s.view == MenuState.View.SaveGame);
                break;
            case MenuState.View.NewGame:
                if (!_seedField.isFocused && _seedField.text != (_s.seedText ?? ""))
                    _seedField.SetTextWithoutNotify(_s.seedText ?? "");
                bool inRun = Game.Run != null && Game.Run.Phase != RunPhase.Idle;
                if (_newGameWarning.gameObject.activeSelf != inRun) _newGameWarning.gameObject.SetActive(inRun);
                break;
            case MenuState.View.Confirm:
                UIKit.SetText(_confirmText, Loc.Get(_s.confirmKey ?? "menu.confirm.quit", _s.confirmArg ?? ""));
                break;
            case MenuState.View.Settings:
                RefreshSettings();
                break;
        }
    }

    private void RefreshSettings()
    {
        SettingsData d = Settings.Data;
        foreach (var kv in _tabs)
            if (kv.Value.activeSelf != (kv.Key == _s.tab)) kv.Value.SetActive(kv.Key == _s.tab);
        foreach (var kv in _tabButtons) UIKit.SetButtonActive(kv.Value, kv.Key == _s.tab);

        UIKit.SetText(_scaleLabel, Loc.Get("set.uiscale.value", d.uiScale * 100f));
        for (int i = 0; i < _textButtons.Length; i++) UIKit.SetButtonActive(_textButtons[i], (int)d.textSize == i);
        for (int i = 0; i < _themeButtons.Length; i++) UIKit.SetButtonActive(_themeButtons[i], (int)d.theme == i);

        UIKit.SetText(_warpLabel, GameClock.WarpLabel(d.defaultWarp));
        UIKit.SetText(_startPausedLbl, Loc.Get(d.startPaused ? "set.on" : "set.off"));
        UIKit.SetButtonActive(_startPausedBtn, d.startPaused);
        UIKit.SetText(_focusPauseLbl, Loc.Get(d.pauseOnFocusLoss ? "set.on" : "set.off"));
        UIKit.SetButtonActive(_focusPauseBtn, d.pauseOnFocusLoss);

        UpdateCapture();
        for (int i = 0; i < _bindButtons.Count; i++)
        {
            var b = _bindButtons[i];
            bool cap = Capturing && b.action == _capAction && b.slot == _capSlot;
            UIKit.SetText(b.label, cap ? Loc.Get("set.presskey") : InputMap.KeyLabel(InputMap.GetKey(b.action, b.slot)));
            UIKit.SetButtonActive(b.button, cap);
        }
        UIKit.SetText(_bindHint, Loc.Get(Capturing ? "set.capture.hint" : "set.controls.hint"));
    }
}
