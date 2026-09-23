using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Crude reactor: off / 25% / 50% / 100%. More output burns hydrogen faster (level^1.5), and hydrogen is
/// also the fuel for burns and jumps, so power costs mobility. It shuts down when the tank is empty.
/// </summary>
public sealed class ReactorPanel
{
    private static readonly float[] Levels = { 0f, 0.25f, 0.5f, 1f };

    private TextMeshProUGUI _info;
    private Button[] _buttons;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform prt = UIKit.Node("Reactor", parent);
        UIKit.Size(prt, flexibleWidth: 1f);

        var v = UIKit.VStack(prt, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        RectTransform row = UIKit.Node("Levels", prt);
        UIKit.HStack(row, 6f, 0, expandWidth: true);
        _buttons = new Button[Levels.Length];
        for (int i = 0; i < Levels.Length; i++)
        {
            float level = Levels[i];
            string label = level <= 0f ? Loc.Get("ui.reactor.off") : (level * 100f).ToString("0") + "%";
            _buttons[i] = UIKit.AddButton(row, label, () =>
            {
                if (Game.State != null) Game.State.SetReactorLevel(level);
            }, 0f, 34f);
        }

        _info = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);

        return prt.gameObject;
    }

    public void Refresh()
    {
        if (Game.State == null || _info == null) return;
        GameState s = Game.State;

        UIKit.SetText(_info, Loc.Get("ui.reactor.info", s.ReactorOutputPerDay, s.ReactorHydrogenPerDay));
        for (int i = 0; i < _buttons.Length; i++)
        {
            UIKit.SetButtonActive(_buttons[i], Mathf.Abs(s.ReactorLevel - Levels[i]) < 0.01f);
            _buttons[i].interactable = Levels[i] <= 0f || s.Hydrogen > 0f;
        }
    }
}
