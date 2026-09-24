using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The in-flight gameplay frame (UI shell rework): a persistent left sidebar picking one of five major modes
/// (SENSORS / NAVIGATION / COMMS / SYSTEMS / ATLAS), a collapsible track strip between the topbar (StatusBar,
/// built separately by GameUI) and the mode row, and a content area the active major mode fills completely.
/// Replaces seven previously-independent, draggable floating panels (SystemsDock, SensorConsole, OrbitPanel,
/// ManeuverPanel, TrackPanel, AtlasPanel, NavScreen as its own full-screen overlay) with one fixed layout, so
/// the screen isn't a pile of overlapping windows the player has to manage.
///
/// Only the active major mode's content is visible, but (matching the SystemsDock/SensorConsole convention
/// this replaces) every mode's Refresh keeps running regardless - cheap, and keeps each sub-panel self-
/// contained. Split into RefreshFast (called every real frame, like RunDriver ticks WaterfallProcessor: the
/// waterfall/imager/spectrometer inside Sensors and NavScreen's own self-throttled redraw both need it) and
/// RefreshSlow (10 Hz is plenty for everything else), mirroring the cadence GameUI already used before this
/// rework - GameUI just calls into this instead of seven separate objects now.
/// </summary>
public sealed class GameShell
{
    private readonly TrackPanel _tracks = new TrackPanel();
    private readonly SensorConsole _sensors = new SensorConsole();
    private readonly NavigationMode _navigation = new NavigationMode();
    private readonly CommsMode _comms = new CommsMode();
    private readonly SystemsMode _systems = new SystemsMode();
    private readonly AtlasPanel _atlas = new AtlasPanel();

    private GameObject[] _bodies;
    private Button[] _modeButtons;
    private int _active;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform root = UIKit.Node("Shell", parent);
        UIKit.Stretch(root, top: t.statusBarHeight); // leaves StatusBar's own strip alone above this
        var rootV = UIKit.VStack(root, t.spacing, 0);
        rootV.childAlignment = TextAnchor.UpperLeft;

        _tracks.Build(root);

        RectTransform row = UIKit.Node("Row", root);
        UIKit.Size(row, flexibleWidth: 1f, flexibleHeight: 1f);
        // expandWidth: false - the sidebar is a fixed 160px, not an equal partner to split space with
        // Content; Unity's HorizontalLayoutGroup force-expands EVERY child under expandWidth:true regardless
        // of preferredWidth, which is what made the sidebar balloon to half the screen.
        UIKit.HStack(row, t.spacing, 0, expandWidth: false).childAlignment = TextAnchor.UpperLeft;

        BuildSidebar(row, t);
        BuildContent(row, t);

        return root.gameObject;
    }

    private void BuildSidebar(Transform parent, UITheme t)
    {
        // Outer node: ONLY a LayoutElement, so Row's HorizontalLayoutGroup reads a clean, unambiguous 160px
        // preferredWidth from it. A VerticalLayoutGroup on the SAME node (as this used to be) also implements
        // ILayoutElement and reports ITS OWN preferred width back up to Row (based on its children's sizes),
        // which silently overrode the fixed 160px - that's what actually ballooned the sidebar, not
        // expandWidth. The VStack (for the mode buttons) goes on a separate Stretch-filled child instead, the
        // same way every major mode's own content panel already avoids this by building into a Content child
        // rather than putting a LayoutGroup directly on a Size-constrained node.
        RectTransform side = UIKit.Node("Sidebar", parent);
        UIKit.Size(side, preferredWidth: 160f, flexibleHeight: 1f);

        Image sideImg = UIKit.AddPanel(side, "Bg", t.panelColor);
        RectTransform inner = sideImg.rectTransform;
        UIKit.Stretch(inner);
        var sideV = UIKit.VStack(inner, 6f, (int)t.padding);
        sideV.childAlignment = TextAnchor.UpperLeft;

        string[] labels =
        {
            Loc.Get("ui.mode.sensors"), Loc.Get("ui.mode.navigation"), Loc.Get("ui.mode.comms"),
            Loc.Get("ui.mode.systems"), Loc.Get("ui.mode.atlas")
        };
        _modeButtons = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i; // capture
            _modeButtons[i] = UIKit.AddButton(inner, labels[i], () => SetMode(idx), 0f, 44f);
        }
    }

    private void BuildContent(Transform parent, UITheme t)
    {
        RectTransform content = UIKit.Node("Content", parent);
        UIKit.Size(content, flexibleWidth: 1f, flexibleHeight: 1f);

        _bodies = new GameObject[5];
        _bodies[0] = _sensors.Build(content);
        _bodies[1] = _navigation.Build(content);
        _bodies[2] = _comms.Build(content);
        _bodies[3] = _systems.Build(content);
        _bodies[4] = _atlas.Build(content);

        SetMode(0);
    }

    private void SetMode(int idx)
    {
        _active = idx;
        for (int i = 0; i < _bodies.Length; i++)
            if (_bodies[i].activeSelf != (i == idx)) _bodies[i].SetActive(i == idx);
        for (int i = 0; i < _modeButtons.Length; i++)
            UIKit.SetButtonActive(_modeButtons[i], i == idx);
    }

    /// <summary>Every real frame: the sensor screens (aimed reads, DSP redraws) and NavScreen (self-throttled
    /// internally) both need it, same as before this rework.</summary>
    public void RefreshFast(float unscaledDeltaSeconds)
    {
        _sensors.Refresh(unscaledDeltaSeconds);
        _navigation.Refresh();
    }

    /// <summary>10 Hz is plenty for the rest - text readouts, the track strip, Comms/Systems/Atlas.</summary>
    public void RefreshSlow()
    {
        _tracks.Refresh();
        _comms.Refresh();
        _systems.Refresh();
        _atlas.Refresh();

        bool navFitted = Game.State != null && NavTier.HasSystemView(Game.State.Loadout.Level(ProbeSystem.NavComputer));
        if (_modeButtons != null && _modeButtons.Length > 1) _modeButtons[1].interactable = navFitted;
    }

    /// <summary>Before the canvas is destroyed (UI rebuild): stops the aimed sensors so their power loads are
    /// released, exactly as switching tabs would - same as SensorConsole.Shutdown() always required.</summary>
    public void Shutdown() => _sensors.Shutdown();
}
