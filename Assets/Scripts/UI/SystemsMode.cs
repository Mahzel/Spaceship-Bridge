using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The SYSTEMS major mode (UI shell rework, GameShell). Same tabbed-dock pattern as SystemsDock/NavigationMode.
/// Minor modes: REACTOR (ReactorPanel) / WAKE (WakePanel - the wake-signature monitor). Not to be confused
/// with ContactsScreen, a Sensors tab renamed away from "System" specifically to avoid colliding with this
/// mode's name.
/// </summary>
public sealed class SystemsMode
{
    private readonly ReactorPanel _reactor = new ReactorPanel();
    private readonly WakePanel _wake = new WakePanel();

    private GameObject[] _bodies;
    private Button[] _tabs;
    private int _active;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image panel = UIKit.AddPanel(parent, "SystemsMode", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        UIKit.Stretch(prt);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;

        RectTransform tabRow = UIKit.Node("Tabs", prt);
        UIKit.HStack(tabRow, 4f, 0, expandWidth: true);
        string[] labels = { Loc.Get("ui.tab.reactor"), Loc.Get("ui.tab.wake") };
        _tabs = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i; // capture
            _tabs[i] = UIKit.AddButton(tabRow, labels[i], () => SetActive(idx), 0f, 34f);
        }

        _bodies = new GameObject[2];
        _bodies[0] = _reactor.Build(prt);
        _bodies[1] = _wake.Build(prt);

        SetActive(0);
        return prt.gameObject;
    }

    private void SetActive(int idx)
    {
        _active = idx;
        for (int i = 0; i < _bodies.Length; i++)
            if (_bodies[i].activeSelf != (i == idx)) _bodies[i].SetActive(i == idx);
        for (int i = 0; i < _tabs.Length; i++)
            UIKit.SetButtonActive(_tabs[i], i == idx);
    }

    public void Refresh()
    {
        _reactor.Refresh();
        _wake.Refresh();
    }
}
