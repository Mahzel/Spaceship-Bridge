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
/// This is a plain class, not a MonoBehaviour, matching every sub-screen it owns; GameUI builds one and calls
/// Build/Refresh, the same way it drives SystemsDock.
/// </summary>
public sealed class SensorConsole
{
    private readonly WaterfallScreen _waterfall = new WaterfallScreen();
    private readonly ImagerScreen _imager = new ImagerScreen();
    private readonly SpectrometerScreen _spectrometer = new SpectrometerScreen();
    private readonly SystemScreen _system = new SystemScreen();
    private readonly RadarScreen _radar = new RadarScreen();

    private GameObject[] _bodies;
    private Button[] _tabs;
    private int _active;

    private const string PrefKey = "ui.screen.mode";

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image panel = UIKit.AddPanel(parent, "SensorConsole", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 0f);
        prt.anchoredPosition = new Vector2(-8f, 8f);
        prt.sizeDelta = new Vector2(920f, 0f);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        RectTransform tabRow = UIKit.Node("Modes", prt);
        UIKit.HStack(tabRow, 4f, 0, expandWidth: true);
        string[] keys = { "ui.screen.waterfall", "ui.screen.imager", "ui.screen.spectrometer", "ui.screen.system", "ui.screen.radar" };
        _tabs = new Button[keys.Length];
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

        int start = 0;
        try { start = PlayerPrefs.GetInt(PrefKey, 0); } catch (System.Exception) { }
        SetActive(Mathf.Clamp(start, 0, _bodies.Length - 1));

        DraggablePanel.Attach(prt, "sensors");
        return prt.gameObject;
    }

    private void SetActive(int idx)
    {
        if (idx != _active) HideByIndex(_active);

        _active = idx;
        for (int i = 0; i < _bodies.Length; i++)
            if (_bodies[i].activeSelf != (i == idx)) _bodies[i].SetActive(i == idx);
        for (int i = 0; i < _tabs.Length; i++)
            UIKit.SetButtonActive(_tabs[i], i == idx);

        try { PlayerPrefs.SetInt(PrefKey, idx); } catch (System.Exception) { }
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

    /// <summary>Cheap enough to refresh every mode even when hidden; keeps each screen self-contained and lets
    /// the passive ones (waterfall, system) keep working regardless of which tab is showing.</summary>
    public void Refresh(float unscaledDeltaSeconds)
    {
        _waterfall.Refresh(unscaledDeltaSeconds);
        _imager.Refresh(unscaledDeltaSeconds);
        _spectrometer.Refresh(unscaledDeltaSeconds);
        _system.Refresh(unscaledDeltaSeconds);
        _radar.Refresh(unscaledDeltaSeconds);
    }
}
