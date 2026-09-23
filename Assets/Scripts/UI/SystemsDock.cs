using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One docked, tabbed panel that hosts Jump / Wake / Reactor / Data / Transmit, so the screen isn't covered
/// in five separate floating windows. Only the selected tab's body is visible; each sub-panel builds its
/// content the same way it always did, it just no longer owns its own background or drag handle.
/// </summary>
public sealed class SystemsDock
{
    private readonly JumpPanel _jump = new JumpPanel();
    private readonly WakePanel _wake = new WakePanel();
    private readonly ReactorPanel _reactor = new ReactorPanel();
    private readonly DataPanel _data = new DataPanel();
    private readonly TransmitPanel _tx = new TransmitPanel();
    private readonly NodePanel _node = new NodePanel();

    private GameObject[] _bodies;
    private Button[] _tabs;
    private int _active;

    public void Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image panel = UIKit.AddPanel(parent, "SystemsDock", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 0f);
        prt.anchoredPosition = new Vector2(8f, 8f);
        prt.sizeDelta = new Vector2(700f, 0f);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        RectTransform tabRow = UIKit.Node("Tabs", prt);
        UIKit.HStack(tabRow, 4f, 0, expandWidth: true);
        string[] labels =
        {
            Loc.Get("ui.tab.jump"), Loc.Get("ui.tab.wake"), Loc.Get("ui.tab.reactor"),
            Loc.Get("ui.tab.data"), Loc.Get("ui.tab.tx"), Loc.Get("ui.tab.node")
        };
        _tabs = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i; // capture
            _tabs[i] = UIKit.AddButton(tabRow, labels[i], () => SetActive(idx), 0f, 34f);
        }

        _bodies = new GameObject[6];
        _bodies[0] = _jump.Build(prt);
        _bodies[1] = _wake.Build(prt);
        _bodies[2] = _reactor.Build(prt);
        _bodies[3] = _data.Build(prt);
        _bodies[4] = _tx.Build(prt);
        _bodies[5] = _node.Build(prt);

        SetActive(0);
        DraggablePanel.Attach(prt, "systems");
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
        // Cheap enough to refresh every tab even when hidden; keeps each sub-panel self-contained.
        _jump.Refresh();
        _wake.Refresh();
        _reactor.Refresh();
        _data.Refresh();
        _tx.Refresh();
        _node.Refresh();
    }
}
