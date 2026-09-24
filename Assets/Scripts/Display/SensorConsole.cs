using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One sensor display with a mode selector, replacing four separate scene-wired test screens
/// (Imager/Spectrometer/SystemOverviewScreen/WaterfallScreen's old ComputerScreen forms) with fully
/// code-built (UIKit) screens. Only the selected mode's content is visible, but every screen's Refresh runs
/// every frame regardless — a hidden screen doesn't go offline. Waterfall and System are passive/informational
/// and keep working (and, for Waterfall, drawing power) while hidden; Imager and Spectrometer are deliberate,
/// aimed reads and stop (Hide()) when you look away, the same way they always have.
///
/// This is a plain class, not a MonoBehaviour, matching every sub-screen it owns. Fills the content rect
/// GameShell gives it for the SENSORS major mode (UI shell rework) - Build() no longer owns its own
/// background position/size or drag handle, GameShell does.
/// </summary>
public sealed class SensorConsole
{
    private readonly WaterfallScreen _waterfall = new WaterfallScreen();
    private readonly ImagerScreen _imager = new ImagerScreen();
    private readonly SpectrometerScreen _spectrometer = new SpectrometerScreen();
    private readonly ContactsScreen _system = new ContactsScreen();
    private readonly RadarScreen _radar = new RadarScreen();

    private GameObject[] _bodies;
    private Button[] _tabs;
    private string[] _tabKeys;
    private readonly bool[] _fitted = { true, true, true, true, true };
    private GameObject _notFitted;
    private TMPro.TextMeshProUGUI _notFittedLabel;
    private int _active;
    private bool _shownOnce;

    private const string PrefKey = "ui.screen.mode";

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        // Fills whatever content rect the Sensors major mode is given (GameShell) - no longer an absolute-
        // anchored, auto-sized, draggable floating window.
        Image panel = UIKit.AddPanel(parent, "SensorConsole", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        UIKit.Stretch(prt);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;

        RectTransform tabRow = UIKit.Node("Modes", prt);
        UIKit.HStack(tabRow, 4f, 0, expandWidth: true);
        string[] keys = { "ui.screen.waterfall", "ui.screen.imager", "ui.screen.spectrometer", "ui.screen.system", "ui.screen.radar" };
        _tabs = new Button[keys.Length];
        _tabKeys = keys;
        for (int i = 0; i < keys.Length; i++)
        {
            int idx = i; // capture
            _tabs[i] = UIKit.AddButton(tabRow, Loc.Get(keys[i]), () => SetActive(idx), 0f, 34f);
        }

        _bodies = new GameObject[5];
        _bodies[0] = _waterfall.Build(prt);
        _bodies[1] = _imager.Build(prt);
        _bodies[2] = _spectrometer.Build(prt);
        _bodies[3] = _system.Build(prt);
        _bodies[4] = _radar.Build(prt);

        // Shown instead of a sensor's screen when that sensor isn't in this probe's loadout.
        RectTransform nf = UIKit.Node("NotFitted", prt);
        UIKit.Size(nf, minHeight: 200f, flexibleWidth: 1f);
        UIKit.VStack(nf, t.spacing, (int)(t.padding * 3f)).childAlignment = TextAnchor.MiddleCenter;
        _notFittedLabel = UIKit.AddLabel(nf, "", t.fontSizeBody, t.textDim, TMPro.TextAlignmentOptions.Center);
        _notFittedLabel.textWrappingMode = TMPro.TextWrappingModes.Normal;
        _notFitted = nf.gameObject;
        _notFitted.SetActive(false);
        for (int i = 0; i < _fitted.Length; i++) _fitted[i] = true;

        int start = 0;
        try { start = PlayerPrefs.GetInt(PrefKey, 0); } catch (System.Exception) { }
        SetActive(Mathf.Clamp(start, 0, _bodies.Length - 1));

        return prt.gameObject;
    }

    private void SetActive(int idx)
    {
        if (idx != _active) HideByIndex(_active);
        bool changed = idx != _active || !_shownOnce;
        _shownOnce = true;

        _active = idx;
        ShowActiveBody();
        for (int i = 0; i < _tabs.Length; i++)
            UIKit.SetButtonActive(_tabs[i], i == idx);

        try { PlayerPrefs.SetInt(PrefKey, idx); } catch (System.Exception) { }

        if (changed && idx == 2 && _fitted[2]) _spectrometer.Show(); // take the selected track as the target
    }

    private void HideByIndex(int idx)
    {
        switch (idx)
        {
            case 1: _imager.Hide(); break;
            case 2: _spectrometer.Hide(); break;
            // Waterfall and System have no-op Hide(): they keep working while off-screen.
        }
    }

    /// <summary>Before the console is destroyed (UI rebuild): stops the aimed sensors so their power loads
    /// are released, exactly as switching tabs would.</summary>
    public void Shutdown()
    {
        _imager.Hide();
        _spectrometer.Hide();
    }

    /// <summary>Cheap enough to refresh every mode even when hidden; keeps each screen self-contained and lets
    /// the passive ones (waterfall, system) keep working regardless of which tab is showing.</summary>
    public void Refresh(float unscaledDeltaSeconds)
    {
        UpdateFitted();
        _waterfall.Refresh(unscaledDeltaSeconds);
        if (_fitted[1]) _imager.Refresh(unscaledDeltaSeconds);
        if (_fitted[2]) _spectrometer.Refresh(unscaledDeltaSeconds);
        _system.Refresh(unscaledDeltaSeconds);
        if (_fitted[4]) _radar.Refresh(unscaledDeltaSeconds);
    }

    /// <summary>Which aimed sensors this probe carries (the loadout chosen at the refit). A sensor that isn't
    /// fitted gets a placeholder instead of its screen, and is stopped if it was running.</summary>
    private void UpdateFitted()
    {
        GameState st = Game.State;
        if (st == null) return;
        bool any = false;
        any |= SetFitted(1, st.IsSensorInstalled(SensorKind.Imager));
        any |= SetFitted(2, st.IsSensorInstalled(SensorKind.Spectrometer));
        any |= SetFitted(4, st.IsSensorInstalled(SensorKind.Radar));
        if (any) ShowActiveBody();
    }

    private bool SetFitted(int idx, bool fitted)
    {
        if (_fitted[idx] == fitted) return false;
        _fitted[idx] = fitted;
        if (!fitted) HideByIndex(idx);
        UITheme t = UITheme.Current;
        var label = _tabs[idx].GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (label != null) label.color = fitted ? t.text : t.textDim;
        return true;
    }

    private void ShowActiveBody()
    {
        bool fitted = _fitted[_active];
        for (int i = 0; i < _bodies.Length; i++)
        {
            bool on = i == _active && fitted;
            if (_bodies[i].activeSelf != on) _bodies[i].SetActive(on);
        }
        if (_notFitted != null)
        {
            if (!fitted) UIKit.SetText(_notFittedLabel, Loc.Get("ui.screen.notfitted", Loc.Get(_tabKeys[_active])));
            if (_notFitted.activeSelf != !fitted) _notFitted.SetActive(!fitted);
        }
    }
}
