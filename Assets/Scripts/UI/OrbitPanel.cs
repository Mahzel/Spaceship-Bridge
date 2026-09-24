using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Persistent readout of the ship's actual orbit (see ShipOrbit / OrbitalMechanics): what it's currently
/// orbiting, periapsis/apoapsis, eccentricity, inclination and period. Read-only for now - maneuver-node
/// planning is a later pass. Reads Game.State.ShipOrbit.
///
/// Lives pinned to the bottom-right corner of the NAV minor mode's content (Navigation major mode, UI shell
/// rework) - no longer a draggable floating window anchored to the whole canvas. Build's `parent` is that
/// NAV tab's own root rect, so "bottom-right" here means bottom-right of THAT, not the screen.
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
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 0f);
        prt.anchoredPosition = new Vector2(-8f, 8f);
        prt.sizeDelta = new Vector2(340f, 0f);

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
    }

    public void Refresh()
    {
        if (_primary == null) return;
        ShipOrbit orbit = Game.State != null ? Game.State.ShipOrbit : null;

        if (orbit != null && orbit.Hyperbolic)
        {
            // Unbound: show the escape trajectory rather than "no orbit".
            float g = GameConstants.GAME_UNITS_PER_UA;
            UIKit.SetText(_primary, Loc.Get("ui.orbit.escape", orbit.PrimaryName));
            UIKit.SetText(_apsides, Loc.Get("ui.orbit.peri", Loc.Distance(orbit.PeriapsisGame / g)));
            UIKit.SetText(_shape,   Loc.Get("ui.orbit.shape", (float)orbit.Eccentricity, orbit.Elements.inclination));
            UIKit.SetText(_period,  Loc.Get("ui.orbit.vinf", System.Math.Sqrt(orbit.Mu / -orbit.SemiMajorAxis) * ShipState.KmPerUnit));
            UIKit.SetText(_now,     Loc.Get("ui.orbit.nu", orbit.TrueAnomalyDeg));
            return;
        }

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
        UIKit.SetText(_apsides, Loc.Get("ui.orbit.apsides", Loc.Distance(periAu), Loc.Distance(apoAu)));
        UIKit.SetText(_shape,   Loc.Get("ui.orbit.shape", (float)orbit.Eccentricity, orbit.Elements.inclination));
        UIKit.SetText(_period,  Loc.Get("ui.orbit.period", periodDays));
        UIKit.SetText(_now,     Loc.Get("ui.orbit.nu", orbit.TrueAnomalyDeg));
    }
}
