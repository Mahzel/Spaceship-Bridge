using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// NAV: full-screen graphical plot of the ship's own orbit (handoff-navigation-ui.md, roadmap item 1).
/// Gated by the NavComputer loadout tier (NavTier.HasSystemView) - unfitted, the screen still opens but shows
/// only a "not fitted" message, same pattern as ui.screen.notfitted for sensors.
///
/// Own-ship orbit is exact (ShipOrbit's conic, not a sensor read): the ellipse, Pe/Ap, the reference-plane
/// crossings (AN/DN) and a live true-anomaly marker. Catalogue and track layers (roadmap item 2) are drawn on
/// top, toggle-able, and never touch SensorSight or a live CelestialBody - catalogue orbits come from
/// Catalogue.CollectOrbitsAroundPrimary (the generated SystemData elements), tracks from TrackManager's own
/// bearing/range estimates. The maneuver planner overlay, the timeline and the galaxy map are later items on
/// the same roadmap and are NOT drawn here yet.
///
/// Top-down (world X, Z - see KeplerOrbit's rotation: Y is the out-of-plane axis, so a pure XZ projection is
/// exactly the ecliptic view every other screen already uses). Inclination doesn't show as tilt in a top-down
/// projection, so it gets its own small dial readout instead of being deferred to a later 3D pass.
/// </summary>
public sealed class NavScreen
{
    private const float RedrawInterval = 0.1f;
    private const int EllipsePoints = 96;
    private const int CatalogueEllipsePoints = 72;
    private const float ZoomStep = 1.4f;
    private const float MinPxPerAu = 0.01f, MaxPxPerAu = 200000f;
    private const float BearingRayPx = 160f; // length of a bearing-only track's ray, screen pixels

    private GameObject _root;
    private RectTransform _panelRect, _mapRect, _dialRect;
    private MapCanvas _map, _dial;
    private TextMeshProUGUI _notFitted, _primary, _shape, _incl, _period, _nu;
    private TextMeshProUGUI _peLabel, _apLabel, _anLabel, _dnLabel, _shipLabel;
    private Button _back, _zoomIn, _zoomOut, _layerCatalogue, _layerTracks;

    // Layer toggles (roadmap item 2). Pooled labels grow to fit however many catalogue bodies / tracks exist.
    private bool _showCatalogue = true, _showTracks = true;
    private readonly List<TextMeshProUGUI> _catalogueLabels = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> _trackLabels = new List<TextMeshProUGUI>();
    private readonly List<Catalogue.CataloguedOrbit> _catalogueScratch = new List<Catalogue.CataloguedOrbit>();

    private bool _open;
    private float _pixelsPerAu = 40f;
    private bool _fitted; // whether AutoFit ran for the orbit currently on screen
    private float _redrawAccum = RedrawInterval;

    public bool IsOpen => _open;

    // -----------------------------------------------------------------------------------------------------
    #region Build
    public void Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image dim = UIKit.AddPanel(parent, "Nav", t.dimColor);
        dim.raycastTarget = true;
        UIKit.Stretch(dim.rectTransform);
        _root = dim.gameObject;

        Image panel = UIKit.AddPanel(dim.transform, "Panel", t.panelColor);
        RectTransform prt = panel.rectTransform;
        _panelRect = prt;
        prt.anchorMin = new Vector2(0.03f, 0.05f);
        prt.anchorMax = new Vector2(0.97f, 0.95f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;

        var v = UIKit.VStack(prt, t.spacing, (int)t.padding);
        v.childAlignment = TextAnchor.UpperLeft;

        RectTransform top = UIKit.Node("Top", prt);
        UIKit.HStack(top, 8f, 0).childAlignment = TextAnchor.MiddleLeft;
        _back = UIKit.AddButton(top, Loc.Get("ui.nav.back"), Close, 90f, 34f);
        UIKit.AddLabel(top, Loc.Get("ui.nav.title"), t.fontSizeBody, t.accent);

        _notFitted = UIKit.AddLabel(prt, "", t.fontSizeBody, t.textDim);
        _notFitted.textWrappingMode = TextWrappingModes.Normal;
        UIKit.Size(_notFitted.rectTransform, preferredWidth: 800f);

        RectTransform body = UIKit.Node("Body", prt);
        UIKit.Size(body, flexibleWidth: 1f);
        var bodyH = UIKit.HStack(body, t.spacing * 2f, 0, expandWidth: true);
        bodyH.childAlignment = TextAnchor.UpperLeft;

        BuildMap(body);
        BuildSidebar(body, t);

        _root.SetActive(false);
    }

    private void BuildMap(Transform parent)
    {
        _mapRect = UIKit.Node("Map", parent);
        UIKit.Size(_mapRect, flexibleWidth: 1f, minHeight: 600f);
        _map = _mapRect.gameObject.AddComponent<MapCanvas>();
    }

    private void BuildSidebar(Transform parent, UITheme t)
    {
        RectTransform side = UIKit.Node("Sidebar", parent);
        UIKit.Size(side, preferredWidth: 340f);
        var v = UIKit.VStack(side, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;

        _primary = UIKit.AddLabel(side, "", t.fontSizeBody, t.accent);
        _shape   = UIKit.AddLabel(side, "", t.fontSizeSmall, t.text);
        _period  = UIKit.AddLabel(side, "", t.fontSizeSmall, t.text);
        _nu      = UIKit.AddLabel(side, "", t.fontSizeSmall, t.textDim);

        UIKit.AddSpacer(side, 6f);
        _incl = UIKit.AddLabel(side, "", t.fontSizeSmall, t.text);

        _dialRect = UIKit.Node("InclDial", side);
        UIKit.Size(_dialRect, preferredWidth: 300f, minHeight: 160f);
        _dial = _dialRect.gameObject.AddComponent<MapCanvas>();

        UIKit.AddSpacer(side, 6f);
        RectTransform zoomRow = UIKit.Node("Zoom", side);
        UIKit.HStack(zoomRow, 6f, 0, expandWidth: true);
        _zoomOut = UIKit.AddButton(zoomRow, Loc.Get("ui.nav.zoomout"), () => Zoom(1f / ZoomStep), 0f, 34f);
        _zoomIn  = UIKit.AddButton(zoomRow, Loc.Get("ui.nav.zoomin"),  () => Zoom(ZoomStep), 0f, 34f);

        UIKit.AddSpacer(side, 6f);
        RectTransform layerRow = UIKit.Node("Layers", side);
        UIKit.HStack(layerRow, 6f, 0, expandWidth: true);
        _layerCatalogue = UIKit.AddButton(layerRow, Loc.Get("ui.nav.layer.catalogue"), ToggleCatalogue, 0f, 34f);
        _layerTracks    = UIKit.AddButton(layerRow, Loc.Get("ui.nav.layer.tracks"),    ToggleTracks,    0f, 34f);
        UIKit.SetButtonActive(_layerCatalogue, _showCatalogue);
        UIKit.SetButtonActive(_layerTracks, _showTracks);

        // Marker labels: a fixed handful, positioned over the map each redraw. No label-declutter yet
        // (handoff item 1 note) - fine at this vertex count (primary, ship, Pe, Ap, AN, DN).
        _peLabel   = MapLabel(_mapRect, Loc.Get("ui.nav.label.pe"), t.navNode);
        _apLabel   = MapLabel(_mapRect, Loc.Get("ui.nav.label.ap"), t.navNode);
        _anLabel   = MapLabel(_mapRect, Loc.Get("ui.nav.label.an"), t.navPlane);
        _dnLabel   = MapLabel(_mapRect, Loc.Get("ui.nav.label.dn"), t.navPlane);
        _shipLabel = MapLabel(_mapRect, Loc.Get("ui.nav.label.ship"), t.accent);
    }

    private static TextMeshProUGUI MapLabel(Transform parent, string text, Color color)
    {
        TextMeshProUGUI l = UIKit.AddLabel(parent, text, UITheme.Current.fontSizeSmall, color);
        RectTransform rt = l.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        return l;
    }
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region Open / close
    public void Toggle() { if (_open) Close(); else Open(); }

    public void Open()
    {
        if (_root == null) return;
        _open = true;
        _fitted = false; // re-fit zoom to whatever orbit is current
        _redrawAccum = RedrawInterval;
    }

    public void Close() => _open = false;
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region Refresh
    public void Refresh()
    {
        if (_root == null) return;

        RunController run = Game.Run;
        bool canShow = _open && run != null && run.Phase == RunPhase.Flight;
        if (_root.activeSelf != canShow)
        {
            _root.SetActive(canShow);
            // Force the flex layout (map/sidebar widths) to settle before the first Draw() reads _mapRect.rect,
            // so the initial zoom fit isn't computed off a stale (or default) rect size from while it was hidden.
            if (canShow) LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);
        }
        if (!canShow) { _open = _open && canShow; return; }

        int navLevel = Game.State != null ? Game.State.Loadout.Level(ProbeSystem.NavComputer) : 0;
        bool fitted = NavTier.HasSystemView(navLevel);
        _notFitted.gameObject.SetActive(!fitted);
        _mapRect.gameObject.SetActive(fitted);
        SetSidebarActive(fitted);
        if (!fitted)
        {
            UIKit.SetText(_notFitted, Loc.Get("ui.nav.notfitted"));
            return;
        }

        _redrawAccum += Time.unscaledDeltaTime;
        if (_redrawAccum < RedrawInterval) return;
        _redrawAccum = 0f;

        Draw();
    }

    private void SetSidebarActive(bool active)
    {
        _primary.gameObject.SetActive(active);
        _shape.gameObject.SetActive(active);
        _period.gameObject.SetActive(active);
        _nu.gameObject.SetActive(active);
        _incl.gameObject.SetActive(active);
        _dialRect.gameObject.SetActive(active);
        _zoomIn.gameObject.SetActive(active);
        _zoomOut.gameObject.SetActive(active);
        _layerCatalogue.gameObject.SetActive(active);
        _layerTracks.gameObject.SetActive(active);
    }

    private void Zoom(float factor) => _pixelsPerAu = Mathf.Clamp(_pixelsPerAu * factor, MinPxPerAu, MaxPxPerAu);

    private void ToggleCatalogue()
    {
        _showCatalogue = !_showCatalogue;
        UIKit.SetButtonActive(_layerCatalogue, _showCatalogue);
    }

    private void ToggleTracks()
    {
        _showTracks = !_showTracks;
        UIKit.SetButtonActive(_layerTracks, _showTracks);
    }
    #endregion

    // -----------------------------------------------------------------------------------------------------
    #region Draw
    private void Draw()
    {
        UITheme t = UITheme.Current;
        ShipOrbit orbit = Game.State != null ? Game.State.ShipOrbit : null;
        _map.Clear();

        if (orbit == null || !orbit.HasTrajectory)
        {
            HideMarkerLabels();
            HideCatalogueLabels();
            HideTrackLabels();
            UIKit.SetText(_primary, Loc.Get("ui.nav.none"));
            UIKit.SetText(_shape, ""); UIKit.SetText(_period, ""); UIKit.SetText(_nu, ""); UIKit.SetText(_incl, "");
            _map.Rebuild();
            DrawDial(float.NaN);
            return;
        }

        if (!_fitted) { AutoFit(orbit); _fitted = true; }

        float gu = GameConstants.GAME_UNITS_PER_UA;
        Vector2 center = _mapRect.rect.center;
        Func<Vector3, Vector2> toScreen = off => center + new Vector2(off.x, off.z) / gu * _pixelsPerAu;

        // Primary at the focus.
        _map.AddDot(center, 7f, t.navBody);

        // Catalogue layer (roadmap item 2): drawn first so the ship's own orbit and markers paint over it.
        DrawCatalogueLayer(orbit, toScreen);

        // The conic itself, sampled by true anomaly (uniform for now - adaptive density is a later refinement).
        bool bound = !orbit.Hyperbolic;
        var pts = new System.Collections.Generic.List<Vector2>(EllipsePoints + 1);
        if (bound)
        {
            for (int i = 0; i <= EllipsePoints; i++)
            {
                double nu = i / (double)EllipsePoints * Math.PI * 2.0;
                if (orbit.OffsetAtTrueAnomaly(nu, out Vector3 off)) pts.Add(toScreen(off));
            }
            if (pts.Count > 1) _map.AddPolyline(pts, 2f, t.navOrbit, true);
        }
        else
        {
            double e = orbit.Eccentricity;
            double nuInf = Math.Acos(-1.0 / e) * 0.98; // stop just short of the asymptote
            for (int i = 0; i <= EllipsePoints; i++)
            {
                double nu = -nuInf + i / (double)EllipsePoints * (2.0 * nuInf);
                if (orbit.OffsetAtTrueAnomaly(nu, out Vector3 off)) pts.Add(toScreen(off));
            }
            if (pts.Count > 1) _map.AddPolyline(pts, 2f, t.navOrbit, false);
        }

        // Periapsis (and apoapsis, bound orbits only).
        Vector2? peScreen = null, apScreen = null;
        if (orbit.OffsetAtTrueAnomaly(0.0, out Vector3 peOff))
        {
            peScreen = toScreen(peOff);
            _map.AddCross(peScreen.Value, 6f, 2f, t.navNode);
        }
        if (bound && orbit.OffsetAtTrueAnomaly(Math.PI, out Vector3 apOff))
        {
            apScreen = toScreen(apOff);
            _map.AddCross(apScreen.Value, 6f, 2f, t.navNode);
        }

        // Reference-plane crossings.
        Vector2? anScreen = null, dnScreen = null;
        if (orbit.NodeCrossings(out Vector3 asc, out Vector3 desc))
        {
            anScreen = toScreen(asc);
            dnScreen = toScreen(desc);
            _map.AddDot(anScreen.Value, 4f, t.navPlane);
            _map.AddDot(dnScreen.Value, 4f, t.navPlane);
        }

        // Live ship position.
        Vector2? shipScreen = null;
        if (Game.Clock != null && orbit.RelativeStateAt(Game.Clock.SimSeconds, out Vector3 shipRel, out Vector3 _))
        {
            shipScreen = toScreen(shipRel);
        }

        // Track layer (roadmap item 2): under the live ship marker, over the catalogue/orbit lines.
        DrawTrackLayer(orbit, shipScreen, toScreen);

        if (shipScreen.HasValue) _map.AddDot(shipScreen.Value, 5f, t.accent);

        _map.Rebuild();
        PlaceMarkerLabels(peScreen, apScreen, anScreen, dnScreen, shipScreen);

        // Readout.
        UIKit.SetText(_primary, Loc.Get("ui.nav.primary", orbit.PrimaryName));
        float semiMajorAu = Mathf.Abs((float)orbit.SemiMajorAxis) / gu;
        UIKit.SetText(_shape, Loc.Get("ui.nav.shape", Loc.Distance(semiMajorAu), orbit.Eccentricity));
        UIKit.SetText(_period, bound ? Loc.Get("ui.nav.period", orbit.Elements.orbitalPeriod / 86400.0) : "");
        UIKit.SetText(_nu, Loc.Get("ui.nav.nu", orbit.TrueAnomalyDeg));
        UIKit.SetText(_incl, Loc.Get("ui.nav.incl", orbit.Elements.inclination));

        DrawDial(orbit.Elements.inclination);
    }

    /// <summary>Catalogued bodies orbiting the ship's current primary (Catalogue.CollectOrbitsAroundPrimary):
    /// dim orbit ellipse plus a marker at the body's actual position now. Never reads SensorSight or a live
    /// CelestialBody - see the class doc.</summary>
    private void DrawCatalogueLayer(ShipOrbit orbit, Func<Vector3, Vector2> toScreen)
    {
        if (!_showCatalogue) { HideCatalogueLabels(); return; }

        Catalogue.CollectOrbitsAroundPrimary(orbit.PrimaryIndex, _catalogueScratch);
        double simSeconds = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
        UITheme t = UITheme.Current;

        int shown = 0;
        for (int i = 0; i < _catalogueScratch.Count; i++)
        {
            Catalogue.CataloguedOrbit c = _catalogueScratch[i];

            var pts = new List<Vector2>(CatalogueEllipsePoints + 1);
            for (int k = 0; k <= CatalogueEllipsePoints; k++)
            {
                double nu = k / (double)CatalogueEllipsePoints * Math.PI * 2.0;
                pts.Add(toScreen(KeplerOrbit.OffsetAtTrueAnomaly(c.orbit, nu)));
            }
            _map.AddPolyline(pts, 1f, t.navCatalogue, true);

            Vector2 dot = toScreen(KeplerOrbit.OffsetAt(c.orbit, simSeconds));
            _map.AddDot(dot, 3f, t.navCatalogue);

            TextMeshProUGUI label = PooledLabel(_catalogueLabels, shown, t.navCatalogue);
            UIKit.SetText(label, c.name);
            PlaceFree(label, dot, new Vector2(6f, 6f));
            shown++;
        }
        for (int i = shown; i < _catalogueLabels.Count; i++) _catalogueLabels[i].gameObject.SetActive(false);
    }

    /// <summary>Every known track: a bearing-only contact draws as a dashed ray from the ship, a ranged one as a
    /// dot with a 1-sigma tick along the bearing line (TrackManager.BestRange - own-ship's own uncertainty, not
    /// drawn as a precise position). Colour follows the same searching/locked/selected convention as the
    /// waterfall (UITheme.WaterfallTrackColor).</summary>
    private void DrawTrackLayer(ShipOrbit orbit, Vector2? shipScreen, Func<Vector3, Vector2> toScreen)
    {
        if (!_showTracks || Game.State == null) { HideTrackLabels(); return; }

        TrackManager tm = Game.State.Tracks;
        IList<Track> tracks = tm.All;
        UITheme t = UITheme.Current;

        SystemManager sm = SystemManager.Current;
        bool havePrimary = sm != null && sm.CurrentData != null && orbit.PrimaryIndex >= 0;
        Vector3 primaryPos = havePrimary
            ? sm.CurrentData.PositionOf(orbit.PrimaryIndex, Game.Clock != null ? Game.Clock.SimSeconds : 0.0)
            : Vector3.zero;

        int shown = 0;
        for (int i = 0; i < tracks.Count; i++)
        {
            Track tr = tracks[i];
            bool locked = tr.status == TrackStatus.Confirmed;
            Color color = t.WaterfallTrackColor(tr.id == tm.SelectedId, !locked);
            float rad = tr.bearing * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));

            Vector2? mark = null;
            if (havePrimary && locked && tr.range.Observable)
            {
                RangeEstimate re = tr.range;
                Vector2 dot = toScreen(new Vector3((float)(re.x - primaryPos.x), 0f, (float)(re.z - primaryPos.z)));
                _map.AddDot(dot, 4f, color);
                float sigmaPx = (float)(re.rangeSigma / GameConstants.GAME_UNITS_PER_UA) * _pixelsPerAu;
                _map.AddLine(dot - dir * sigmaPx, dot + dir * sigmaPx, 1f, color);
                mark = dot;
            }
            else if (shipScreen.HasValue)
            {
                Vector2 far = shipScreen.Value + dir * BearingRayPx;
                _map.AddDashedPolyline(new List<Vector2> { shipScreen.Value, far }, 1f, color, 6f, 5f);
                mark = far;
            }

            if (mark.HasValue)
            {
                TextMeshProUGUI label = PooledLabel(_trackLabels, shown, color);
                UIKit.SetText(label, tr.name);
                PlaceFree(label, mark.Value, new Vector2(6f, -6f));
                shown++;
            }
        }
        for (int i = shown; i < _trackLabels.Count; i++) _trackLabels[i].gameObject.SetActive(false);
    }

    /// <summary>Gets (creating if needed) the label at `index` of a pool, shows it and sets its colour. Pools
    /// grow to the largest count seen and are trimmed by hiding, not destroying, so redraws don't churn objects.</summary>
    private TextMeshProUGUI PooledLabel(List<TextMeshProUGUI> pool, int index, Color color)
    {
        while (pool.Count <= index) pool.Add(MapLabel(_mapRect, "", color));
        TextMeshProUGUI label = pool[index];
        label.color = color;
        label.gameObject.SetActive(true);
        return label;
    }

    /// <summary>Places a pooled label (anchored 0.5,0.5, pivot 0,0.5 - see MapLabel) at a free-floating screen
    /// point plus a small pixel offset, the same coordinate fix-up Place() does for the fixed marker labels.</summary>
    private void PlaceFree(TextMeshProUGUI label, Vector2 screenPos, Vector2 offset)
        => label.rectTransform.anchoredPosition = screenPos - _mapRect.rect.center + offset;

    private void HideCatalogueLabels()
    {
        for (int i = 0; i < _catalogueLabels.Count; i++) _catalogueLabels[i].gameObject.SetActive(false);
    }

    private void HideTrackLabels()
    {
        for (int i = 0; i < _trackLabels.Count; i++) _trackLabels[i].gameObject.SetActive(false);
    }

    private void HideMarkerLabels()
    {
        _peLabel.gameObject.SetActive(false);
        _apLabel.gameObject.SetActive(false);
        _anLabel.gameObject.SetActive(false);
        _dnLabel.gameObject.SetActive(false);
        _shipLabel.gameObject.SetActive(false);
    }

    private void PlaceMarkerLabels(Vector2? pe, Vector2? ap, Vector2? an, Vector2? dn, Vector2? ship)
    {
        Place(_peLabel, pe);
        Place(_apLabel, ap);
        Place(_anLabel, an);
        Place(_dnLabel, dn);
        Place(_shipLabel, ship);
    }

    /// <summary>screenPos is in _mapRect's own local space (what MapCanvas draws in, i.e. already includes the
    /// +center offset). The label is a child anchored at (0.5, 0.5), so its anchoredPosition is relative to
    /// _mapRect's centre instead - subtract it back out here rather than at every call site.</summary>
    private void Place(TextMeshProUGUI label, Vector2? screenPos)
    {
        bool show = screenPos.HasValue;
        label.gameObject.SetActive(show);
        if (show) label.rectTransform.anchoredPosition = screenPos.Value - _mapRect.rect.center + new Vector2(9f, 9f);
    }

    /// <summary>Upper-semicircle gauge: incl 0 deg = left (equatorial, prograde), 90 = top (polar), 180 = right
    /// (equatorial, retrograde). A top-down orbit plot can't show inclination as tilt, so it gets this instead.</summary>
    private void DrawDial(float inclDeg)
    {
        UITheme t = UITheme.Current;
        _dial.Clear();
        Vector2 c = new Vector2(0f, -60f); // pivot near the bottom of the widget's rect
        float r = 90f;
        _dial.AddArc(c, r, -90f, 90f, 2f, t.textDim, 40);
        _dial.AddLine(c + new Vector2(-r, 0f), c + new Vector2(-r - 8f, 0f), 2f, t.textDim);
        _dial.AddLine(c + new Vector2(0f, r), c + new Vector2(0f, r + 8f), 2f, t.textDim);
        _dial.AddLine(c + new Vector2(r, 0f), c + new Vector2(r + 8f, 0f), 2f, t.textDim);
        if (!float.IsNaN(inclDeg))
        {
            float gaugeDeg = -90f + Mathf.Clamp(inclDeg, 0f, 180f);
            Vector2 dir = new Vector2(Mathf.Sin(gaugeDeg * Mathf.Deg2Rad), Mathf.Cos(gaugeDeg * Mathf.Deg2Rad));
            _dial.AddLine(c, c + dir * (r * 0.9f), 3f, t.accent);
            _dial.AddDot(c, 4f, t.accent);
        }
        _dial.Rebuild();
    }

    private void AutoFit(ShipOrbit orbit)
    {
        float halfPx = Mathf.Max(60f, Mathf.Min(_mapRect.rect.width, _mapRect.rect.height) * 0.42f);
        float gu = GameConstants.GAME_UNITS_PER_UA;
        float referenceAu = orbit.Hyperbolic
            ? Mathf.Max(0.01f, orbit.PeriapsisGame / gu * 6f)
            : Mathf.Max(0.01f, orbit.ApoapsisGame / gu);
        _pixelsPerAu = Mathf.Clamp(halfPx / referenceAu, MinPxPerAu, MaxPxPerAu);
    }
    #endregion
}
