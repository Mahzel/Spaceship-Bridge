using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The COMMS major mode (UI shell rework, GameShell). Same tabbed-dock pattern as SystemsDock/NavigationMode.
/// Minor modes: DATA (DataPanel - the survey record queue and what's transmitted) / TX (TransmitPanel - the
/// transmitter itself: range, rate, power).
/// </summary>
public sealed class CommsMode
{
    private readonly DataPanel _data = new DataPanel();
    private readonly TransmitPanel _tx = new TransmitPanel();

    private GameObject[] _bodies;
    private Button[] _tabs;
    private int _active;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image panel = UIKit.AddPanel(parent, "CommsMode", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        UIKit.Stretch(prt);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;

        RectTransform tabRow = UIKit.Node("Tabs", prt);
        UIKit.HStack(tabRow, 4f, 0, expandWidth: true);
        string[] labels = { Loc.Get("ui.tab.data"), Loc.Get("ui.tab.tx") };
        _tabs = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i; // capture
            _tabs[i] = UIKit.AddButton(tabRow, labels[i], () => SetActive(idx), 0f, 34f);
        }

        _bodies = new GameObject[2];
        _bodies[0] = _data.Build(prt);
        _bodies[1] = _tx.Build(prt);

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
        _data.Refresh();
        _tx.Refresh();
    }
}
