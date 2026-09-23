using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-left readout of the ship's actual orbit (see ShipOrbit / OrbitalMechanics): what it's currently
/// orbiting, periapsis/apoapsis, eccentricity, inclination and period. Read-only for now - maneuver-node
/// planning is a later pass. Reads Game.State.ShipOrbit; refreshed by GameUI like every other panel.
/// </summary>
public sealed class OrbitPanel
{
    private TextMeshProUGUI _primary, _apsides, _shape, _period, _now;

    public void Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image panel = UIKit.AddPanel(parent, "OrbitPanel", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 1f);
        prt.anchoredPosition = new Vector2(8f, -(t.statusBarHeight + 8f));
        prt.sizeDelta = new Vector2(380f, 0f);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        UIKit.AddLabel(prt, Loc.Get("ui.orbit"), t.fontSizeBody, t.accent);
        _primary = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.text);
        _apsides = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.text);
        _shape   = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);
        _period  = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);
        _now     = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);

        DraggablePanel.Attach(prt, "orbit");
    }

    public void Refresh()
    {
        if (_primary == null) return;
        ShipOrbit orbit = Game.State != null ? Game.State.ShipOrbit : null;

        if (orbit == null || !orbit.Valid)
        {
            UIKit.SetText(_primary, Loc.Get("ui.orbit.none"));
            UIKit.SetText(_apsides, "");
            UIKit.SetText(_shape, "");
            UIKit.SetText(_period, "");
            UIKit.SetText(_now, "");
            return;
        }

        float gu = GameConstants.GAME_UNITS_PER_UA;
        float periAu = orbit.PeriapsisGame / gu;
        float apoAu  = orbit.ApoapsisGame / gu;
        double periodDays = orbit.Elements.orbitalPeriod / 86400.0;

        UIKit.SetText(_primary, Loc.Get("ui.orbit.primary", orbit.PrimaryName));
        UIKit.SetText(_apsides, Loc.Get("ui.orbit.apsides", periAu, apoAu));
        UIKit.SetText(_shape,   Loc.Get("ui.orbit.shape", orbit.Elements.eccentricity, orbit.Elements.inclination));
        UIKit.SetText(_period,  Loc.Get("ui.orbit.period", periodDays));
        UIKit.SetText(_now,     Loc.Get("ui.orbit.nu", orbit.TrueAnomalyDeg));
    }
}
