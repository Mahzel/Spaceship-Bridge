using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// NAV: graphical plot of the ship's own orbit (handoff-navigation-ui.md, roadmap item 1). The default minor
/// mode of Navigation (UI shell rework) - NavigationMode builds this to fill its own tab body. OrbitPanel (the
/// ship's own orbit) and TrackOrbitPanel (the selected track's fitted orbit) are built as corner overlays
/// INSIDE the map rect itself - bottom-right and bottom-left respectively - rather than pinned to the whole
/// tab body, so they sit over open map space instead of behind the sidebar's own bottom controls. No longer a
/// full-screen dim+panel overlay with its own Back button: OnShown()/Refresh() replace the old Open/Close/
/// IsOpen toggle, and visibility is entirely NavigationMode's tab-switch, not this class's own.
///
/// Gated by the NavComputer loadout tier (NavTier.HasSystemView) - unfitted, the tab still shows but with
/// only a "not fitted" message, same pattern as ui.screen.notfitted for sensors.
///
/// Own-ship orbit is exact (ShipOrbit's conic, not a sensor read): the ellipse, Pe/Ap, the reference-plane
/// crossings (AN/DN) and a live true-anomaly marker. Catalogue and track layers (roadmap item 2) are drawn on
/// top, toggle-able, and never touch SensorSight or a live CelestialBody - catalogue orbits come from
/// Catalogue.CollectOrbitsAroundPrimary (the generated SystemData elements), tracks from TrackManager's own
/// bearing/range estimates. Clicking a track here selects it exactly like ContactsScreen's row click (Track
/// strip, radar TRACK mode and Game.State.TargetBodyName all follow). The sidebar's TRANSFER section
/// (roadmap item 5) previews and arms a Hohmann transfer to that target: phase angle now vs. the next window,
/// wait time, Δv and time of flight (ManeuverPlan.ComputeTransferWindow), then CREATE NODES arms the same
/// two-burn plan NodePanel's "plot a transfer" does (ManeuverPlan.SolveHohmann) but timed to the window
/// instead of firing from right now. The target's orbit is never looked up from NodeData/the catalogue: it's
/// OrbitFit.TryFit off the selected track's own range estimate, so no identification is required (a range is
/// enough) and a bad fit makes a bad burn, on purpose. Node placement on the map itself, the predicted-path
/// overlay, the timeline and the galaxy map are later items on the same roadmap and are NOT drawn here yet.
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
    private const float TrackClickRadiusPx = 14f; // how close a click must land on a track's marker to select it

    private RectTransform _rootRect, _mapRect, _dialRect;
    private MapCanvas _map, _dial;
    // Corner overlays living INSIDE the map rect itself (not the whole NAV tab) so they never compete with the
    // sidebar for space: OrbitPanel (ship's own orbit) bottom-right, TrackOrbitPanel (selected track's fitted
    // orbit) bottom-left. NavigationMode used to build OrbitPanel into the whole tab body instead, which put
    // its bottom-right corner behind the sidebar's own bottom controls whenever both landed near the same spot.
    private readonly OrbitPanel _orbit = new OrbitPanel();
    private readonly TrackOrbitPanel _targetOrbit = new TrackOrbitPanel();
    private TextMeshProUGUI _notFitted, _incl;
    private TextMeshProUGUI _peLabel, _apLabel, _anLabel, _dnLabel, _shipLabel;
    private Button _zoomIn, _zoomOut, _layerCatalogue, _layerTracks;

    // Layer toggles (roadmap item 2). Pooled labels grow to fit however many catalogue bodies / tracks exist.
    private bool _showCatalogue = true, _showTracks = true;
    private readonly List<TextMeshProUGUI> _catalogueLabels = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> _trackLabels = new List<TextMeshProUGUI>();
    private readonly List<Catalogue.CataloguedOrbit> _catalogueScratch = new List<Catalogue.CataloguedOrbit>();
    // Screen position of every track drawn this redraw, for click-to-select (DrawTrackLayer fills it, OnMapClicked reads it).
    private readonly List<(int id, Vector2 pos)> _trackHits = new List<(int, Vector2)>();

    // Transfer helper (roadmap item 5): reads Game.State.Tracks.SelectedId, the same selection ContactsScreen
    // sets when the player clicks a track there (or a track clicked directly on this map) - no identification
    // required, see OrbitFit. TargetBodyName is only read for the display label.
    private TextMeshProUGUI _transferHeader, _transferTarget, _transferPhase, _transferWindow, _transferDv;
    private Button _transferButton;

    private float _pixelsPerAu = 40f;
    private Vector2 _panOffsetPx; // drag-to-pan, screen pixels from the map's own centre; reset on re-fit
    private bool _fitted; // whether AutoFit ran for the orbit currently on screen
    private float _redrawAccum = RedrawInterval;

    // -----------------------------------------------------------------------------------------------------
    #region Build
    /// <summary>Fills whatever tab-body rect NavigationMode gives it. No background of its own (matching
    /// NodePanel/JumpPanel's style) - it's already inside NavigationMode's own panel.</summary>
    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform prt = UIKit.Node("Nav", parent);
        UIKit.Stretch(prt);
        _rootRect = prt;

        var v = UIKit.VStack(prt, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;

        _notFitted = UIKit.AddLabel(prt, "", t.fontSizeBody, t.textDim);
        _notFitted.textWrappingMode = TextWrappingModes.Normal;
        UIKit.Size(_notFitted.rectTransform, preferredWidth: 800f);

        // Outer node: ONLY a LayoutElement, same decoupling as BuildSidebar below (see its comment) - the
        // HStack that actually arranges Map+Sidebar goes on a separate Stretch-filled child, not this node,
        // so it can't report its own children-derived size back up to prt's VStack.
        RectTransform body = UIKit.Node("Body", prt);
        UIKit.Size(body, flexibleWidth: 1f, flexibleHeight: 1f);

        RectTransform bodyInner = UIKit.Node("Inner", body);
        UIKit.Stretch(bodyInner);
        // expandWidth: false - the sidebar is a fixed 340px, not an equal partner to split space with the
        // map; see GameShell's identical fix for why expandWidth:true would balloon it.
        var bodyH = UIKit.HStack(bodyInner, t.spacing * 2f, 0, expandWidth: false);
        bodyH.childAlignment = TextAnchor.UpperLeft;

        BuildMap(bodyInner);
        BuildSidebar(bodyInner, t);

        return prt.gameObject;
    }

    private void BuildMap(Transform parent)
    {
        _mapRect = UIKit.Node("Map", parent);
        UIKit.Size(_mapRect, flexibleWidth: 1f, minHeight: 600f, flexibleHeight: 1f);
        // RectMask2D on THIS node clips everything drawn/positioned inside it (MapCanvas, marker labels, the
        // corner overlays) to the map's own bounds - a catalogue orbit far bigger than whatever the current
        // zoom was fit to (Saturn's, say, next to a sub-1AU ship orbit) used to render straight through the
        // sidebar and up over the topbar instead of just running off the edge like a real scope would.
        _mapRect.gameObject.AddComponent<RectMask2D>();

        // MapCanvas itself goes on a CHILD node, not this one: Unity's clip search starts at a Graphic's
        // PARENT, so a RectMask2D would not clip a Graphic sitting on its own same GameObject. The marker
        // labels/corner overlays built as actual children of _mapRect (below) don't need this - a mask on
        // their direct parent already clips them correctly.
        RectTransform canvasRect = UIKit.Node("Canvas", _mapRect);
        UIKit.Stretch(canvasRect);
        _map = canvasRect.gameObject.AddComponent<MapCanvas>();
        _map.raycastTarget = true; // clickable: selecting a track here mirrors ContactsScreen's row click
        var aim = canvasRect.gameObject.AddComponent<PointerAim>();
        aim.OnClick = OnMapClicked;
        // Drag to pan: the view can now run off zoomed-in content (or a huge catalogue orbit past the clip
        // edge) without losing track of it entirely.
        aim.OnDragged = (delta, local, rt) => _panOffsetPx += delta;

        _orbit.Build(_mapRect);
        _targetOrbit.Build(_mapRect);
    }

    /// <summary>Selects whichever track's last-drawn marker (DrawTrackLayer's _trackHits) is nearest the
    /// click, within TrackClickRadiusPx - same effect as ContactsScreen.Select(), so the Track strip, radar
    /// TRACK mode and the transfer target all follow a click here exactly as they follow one there.</summary>
    private void OnMapClicked(Vector2 local, RectTransform rt)
    {
        if (Game.State == null) return;
        int bestId = 0;
        float bestDist = TrackClickRadiusPx;
        for (int i = 0; i < _trackHits.Count; i++)
        {
            float d = Vector2.Distance(local, _trackHits[i].pos);
            if (d < bestDist) { bestDist = d; bestId = _trackHits[i].id; }
        }
        if (bestId == 0) return;

        TrackManager tm = Game.State.Tracks;
        tm.SelectedId = bestId;
        Track tr = tm.Find(bestId);
        // Display only - the transfer math (RefreshTransfer/CreateTransferNodes) reads the selected track's
        // own OrbitFit, not this name. No identification required, just a range.
        Game.State.SetTarget(tr != null ? tr.name : null);
    }

    private void BuildSidebar(Transform parent, UITheme t)
    {
        // Outer node: ONLY a LayoutElement (see GameShell.BuildSidebar's identical fix for why) - the VStack
        // that actually arranges the readouts/buttons goes on a separate Stretch-filled child instead of
        // this same node, so it can't report its own children-derived width back up to Body and override the
        // fixed 340px.
        RectTransform side = UIKit.Node("Sidebar", parent);
        UIKit.Size(side, preferredWidth: 340f);

        RectTransform inner = UIKit.Node("Inner", side);
        UIKit.Stretch(inner);
        var v = UIKit.VStack(inner, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;

        _incl = UIKit.AddLabel(inner, "", t.fontSizeSmall, t.text);
        UIKit.Size(_incl.rectTransform, minHeight: 20f);

        _dialRect = UIKit.Node("InclDial", inner);
        UIKit.Size(_dialRect, preferredWidth: 300f, minHeight: 160f);
        _dial = _dialRect.gameObject.AddComponent<MapCanvas>();

        UIKit.AddSpacer(inner, 6f);
        RectTransform zoomRow = UIKit.Node("Zoom", inner);
        UIKit.HStack(zoomRow, 6f, 0, expandWidth: true);
        _zoomOut = UIKit.AddButton(zoomRow, Loc.Get("ui.nav.zoomout"), () => Zoom(1f / ZoomStep), 0f, 34f);
        _zoomIn  = UIKit.AddButton(zoomRow, Loc.Get("ui.nav.zoomin"),  () => Zoom(ZoomStep), 0f, 34f);

        UIKit.AddSpacer(inner, 6f);
        RectTransform layerRow = UIKit.Node("Layers", inner);
        UIKit.HStack(layerRow, 6f, 0, expandWidth: true);
        _layerCatalogue = UIKit.AddButton(layerRow, Loc.Get("ui.nav.layer.catalogue"), ToggleCatalogue, 0f, 34f);
        _layerTracks    = UIKit.AddButton(layerRow, Loc.Get("ui.nav.layer.tracks"),    ToggleTracks,    0f, 34f);
        UIKit.SetButtonActive(_layerCatalogue, _showCatalogue);
        UIKit.SetButtonActive(_layerTracks, _showTracks);

        UIKit.AddSpacer(inner, 6f);
        // Explicit minHeight on every one of these: they're all built with EMPTY text (RefreshTransfer fills
        // them in later), and a plain TextMeshProUGUI with no text reports 0 preferredHeight to the
        // VerticalLayoutGroup at build time. That 0 doesn't reliably get corrected once real text arrives
        // later (unlike OrbitPanel's labels, which sit on a node that also carries its own ContentSizeFitter -
        // that extra component is what forces OrbitPanel's re-layout on every text change; this VStack has
        // none) - the visible symptom was every one of these rows collapsing onto its neighbour, text
        // literally overlapping. _transferHeader is the one exception with real text from the start, so it
        // never showed the bug, but it gets the same explicit height for consistency.
        _transferHeader = UIKit.AddLabel(inner, Loc.Get("ui.nav.transfer.header"), t.fontSizeBody, t.accent);
        UIKit.Size(_transferHeader.rectTransform, minHeight: 26f);
        _transferTarget = UIKit.AddLabel(inner, "", t.fontSizeSmall, t.text);
        UIKit.Size(_transferTarget.rectTransform, minHeight: 20f);
        _transferPhase  = UIKit.AddLabel(inner, "", t.fontSizeSmall, t.textDim);
        UIKit.Size(_transferPhase.rectTransform, minHeight: 20f);
        _transferWindow = UIKit.AddLabel(inner, "", t.fontSizeSmall, t.textDim);
        UIKit.Size(_transferWindow.rectTransform, minHeight: 20f);
        _transferDv     = UIKit.AddLabel(inner, "", t.fontSizeSmall, t.text);
        UIKit.Size(_transferDv.rectTransform, minHeight: 20f);
        _transferButton = UIKit.AddButton(inner, Loc.Get("ui.nav.transfer.create"), CreateTransferNodes, 0f, 34f);

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
    #region Shown / Refresh
    /// <summary>Called by NavigationMode whenever its tab row switches TO the NAV tab - replaces the old
    /// Open()'s "re-fit zoom to whatever orbit is current". Also forces a layout pass on the map rect before
    /// the next AutoFit reads it, in case this is the very first time the tab body's size has settled.</summary>
    public void OnShown()
    {
        _fitted = false;
        _panOffsetPx = Vector2.zero;
        _redrawAccum = RedrawInterval;
        if (_mapRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_mapRect);
    }

    /// <summary>Cheap enough to call every tick even while this tab isn't the one showing (NavigationMode
    /// refreshes every minor mode's body regardless, same convention as SystemsDock/SensorConsole).</summary>
    public void Refresh()
    {
        if (_rootRect == null) return;
        RunController run = Game.Run;
        if (run == null || run.Phase != RunPhase.Flight) return;

        int navLevel = Game.State != null ? Game.State.Loadout.Level(ProbeSystem.NavComputer) : 0;
        bool fitted = NavTier.HasSystemView(navLevel);
        _notFitted.gameObject.SetActive(!fitted);
        _mapRect.gameObject.SetActive(fitted);
        SetSidebarActive(fitted);
        _orbit.Refresh();
        _targetOrbit.Refresh();
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
        _incl.gameObject.SetActive(active);
        _dialRect.gameObject.SetActive(active);
        _zoomIn.gameObject.SetActive(active);
        _zoomOut.gameObject.SetActive(active);
        _layerCatalogue.gameObject.SetActive(active);
        _layerTracks.gameObject.SetActive(active);
        _transferHeader.gameObject.SetActive(active);
        _transferTarget.gameObject.SetActive(active);
        _transferPhase.gameObject.SetActive(active);
        _transferWindow.gameObject.SetActive(active);
        _transferDv.gameObject.SetActive(active);
        _transferButton.gameObject.SetActive(active);
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
        RefreshTransfer();

        if (orbit == null || !orbit.HasTrajectory)
        {
            HideMarkerLabels();
            HideCatalogueLabels();
            HideTrackLabels();
            _trackHits.Clear();
            UIKit.SetText(_incl, "");
            _map.Rebuild();
            DrawDial(float.NaN);
            return;
        }

        if (!_fitted) { AutoFit(orbit); _fitted = true; }

        float gu = GameConstants.GAME_UNITS_PER_UA;
        Vector2 center = _mapRect.rect.center + _panOffsetPx;
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

        // Predicted path (roadmap item 3): the ship's own orbit AFTER its next queued burn fires, dashed so it
        // reads as "not yet real" next to the current solid conic. Nothing drawn while nothing's armed.
        DrawPredictedPathLayer(toScreen);

        // Track layer (roadmap item 2): under the live ship marker, over the catalogue/orbit lines.
        DrawTrackLayer(orbit, shipScreen, toScreen);

        // Selected track's fitted orbit (roadmap item 6): over everything else so it reads as "this is what
        // we're looking at", under the live ship marker so that stays on top.
        DrawTargetOrbitLayer(toScreen);

        if (shipScreen.HasValue) _map.AddDot(shipScreen.Value, 5f, t.accent);

        _map.Rebuild();
        PlaceMarkerLabels(peScreen, apScreen, anScreen, dnScreen, shipScreen);

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

    /// <summary>Roadmap item 6's "dashed, widening with sigma" ellipse: the SELECTED track's fitted orbit
    /// (OrbitFit.TryFit - never NodeData/the catalogue, same rule as the transfer planner), drawn as a dashed
    /// ellipse in the track's own colour, with two fainter dashed ellipses bracketing it - the same orbit
    /// shape scaled by (1 +/- the track's own range sigma fraction) about the primary. A genuinely rough
    /// physical stand-in for positional uncertainty (not a real covariance propagation), but it does what the
    /// roadmap asked: the band visibly shrinks as the range estimate tightens, and vanishes once it's tight
    /// enough to not matter. No orbit at all if the track isn't selected or the fit fails (unranged, no
    /// primary in common, or a degenerate state vector) - same conditions TrackOrbitPanel already shows
    /// "No stable orbit" for, so the two never disagree.</summary>
    private void DrawTargetOrbitLayer(Func<Vector3, Vector2> toScreen)
    {
        if (Game.State == null) return;
        Track tr = Game.State.Tracks.Find(Game.State.Tracks.SelectedId);
        if (tr == null || !OrbitFit.TryFit(tr, out OrbitElements el)) return;

        UITheme t = UITheme.Current;
        Color color = t.WaterfallTrackColor(true, tr.status != TrackStatus.Confirmed);

        DrawFittedEllipse(el, toScreen, 2f, color);

        float sigmaFraction = tr.range.Observable && tr.range.range > 0.0
            ? Mathf.Clamp01((float)(tr.range.rangeSigma / tr.range.range)) : 0f;
        if (sigmaFraction > 0.01f)
        {
            Color band = color; band.a *= 0.35f;
            OrbitElements wide = el; wide.semiMajorAxis *= 1f + sigmaFraction;
            OrbitElements narrow = el; narrow.semiMajorAxis *= Mathf.Max(0.05f, 1f - sigmaFraction);
            DrawFittedEllipse(wide, toScreen, 1f, band);
            DrawFittedEllipse(narrow, toScreen, 1f, band);
        }
    }

    /// <summary>Roadmap item 3's predicted path: the ship's own orbit after its NEXT queued burn fires
    /// (ManeuverPlan.PreviewNode, the exact same non-destructive preview NodePanel's own PERI/APO/e/i readout
    /// already uses - this just also draws the shape it describes). Own-ship data, not a track, so no fit or
    /// uncertainty band - it's either armed or it isn't.</summary>
    private void DrawPredictedPathLayer(Func<Vector3, Vector2> toScreen)
    {
        ManeuverPlan mp = Game.State != null ? Game.State.Maneuver : null;
        if (mp == null || !mp.Armed || mp.Next == null) return;

        ManeuverPlan.Node next = mp.Next.Value;
        ManeuverPlan.Preview p = ManeuverPlan.PreviewNode(next.simSeconds, next.progradeKmS, next.normalKmS);
        if (!p.valid) return;

        DrawFittedEllipse(p.elements, toScreen, 2f, UITheme.Current.navNode);
    }

    private void DrawFittedEllipse(in OrbitElements el, Func<Vector3, Vector2> toScreen, float width, Color color)
    {
        var pts = new List<Vector2>(CatalogueEllipsePoints + 1);
        for (int k = 0; k <= CatalogueEllipsePoints; k++)
        {
            double nu = k / (double)CatalogueEllipsePoints * Math.PI * 2.0;
            pts.Add(toScreen(KeplerOrbit.OffsetAtTrueAnomaly(el, nu)));
        }
        _map.AddDashedPolyline(pts, width, color, 5f, 4f, true);
    }

    /// <summary>Every known track: a bearing-only contact draws as a dashed ray from the ship, a ranged one as a
    /// dot with a 1-sigma tick along the bearing line (TrackManager.BestRange - own-ship's own uncertainty, not
    /// drawn as a precise position). Colour follows the same searching/locked/selected convention as the
    /// waterfall (UITheme.WaterfallTrackColor).</summary>
    private void DrawTrackLayer(ShipOrbit orbit, Vector2? shipScreen, Func<Vector3, Vector2> toScreen)
    {
        _trackHits.Clear();
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
                _trackHits.Add((tr.id, mark.Value));
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

    /// <summary>Roadmap item 5 (transfer helper): phase angle now vs. the window, wait time, Δv and time of
    /// flight for a Hohmann transfer to the selected track (Game.State.Tracks.SelectedId - the same selection
    /// ContactsScreen's row click and OnMapClicked above both drive). The orbit itself comes from OrbitFit.TryFit
    /// off that track's own range estimate, never a catalog/NodeData lookup - no identification required, just
    /// a usable range. Read-only; CreateTransferNodes is the only thing that commits it. Runs every redraw
    /// tick, independent of whether the ship's own orbit has a trajectory, so the panel stays live even while
    /// that's blank.</summary>
    private void RefreshTransfer()
    {
        GameState state = Game.State;
        Track tr = state != null ? state.Tracks.Find(state.Tracks.SelectedId) : null;
        if (tr == null)
        {
            UIKit.SetText(_transferTarget, Loc.Get("ui.nav.transfer.none"));
            UIKit.SetText(_transferPhase, "");
            UIKit.SetText(_transferWindow, "");
            UIKit.SetText(_transferDv, "");
            _transferButton.interactable = false;
            return;
        }
        UIKit.SetText(_transferTarget, Loc.Get("ui.nav.transfer.target", tr.name));

        if (!OrbitFit.TryFit(tr, out OrbitElements target))
        {
            UIKit.SetText(_transferPhase, Loc.Get("ui.nav.transfer.unavailable"));
            UIKit.SetText(_transferWindow, "");
            UIKit.SetText(_transferDv, "");
            _transferButton.interactable = false;
            return;
        }

        double now = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
        ManeuverPlan.TransferWindow w = ManeuverPlan.ComputeTransferWindow(target, now);
        if (!w.valid)
        {
            UIKit.SetText(_transferPhase, Loc.Get("ui.nav.transfer.unavailable"));
            UIKit.SetText(_transferWindow, "");
            UIKit.SetText(_transferDv, "");
            _transferButton.interactable = false;
            return;
        }

        UIKit.SetText(_transferPhase, Loc.Get("ui.nav.transfer.phase", w.phaseNowDeg, w.phaseIdealDeg));
        UIKit.SetText(_transferWindow, w.waitSeconds < 3600.0
            ? Loc.Get("ui.nav.transfer.window.open")
            : Loc.Get("ui.nav.transfer.window.wait", Loc.Countdown(w.waitSeconds)));
        UIKit.SetText(_transferDv, Loc.Get("ui.nav.transfer.dv", w.departureDvKmS, w.arrivalDvKmS, w.transferTimeSeconds / 86400.0));
        _transferButton.interactable = true;
    }

    /// <summary>Recomputes the fit and the window fresh (both may have shifted since the last redraw tick) and
    /// arms the two burns SolveHohmann produces - NodePanel's own "plot a transfer" arms departure at NOW
    /// (phase-blind); this waits for the window ComputeTransferWindow found.</summary>
    private void CreateTransferNodes()
    {
        GameState state = Game.State;
        if (state == null || Game.Clock == null) return;
        Track tr = state.Tracks.Find(state.Tracks.SelectedId);
        if (tr == null || !OrbitFit.TryFit(tr, out OrbitElements target)) return;

        ManeuverPlan.TransferWindow w = ManeuverPlan.ComputeTransferWindow(target, Game.Clock.SimSeconds);
        if (!w.valid) return;

        if (ManeuverPlan.SolveHohmann(target, w.departSimSeconds, out ManeuverPlan.Node departure, out ManeuverPlan.Node arrival))
            state.Maneuver.SetPair(departure, arrival);
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
