using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-right panel: ship heading, velocity and discrete burns. A burn changes the velocity along the
/// heading instantly and spends hydrogen (see ProbeSpec.hydrogenPerKmS). Manoeuvring is what makes range
/// observable from bearings alone.
/// </summary>
public sealed class ManeuverPanel
{
    private static readonly float[] BurnSizes = { 0.1f, 1f, 5f, 20f };
    private static readonly float[] TurnSteps = { -10f, -1f, 1f, 10f };

    private TextMeshProUGUI _heading, _velocity, _cost;
    private Button[] _sizeButtons;
    private Button _burn, _retro;
    private int _sizeIndex = 1;

    public void Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image panel = UIKit.AddPanel(parent, "ManeuverPanel", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 0f);
        prt.anchoredPosition = new Vector2(-8f, 8f);
        prt.sizeDelta = new Vector2(430f, 0f);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        UIKit.AddLabel(prt, Loc.Get("ui.maneuver"), t.fontSizeBody, t.accent);

        _heading = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.text);
        RectTransform turnRow = UIKit.Node("TurnRow", prt);
        UIKit.HStack(turnRow, 6f, 0, expandWidth: true);
        for (int i = 0; i < TurnSteps.Length; i++)
        {
            float step = TurnSteps[i];
            string label = step > 0f ? "+" + step.ToString("0") : step.ToString("0");
            UIKit.AddButton(turnRow, label, () =>
            {
                if (Game.State != null) Game.State.Ship.Turn(step);
            }, 0f, 34f);
        }

        _velocity = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.text);

        UIKit.AddLabel(prt, Loc.Get("ui.burnsize"), t.fontSizeSmall, t.textDim);
        RectTransform sizeRow = UIKit.Node("SizeRow", prt);
        UIKit.HStack(sizeRow, 6f, 0, expandWidth: true);
        _sizeButtons = new Button[BurnSizes.Length];
        for (int i = 0; i < BurnSizes.Length; i++)
        {
            int index = i;
            _sizeButtons[i] = UIKit.AddButton(sizeRow, BurnSizes[i].ToString("0.#"), () => _sizeIndex = index, 0f, 34f);
        }

        RectTransform burnRow = UIKit.Node("BurnRow", prt);
        UIKit.HStack(burnRow, 6f, 0, expandWidth: true);
        _burn  = UIKit.AddButton(burnRow, Loc.Get("ui.burn.fwd"),   () => Burn(+1f), 0f, 40f);
        _retro = UIKit.AddButton(burnRow, Loc.Get("ui.burn.retro"), () => Burn(-1f), 0f, 40f);

        _cost = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);
        DraggablePanel.Attach(prt, "maneuver");
    }

    private void Burn(float sign)
    {
        if (Game.State == null || Game.Run == null || Game.Run.Phase != RunPhase.Flight) return;
        Game.State.TryBurn(sign * BurnSizes[_sizeIndex]);
    }

    public void Refresh()
    {
        if (Game.State == null || _heading == null) return;
        GameState state = Game.State;
        ShipState ship = state.Ship;

        UIKit.SetText(_heading,  Loc.Get("ui.heading", ship.headingDeg));
        UIKit.SetText(_velocity, Loc.Get("ui.velocity", ship.Speed, ship.VelocityBearingDeg));

        float dv = BurnSizes[_sizeIndex];
        float cost = state.BurnCost(dv);
        bool flying = Game.Run != null && Game.Run.Phase == RunPhase.Flight;
        bool canBurn = flying && cost <= state.Hydrogen + 1e-4f;

        UIKit.SetText(_cost, Loc.Get("ui.burn.cost", cost, state.Hydrogen));
        _burn.interactable  = canBurn;
        _retro.interactable = canBurn;
        for (int i = 0; i < _sizeButtons.Length; i++)
            UIKit.SetButtonActive(_sizeButtons[i], i == _sizeIndex);
    }
}
