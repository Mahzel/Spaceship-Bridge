using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Owns the overlay canvas for the always-present game UI (status bar, debrief). Created by Game.Boot,
/// so no scene setup is needed. Screens live under one Canvas Scaler (1920x1080 reference).
/// </summary>
public class GameUI : MonoBehaviour
{
    // Recreated by RebuildUI (theme / text size change): screens capture colors and sizes when built.
    private StatusBar _status;
    private GameShell _shell; // sidebar + track strip + Sensors/Navigation/Comms/Systems/Atlas (UI shell rework)
    private DebriefScreen _debrief;
    private RefitScreen _refit;
    private MenuUI _menu;
    private TMPro.TextMeshProUGUI _toast;
    private int _toastSerial;
    private float _toastUntil;
    private float _nextRefresh;

    private GameObject _canvasGO;
    private CanvasScaler _scaler;
    private readonly MenuState _menuState = new MenuState(); // survives rebuilds
    private ThemePreset _builtTheme;
    private TextSize _builtText;
    private bool _pausedByFocusLoss;

    private void Awake()
    {
        BuildUI();
        Settings.Changed += OnSettingsChanged;
        if (Game.Run != null) Game.Run.Changed += OnRunChanged;
    }

    private void BuildUI()
    {
        _builtTheme = Settings.Data.theme;
        _builtText = Settings.Data.textSize;

        _status = new StatusBar();
        _shell = new GameShell();
        _debrief = new DebriefScreen();
        _refit = new RefitScreen();
        _menu = new MenuUI(_menuState);

        var canvasGO = new GameObject("GameCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        _canvasGO = canvasGO;
        _scaler = canvasGO.GetComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        _scaler.matchWidthOrHeight = 0.5f;
        ApplyScale();

        _status.Build(canvasGO.transform);
        _shell.Build(canvasGO.transform);
        BuildToast(canvasGO.transform);
        _debrief.Build(canvasGO.transform);
        _refit.Build(canvasGO.transform);
        _menu.Build(canvasGO.transform); // last = drawn on top
    }

    /// <summary>Tears the whole canvas down and builds it again with the current theme clone. Sensor processing
    /// lives outside the screens, so tracks, pings and the waterfall history are unaffected; only the pixels
    /// are redrawn.</summary>
    private void RebuildUI()
    {
        if (_shell != null) _shell.Shutdown();
        if (_refit != null) _refit.Shutdown();
        if (_canvasGO != null) Destroy(_canvasGO);
        UITheme.Rebuild();
        BuildUI();
        Resources.UnloadUnusedAssets();
        OnRunChanged();
    }

    private void ApplyScale()
    {
        if (_scaler != null) _scaler.referenceResolution = new Vector2(1920f, 1080f) / Settings.Data.uiScale;
    }

    private void OnSettingsChanged()
    {
        ApplyScale();
        if (Settings.Data.theme != _builtTheme || Settings.Data.textSize != _builtText) RebuildUI();
    }

    private void Start()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
            Debug.LogWarning("[GameUI] No EventSystem in the scene: the buttons will not react. Add one via GameObject > UI > Event System.");
        OnRunChanged();
    }

    private void OnDestroy()
    {
        if (Game.Run != null) Game.Run.Changed -= OnRunChanged;
        Settings.Changed -= OnSettingsChanged;
        InputMap.Blocked = false;
    }

    private void OnRunChanged()
    {
        RunController run = Game.Run;
        bool idle = run == null || run.Phase == RunPhase.Idle;
        // No run: the main menu is the only thing to do. A run just started: close whatever menu was up.
        if (idle && !_menuState.Open) _menuState.view = MenuState.View.Main;
        else if (!idle && (_menuState.view == MenuState.View.Main || _menuState.view == MenuState.View.NewGame
                           || _menuState.view == MenuState.View.Settings && _menuState.backFromSettings == MenuState.View.Main))
            _menuState.view = MenuState.View.None;
        _menu.Refresh();

        _debrief.Refresh();
        _refit.Refresh();
        _status.Refresh();
        _shell.RefreshFast(0f);
        _shell.RefreshSlow();
    }

    private void BuildToast(Transform parent)
    {
        UITheme t = UITheme.Current;
        _toast = UIKit.AddLabel(parent, "", t.fontSizeBody, t.warning, TMPro.TextAlignmentOptions.Center);
        RectTransform rt = _toast.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -(t.statusBarHeight + 8f));
        rt.sizeDelta = new Vector2(900f, 34f);
        _toast.gameObject.SetActive(false);
    }

    private void UpdateToast()
    {
        if (Game.Wake == null || _toast == null) return;
        if (Game.Wake.MessageSerial != _toastSerial)
        {
            _toastSerial = Game.Wake.MessageSerial;
            UIKit.SetText(_toast, Game.Wake.LastMessage);
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 8f;
        }
        else if (_toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
            _toast.gameObject.SetActive(false);
    }

    private void UpdateHotkeys()
    {
        RunController run = Game.Run;
        bool inRun = run != null && run.Phase != RunPhase.Idle;

        if (InputMap.MenuPressedRaw() && !_menu.Capturing)
        {
            if (!_menuState.Open) { if (inRun) _menu.OpenPause(); }
            else _menu.Back();
        }

        GameClock clock = Game.Clock;
        if (clock == null || run == null || run.Phase != RunPhase.Flight) return;
        if (InputMap.Pressed(GameAction.Pause)) clock.SetPaused(!clock.Paused);
        if (InputMap.Pressed(GameAction.WarpUp)) clock.StepWarp(+1);
        if (InputMap.Pressed(GameAction.WarpDown)) clock.StepWarp(-1);
    }

    /// <summary>uGUI keeps the last clicked button selected, and Submit (Space/Enter) re-clicks it. Keys are
    /// ours: drop the selection unless it's a text field being typed in.</summary>
    private static void ClearButtonFocus()
    {
        EventSystem es = EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null) return;
        if (es.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null) return;
        es.SetSelectedGameObject(null);
    }

    private void OnApplicationFocus(bool focused)
    {
        GameClock clock = Game.Clock;
        if (clock == null) return;
        if (!focused)
        {
            bool flying = Game.Run != null && Game.Run.Phase == RunPhase.Flight;
            if (Settings.Data.pauseOnFocusLoss && flying && !clock.Paused && !_menuState.Open)
            {
                clock.SetPaused(true);
                _pausedByFocusLoss = true;
            }
        }
        else if (_pausedByFocusLoss)
        {
            _pausedByFocusLoss = false;
            if (clock.Paused && !_menuState.Open) clock.SetPaused(false);
        }
    }

    private void Update()
    {
        ClearButtonFocus();
        UpdateHotkeys();
        _menu.Refresh();
        UpdateToast();

        // Sensor screens tick every frame (scan rows, DSP redraws) like RunDriver ticks WaterfallProcessor —
        // a hidden mode still needs a smooth accumulator, not 100ms chunks. NavScreen (inside Navigation)
        // self-throttles its own redraw, but still needs an every-frame check for that and its gating.
        _shell.RefreshFast(Time.unscaledDeltaTime);
        _refit.Refresh(); // cheap unless the loadout changed: keeps the < > clicks instant

        // The clock and power move every frame; 10 Hz is plenty for the text.
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.1f;
        _status.Refresh();
        _shell.RefreshSlow();
    }
}
