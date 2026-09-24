using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The NAVIGATION major mode (UI shell rework, GameShell). Same tabbed-dock pattern as SystemsDock: only the
/// selected minor mode's body is visible, each sub-panel builds its own content into a shared parent it no
/// longer positions or drags itself.
///
/// Minor modes: NAV (default - the graphical orbit map, NavScreen) / MANOEUVERS (ManeuverPanel) / JUMP
/// (JumpPanel) / NODES (NodePanel). OrbitPanel is NOT a minor mode of its own - it's a persistent readout
/// pinned to the bottom-right corner of the NAV tab specifically (built into the same shared body as
/// NavScreen, so its own bottom-right anchor resolves against that whole tab area), per the design decided
/// for this rework: Orbit lives with the map, not across every Navigation tab.
/// </summary>
public sealed class NavigationMode
{
    private readonly NavScreen _nav = new NavScreen();
    private readonly OrbitPanel _orbit = new OrbitPanel();
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

        // NAV tab: a shared container so OrbitPanel's own bottom-right corner anchor resolves against the
        // whole tab area, not just whatever room NavScreen's internal map/sidebar layout happens to leave.
        RectTransform navBody = UIKit.Node("NavBody", prt);
        UIKit.Size(navBody, flexibleWidth: 1f, flexibleHeight: 1f);
        _nav.Build(navBody);
        _orbit.Build(navBody);

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
        _orbit.Refresh();
        _maneuver.Refresh();
        _jump.Refresh();
        _node.Refresh();
    }
}
