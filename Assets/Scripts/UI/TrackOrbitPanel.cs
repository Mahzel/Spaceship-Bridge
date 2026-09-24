using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-left counterpart to OrbitPanel: the SELECTED TRACK's fitted orbit (OrbitFit.TryFit), not the ship's
/// own. Same map-corner-overlay treatment - built into NavScreen's _mapRect and anchored to ITS bottom-left,
/// so it sits inside the map area rather than competing with the sidebar for the tab's bottom-right corner
/// (see NavScreen.BuildMap: OrbitPanel moved off the whole NAV tab's corner and onto the map itself for the
/// same reason).
///
/// Deliberately reuses nothing from NodeData/the catalogue - same two-knowledge-tiers rule as the transfer
/// planner (handoff-navigation-ui.md): what's shown here is exactly what OrbitFit derives from the track's own
/// range estimate, uncertainty and all. Blank whenever no track is selected or its fit isn't good enough yet.
/// </summary>
public sealed class TrackOrbitPanel
{
    private TextMeshProUGUI _primary, _apsides, _shape, _period;

    public void Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image panel = UIKit.AddPanel(parent, "TrackOrbitPanel", t.panelColor);
        panel.raycastTarget = true;
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 0f);
        prt.anchoredPosition = new Vector2(8f, 8f);
        prt.sizeDelta = new Vector2(300f, 0f);

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        UIKit.AddLabel(prt, Loc.Get("ui.orbit.target"), t.fontSizeBody, t.accent);
        _primary = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.text);
        _apsides = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.text);
        _shape   = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);
        _period  = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);
    }

    public void Refresh()
    {
        if (_primary == null || Game.State == null) return;

        Track tr = Game.State.Tracks.Find(Game.State.Tracks.SelectedId);
        if (tr == null || !OrbitFit.TryFit(tr, out OrbitElements el))
        {
            UIKit.SetText(_primary, Loc.Get("ui.orbit.none"));
            UIKit.SetText(_apsides, "");
            UIKit.SetText(_shape, "");
            UIKit.SetText(_period, "");
            return;
        }

        ShipOrbit ship = Game.State.ShipOrbit;
        string primaryName = ship != null ? ship.PrimaryName : "";

        float gu = GameConstants.GAME_UNITS_PER_UA;
        float periAu = el.semiMajorAxis * (1f - el.eccentricity) / gu;
        float apoAu  = el.semiMajorAxis * (1f + el.eccentricity) / gu;
        double periodDays = el.orbitalPeriod / 86400.0;

        UIKit.SetText(_primary, Loc.Get("ui.orbit.primary", primaryName));
        UIKit.SetText(_apsides, Loc.Get("ui.orbit.apsides", Loc.Distance(periAu), Loc.Distance(apoAu)));
        UIKit.SetText(_shape,   Loc.Get("ui.orbit.shape", el.eccentricity, el.inclination));
        UIKit.SetText(_period,  Loc.Get("ui.orbit.period", periodDays));
    }
}
