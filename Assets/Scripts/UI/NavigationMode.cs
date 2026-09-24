using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The NAVIGATION major mode (UI shell rework, GameShell). Same tabbed-dock pattern as SystemsDock: only the
/// selected minor mode's body is visible, each sub-panel builds its own content into a shared parent it no
/// longer positions or drags itself.
///
/// Minor modes: NAV (default - the graphical orbit map, NavScreen) / MANOEUVERS (ManeuverPanel) / JUMP
/// (JumpPanel) / NODES (NodePanel). OrbitPanel/TrackOrbitPanel are NOT minor modes of their own - they're
/// persistent readouts NavScreen itself builds as corner overlays inside its own map rect, per the design
/// decided for this rework: Orbit lives with the map, not across every Navigation tab.
/// </summary>
public sealed class NavigationMode
{
    private readonly NavScreen _nav = new NavScreen();
    private readonly ManeuverPanel _maneuver = new ManeuverPanel();
    private readonly JumpPanel _jump = new JumpPanel();
    private readonly NodePanel _node = new NodePanel();

    private GameObject[] _bodies;
    private Button[] _tabs;
    private int _active;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image panel = UIKit.AddPanel(parent, "NavigationMode", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        UIKit.Stretch(prt);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;

        RectTransform tabRow = UIKit.Node("Tabs", prt);
        UIKit.HStack(tabRow, 4f, 0, expandWidth: true);
        string[] labels =
        {
            Loc.Get("ui.tab.nav"), Loc.Get("ui.tab.maneuver"), Loc.Get("ui.tab.jump"), Loc.Get("ui.tab.node")
        };
        _tabs = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i; // capture
            _tabs[i] = UIKit.AddButton(tabRow, labels[i], () => SetActive(idx), 0f, 34f);
        }

        RectTransform navBody = UIKit.Node("NavBody", prt);
        UIKit.Size(navBody, flexibleWidth: 1f, flexibleHeight: 1f);
        _nav.Build(navBody);

        _bodies = new GameObject[4];
        _bodies[0] = navBody.gameObject;
        _bodies[1] = _maneuver.Build(prt);
        _bodies[2] = _jump.Build(prt);
        _bodies[3] = _node.Build(prt);

        SetActive(0);
        return prt.gameObject;
    }

    private void SetActive(int idx)
    {
        bool changed = idx != _active;
        _active = idx;
        for (int i = 0; i < _bodies.Length; i++)
            if (_bodies[i].activeSelf != (i == idx)) _bodies[i].SetActive(i == idx);
        for (int i = 0; i < _tabs.Length; i++)
            UIKit.SetButtonActive(_tabs[i], i == idx);

        if (changed && idx == 0) _nav.OnShown(); // re-fit the map's zoom to whatever orbit is current
    }

    /// <summary>Cheap enough to refresh every tab even when hidden; keeps each sub-panel self-contained.</summary>
    public void Refresh()
    {
        _nav.Refresh();
        _maneuver.Refresh();
        _jump.Refresh();
        _node.Refresh();
    }
}
