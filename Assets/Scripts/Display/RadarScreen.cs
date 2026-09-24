using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders the radar sensor as a PPI scope: ship at the centre, world north (bearing 0) up, range rings out
/// to the selected scale. Fully code-built (UIKit), same plain-class pattern as the other console screens.
/// All the actual work (firing, the propagation countdown, CFAR, resolving) lives in RadarProcessor
/// (Game.State.Radar), ticked every real frame from RunDriver regardless of which console mode is showing.
/// Hide() is a no-op for the same reason.
///
/// What the scope shows:
///  - sweep returns as dots that fade with age (RadarProcessor.Freshness). A marked one turns "good" green;
///  - every track as a bearing line (red while searching), with a dot at its fused range once it has one;
///  - the aimed sweep sector, and while a ping is out, the growing echo-horizon arc.
/// Clicking a return marks a new track from it, seeded with the return's range and elevation (or selects the
/// track already marked from it). Clicking anywhere else aims the sweep (SWEEP mode) or selects the nearest
/// track line (TRACK mode).
///
/// Aiming (SWEEP): X = bearing (click, drag round the scope, Left/Right arrows), Y = elevation (EL -/+,
/// Up/Down arrows). The scope is a top-down view, so elevation lives in the readout, not on the picture.
/// In TRACK mode the beam follows the track in both axes; the manual elevation is only used when the track
/// has no elevation yet. Shift makes keys fine.
/// </summary>
public sealed class RadarScreen
{
    private const int ScopeSize = 600; // was 440 - grown to use more of the tab's vertical room, square scope
                                        // stays square; the controls column narrowed to make space (see Build)
    private const float ScopeMargin = 8f;
    private const float RedrawInterval = 0.1f;
    private const float ReturnPickPx = 10f;
    private const float TrackPickDeg = 5f;
    private const float FallbackBeamMin = 5f, FallbackBeamMax = 90f;
    private const float ElStepDeg = 2f;
    private const float AzKeyDegPerSec = 45f, ElKeyDegPerSec = 15f;
    private static readonly float[] FallbackScales = { 5f, 20f, 60f, 200f };

    private RadarPingMode _uiMode = RadarPingMode.Sweep;
    private float _aimBearingDeg;
    private float _beamHalfWidthDeg = 30f;
    private float _aimElevationDeg;
    private int _scaleIndex = 1;

    private GameObject _root;
    private RawImage _scope;
    private Texture2D _texture;
    private Color32[] _px;
    private float[] _pixBearing; // per pixel: world bearing [0, 360)
    private float[] _pixRadius;  // per pixel: fraction of the scope radius (0 centre, 1 edge)
    private float _redrawAccum = RedrawInterval;
    private string _flash;      // a transient message (e.g. "track list full") shown in place of the hint
    private float _flashHold;

    private Button _sweepModeButton, _trackModeButton, _fireButton, _cancelButton;
    private TextMeshProUGUI _fireLabel;
    private Button[] _scaleButtons;
    private TextMeshProUGUI _widthLabel, _targetLabel, _statusLabel, _ringsLabel, _hintLabel, _returnsLabel, _elLabel;
    private RectTransform _widthRow, _targetRow;

    private RadarProcessor Processor { get { return Game.State != null ? Game.State.Radar : null; } }

    private float[] Scales
    {
        get
        {
            RadarProcessor p = Processor;
            return p != null && p.spec.rangeScalesAu != null && p.spec.rangeScalesAu.Length > 0
                 ? p.spec.rangeScalesAu : FallbackScales;
        }
    }

    // Captured once at Build so the buttons and the scale they select can't disagree if the spec shows up later.
    private float[] _scales = FallbackScales;
    private float ScaleAu { get { return _scales[Mathf.Clamp(_scaleIndex, 0, _scales.Length - 1)]; } }

    // ---------------------------------------------------------------------
    #region Build
    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform root = UIKit.Node("Radar", parent);
        _root = root.gameObject;
        UIKit.Size(root, flexibleWidth: 1f);
        var h = UIKit.HStack(root, t.spacing * 2f, 0);
        h.childAlignment = TextAnchor.UpperLeft;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        BuildScope(root);

        RectTransform side = UIKit.Node("Controls", root);
        UIKit.Size(side, preferredWidth: 340f); // was 400 - the elevation row's own content (label+steppers+
                                                 // the longest EL readout string) needs ~340, so that's the
                                                 // floor; trimming further would overlap the "+" stepper
        var v = UIKit.VStack(side, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;

        BuildModeRow(side);
        BuildScaleRow(side);
        BuildWidthRow(side);
        BuildElevationRow(side);
        BuildTargetRow(side);
        BuildFireRow(side);
        _statusLabel = UIKit.AddLabel(side, "", t.fontSizeSmall, t.text);
        _returnsLabel = UIKit.AddLabel(side, "", t.fontSizeSmall, t.textDim);
        _ringsLabel = UIKit.AddLabel(side, "", t.fontSizeSmall, t.textDim);
        _hintLabel = UIKit.AddLabel(side, "", t.fontSizeSmall, t.textDim);
        _hintLabel.textWrappingMode = TextWrappingModes.Normal;

        _scaleIndex = Mathf.Clamp(_scaleIndex, 0, _scales.Length - 1);
        return root.gameObject;
    }

    private void BuildScope(Transform parent)
    {
        RectTransform area = UIKit.Node("ScopeArea", parent);
        UIKit.Size(area, preferredWidth: ScopeSize, minHeight: ScopeSize);

        _scope = UIKit.AddRawImage(area, "Scope", Color.white);
        UIKit.Stretch(_scope.rectTransform);
        _scope.raycastTarget = true;
        var aim = _scope.gameObject.AddComponent<PointerAim>();
        aim.OnClick = OnScopeClicked;
        aim.OnDragged = OnScopeDragged;

        _texture = new Texture2D(ScopeSize, ScopeSize, TextureFormat.RGBA32, false);
        _texture.filterMode = FilterMode.Point;
        _scope.texture = _texture;
        _px = new Color32[ScopeSize * ScopeSize];

        // Per-pixel polar coordinates, computed once: the per-frame passes (sector fill, background) then
        // never call atan2.
        _pixBearing = new float[_px.Length];
        _pixRadius = new float[_px.Length];
        float c = ScopeSize * 0.5f, r = c - ScopeMargin;
        for (int y = 0; y < ScopeSize; y++)
            for (int x = 0; x < ScopeSize; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                int i = y * ScopeSize + x;
                _pixRadius[i] = Mathf.Sqrt(dx * dx + dy * dy) / r;
                _pixBearing[i] = BearingMath.Wrap360(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg);
            }
    }

    private void BuildModeRow(Transform parent)
    {
        RectTransform row = UIKit.Node("Mode", parent);
        UIKit.HStack(row, 6f, 0, expandWidth: true);
        _sweepModeButton = UIKit.AddButton(row, Loc.Get("ui.radar.mode.sweep"), () => _uiMode = RadarPingMode.Sweep, 0f, 34f);
        _trackModeButton = UIKit.AddButton(row, Loc.Get("ui.radar.mode.track"), () => _uiMode = RadarPingMode.Track, 0f, 34f);
    }

    private void BuildScaleRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform row = UIKit.Node("Scale", parent);
        var h = UIKit.HStack(row, 4f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;
        UIKit.AddLabel(row, Loc.Get("ui.radar.scale"), t.fontSizeSmall, t.textDim);
        _scales = Scales;
        float[] scales = _scales;
        _scaleButtons = new Button[scales.Length];
        for (int i = 0; i < scales.Length; i++)
        {
            int idx = i;
            _scaleButtons[i] = UIKit.AddButton(row, Loc.Get("ui.radar.scale.value", scales[i]), () => _scaleIndex = idx, 64f, 30f);
            TextMeshProUGUI lbl = _scaleButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.fontSize = t.fontSizeSmall;
        }
    }

    private void BuildWidthRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform row = UIKit.Node("Width", parent);
        _widthRow = row;
        var h = UIKit.HStack(row, 4f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;
        UIKit.AddLabel(row, Loc.Get("ui.radar.beam"), t.fontSizeSmall, t.textDim);
        UIKit.AddButton(row, "-", () => AdjustBeamWidth(-5f), 28f, 28f);
        _widthLabel = UIKit.AddLabel(row, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_widthLabel.rectTransform, preferredWidth: 60f);
        UIKit.AddButton(row, "+", () => AdjustBeamWidth(5f), 28f, 28f);
    }

    private void BuildElevationRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform row = UIKit.Node("Elevation", parent);
        var h = UIKit.HStack(row, 4f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;
        UIKit.AddLabel(row, Loc.Get("ui.aim.el"), t.fontSizeSmall, t.textDim);
        UIKit.AddButton(row, "-", () => AdjustElevation(-ElStepDeg), 28f, 28f);
        _elLabel = UIKit.AddLabel(row, "", t.fontSizeSmall, t.text);
        UIKit.Size(_elLabel.rectTransform, preferredWidth: 250f);
        UIKit.AddButton(row, "+", () => AdjustElevation(ElStepDeg), 28f, 28f);
    }

    private void AdjustElevation(float delta)
    {
        RadarProcessor p = Processor;
        float e = _aimElevationDeg + delta;
        _aimElevationDeg = p != null ? p.ClampTilt(e) : Mathf.Clamp(e, -85f, 85f);
    }

    private void BuildTargetRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform row = UIKit.Node("Target", parent);
        _targetRow = row;
        var h = UIKit.HStack(row, 6f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;
        _targetLabel = UIKit.AddLabel(row, "", t.fontSizeSmall, t.text);
        row.gameObject.SetActive(false);
    }

    private void BuildFireRow(Transform parent)
    {
        RectTransform row = UIKit.Node("Fire", parent);
        UIKit.HStack(row, 6f, 0);
        _fireButton = UIKit.AddButton(row, Loc.Get("ui.radar.fire"), OnFireClicked, 160f, 36f);
        _fireLabel = _fireButton.GetComponentInChildren<TextMeshProUGUI>();
        _cancelButton = UIKit.AddButton(row, Loc.Get("ui.radar.cancel"), () => { if (Processor != null) Processor.CancelPending(); }, 100f, 36f);
    }
    #endregion

    // ---------------------------------------------------------------------
    #region Input
    private void AdjustBeamWidth(float delta)
    {
        RadarProcessor p = Processor;
        float min = p != null ? p.spec.sweepBeamMinDeg : FallbackBeamMin;
        float max = p != null ? p.spec.sweepBeamMaxDeg : FallbackBeamMax;
        _beamHalfWidthDeg = Mathf.Clamp(_beamHalfWidthDeg + delta, min, max);
    }

    private void OnFireClicked()
    {
        RadarProcessor p = Processor;
        if (p == null || Game.State == null) return;

        string whyNot;
        if (_uiMode == RadarPingMode.Sweep) p.FireSweep(_aimBearingDeg, _beamHalfWidthDeg, ScaleAu, _aimElevationDeg, out whyNot);
        else p.FireTrack(Game.State.Tracks.SelectedId, _aimElevationDeg, out whyNot);
        _redrawAccum = RedrawInterval;
    }

    private void OnScopeClicked(Vector2 local, RectTransform rt)
    {
        if (Game.State == null) return;
        float half = rt.rect.width * 0.5f;
        if (half <= 1f) return;
        // Local point is relative to the pivot (centre). Convert to texture pixels, then to polar.
        float k = ScopeSize / rt.rect.width;
        float px = local.x * k, py = local.y * k;
        float rPix = ScopeSize * 0.5f - ScopeMargin;
        float bearing = BearingMath.Wrap360(Mathf.Atan2(px, py) * Mathf.Rad2Deg);

        // 1) A return under the cursor? Mark it (or select the track already marked from it).
        RadarProcessor p = Processor;
        if (p != null)
        {
            RadarReturn hit = null;
            float bestD = ReturnPickPx * k;
            IList<RadarReturn> rets = p.Returns;
            for (int i = 0; i < rets.Count; i++)
            {
                Vector2 rp;
                if (!ReturnToPixel(rets[i], rPix, out rp)) continue;
                float d = Vector2.Distance(rp, new Vector2(px, py));
                if (d < bestD) { bestD = d; hit = rets[i]; }
            }
            if (hit != null) { MarkFromReturn(hit); _redrawAccum = RedrawInterval; return; }
        }

        // 2) Otherwise: aim (sweep) or pick the nearest track line (track).
        if (_uiMode == RadarPingMode.Sweep)
        {
            _aimBearingDeg = bearing;
        }
        else
        {
            Track best = null; float bestDiff = TrackPickDeg;
            IList<Track> all = Game.State.Tracks.All;
            for (int i = 0; i < all.Count; i++)
            {
                float d = Mathf.Abs(BearingMath.Diff(all[i].bearing, bearing));
                if (d < bestDiff) { bestDiff = d; best = all[i]; }
            }
            if (best != null) Game.State.Tracks.SelectedId = best.id;
        }
        _redrawAccum = RedrawInterval;
    }

    // Drag round the scope to swing the sweep's bearing: the aim follows the pointer's angle from the centre.
    private void OnScopeDragged(Vector2 delta, Vector2 local, RectTransform rt)
    {
        if (_uiMode != RadarPingMode.Sweep) return;
        if (local.sqrMagnitude < 25f) return; // too close to the centre for a stable angle
        _aimBearingDeg = BearingMath.Wrap360(Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg);
        _redrawAccum = RedrawInterval;
    }

    private void MarkFromReturn(RadarReturn r)
    {
        TrackManager tm = Game.State.Tracks;
        if (r.markedTrackId != 0 && tm.Find(r.markedTrackId) != null)
        {
            tm.SelectedId = r.markedTrackId;
            return;
        }

        int maxTracks = Game.State.Waterfall != null ? Game.State.Waterfall.spec.maxTracks : 4;
        double now = Game.Clock != null ? Game.Clock.SimSeconds : r.echoSimTime;
        Track tr = tm.MarkBearing(now, r.bearingDeg, maxTracks);
        if (tr == null)
        {
            _flash = Loc.Get("ui.radar.trackfull");
            _flashHold = 3f;
            return;
        }

        double u = GameConstants.GAME_UNITS_PER_UA;
        ShipState s = Game.State.Ship;
        tm.ApplyRadarFix(tr.id, r.fireSimTime, r.rangeAu * u, r.rangeSigmaAu * u, r.bearingDeg, s.x, s.z, false, 0.0);
        tm.ApplyElevationFix(tr.id, r.fireSimTime, r.elevationDeg, r.elevationSigmaDeg, ElevationSource.Radar);
        tm.ApplySupport(tr.id, r.fireSimTime, ElevationSource.Radar, true, r.bearingDeg, r.bearingSigmaDeg,
                        s.x, s.z, (float)s.headingDeg);
        r.markedTrackId = tr.id;
        tm.SelectedId = tr.id;
    }
    #endregion

    // ---------------------------------------------------------------------
    #region Refresh
    /// <summary>No-op: like the waterfall, a pending ping keeps counting down whether or not this tab is
    /// showing. Only RadarProcessor.Tick (driven by RunDriver) governs that.</summary>
    public void Hide() { }

    public void Refresh(float unscaledDeltaSeconds)
    {
        if (_root == null || !_root.activeInHierarchy) return; // nothing to draw while another tab is up

        RadarProcessor p = Processor;
        bool sweep = _uiMode == RadarPingMode.Sweep;

        UIKit.SetButtonActive(_sweepModeButton, sweep);
        UIKit.SetButtonActive(_trackModeButton, !sweep);
        for (int i = 0; i < _scaleButtons.Length; i++) UIKit.SetButtonActive(_scaleButtons[i], i == _scaleIndex);

        if (_widthRow.gameObject.activeSelf != sweep) _widthRow.gameObject.SetActive(sweep);
        if (_targetRow.gameObject.activeSelf != !sweep) _targetRow.gameObject.SetActive(!sweep);

        Track selected = Game.State != null ? Game.State.Tracks.Find(Game.State.Tracks.SelectedId) : null;

        // Keyboard aim only while this tab is showing: Left/Right = bearing (sweep only), Up/Down = elevation.
        Vector2 slew = AimKeys.Slew();
        if (slew.x != 0f && sweep)
        {
            _aimBearingDeg = BearingMath.Wrap360(_aimBearingDeg + slew.x * AzKeyDegPerSec * unscaledDeltaSeconds);
            _redrawAccum = RedrawInterval;
        }
        if (slew.y != 0f) AdjustElevation(slew.y * ElKeyDegPerSec * unscaledDeltaSeconds);
        RefreshElevationLabel(p, sweep, selected);

        if (sweep) UIKit.SetText(_widthLabel, Loc.Get("ui.radar.beam.value", _beamHalfWidthDeg));
        else UIKit.SetText(_targetLabel, selected != null ? Loc.Get("ui.radar.target", selected.name) : Loc.Get("ui.radar.target.none"));

        // Firing while a ping is out re-pings: the one in flight is cancelled (RadarProcessor.CancelPending).
        _fireButton.interactable = p != null && (sweep || selected != null);
        bool pinging = p != null && p.Pinging;
        UIKit.SetText(_fireLabel, Loc.Get(pinging ? "ui.radar.reping" : "ui.radar.fire"));
        _cancelButton.interactable = pinging;

        UIKit.SetText(_ringsLabel, Loc.Get("ui.radar.rings", ScaleAu * 0.25f));
        UIKit.SetText(_returnsLabel, Loc.Get("ui.radar.returns", p != null ? p.Returns.Count : 0));
        if (_flashHold > 0f) { _flashHold -= unscaledDeltaSeconds; UIKit.SetText(_hintLabel, _flash); }
        else UIKit.SetText(_hintLabel, Loc.Get(sweep ? "ui.radar.hint.sweep" : "ui.radar.hint.track"));
        RefreshStatus(p);

        _redrawAccum += unscaledDeltaSeconds;
        if (_redrawAccum < RedrawInterval) return;
        _redrawAccum = 0f;
        DrawScope(p, selected);
    }

    private void RefreshElevationLabel(RadarProcessor p, bool sweep, Track selected)
    {
        float fan = p != null ? p.spec.sweepElevationHalfWidthDeg : 5f;
        if (sweep || selected == null)
        {
            UIKit.SetText(_elLabel, Loc.Get("ui.radar.el.manual", _aimElevationDeg, fan));
            return;
        }
        if (selected.hasElevation)
        {
            double now = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
            UIKit.SetText(_elLabel, Loc.Get("ui.radar.el.track", selected.elevationDeg, TrackManager.AgedElevationSigma(selected, now)));
        }
        else UIKit.SetText(_elLabel, Loc.Get("ui.radar.el.nofix", _aimElevationDeg, fan));
    }

    private void RefreshStatus(RadarProcessor p)
    {
        if (p == null) { UIKit.SetText(_statusLabel, ""); return; }
        if (p.Pinging) { UIKit.SetText(_statusLabel, Loc.Get("ui.radar.pinging", p.EtaRealSeconds)); return; }

        RadarPingResult r = p.LastResult;
        if (r == null) { UIKit.SetText(_statusLabel, ""); return; }
        if (r.mode == RadarPingMode.Sweep) { UIKit.SetText(_statusLabel, Loc.Get("ui.radar.result.sweep", r.returnCount)); return; }
        if (!r.hit) { UIKit.SetText(_statusLabel, Loc.Get("ui.radar.noreturn")); return; }
        string text = Loc.Get("ui.radar.result.rangerate.brg", r.rangeAu, r.radialVelocityKmS, r.bearingDeg);
        if (r.trackMoved) text += "  " + Loc.Get("ui.radar.result.moved");
        UIKit.SetText(_statusLabel, text);
    }
    #endregion

    // ---------------------------------------------------------------------
    #region Drawing
    private void DrawScope(RadarProcessor p, Track selected)
    {
        UITheme t = UITheme.Current;
        float scale = ScaleAu;
        float c = ScopeSize * 0.5f, rPix = c - ScopeMargin;

        Color32 outside = new Color32(0, 0, 0, 255);
        Color32 inside = t.barBack;
        Color sectorCol = t.accent; sectorCol.a = 0.14f;
        Color pingCol = t.accent; pingCol.a = 0.08f;

        // Which sector to shade: the one in flight if pinging, else (sweep mode) the one being aimed.
        bool shade = false; float sCentre = 0f, sHalf = 0f; Color shadeCol = sectorCol;
        if (p != null && p.Pinging && p.PendingMode == RadarPingMode.Sweep)
        { shade = true; sCentre = p.PendingBearingDeg; sHalf = p.PendingHalfWidthDeg; shadeCol = pingCol; }
        else if (_uiMode == RadarPingMode.Sweep)
        { shade = true; sCentre = _aimBearingDeg; sHalf = _beamHalfWidthDeg; }

        // Background + sector fill in one pass.
        for (int i = 0; i < _px.Length; i++)
        {
            float rr = _pixRadius[i];
            if (rr > 1f) { _px[i] = outside; continue; }
            Color32 col = inside;
            if (shade && Mathf.Abs(BearingMath.Diff(_pixBearing[i], sCentre)) <= sHalf) col = Blend(col, shadeCol);
            _px[i] = col;
        }

        // Rings and spokes.
        Color ringCol = t.accentDim;
        for (int k = 1; k <= 4; k++) DrawCircle(c, c, rPix * k / 4f, ringCol, 0f, 360f);
        Color spokeCol = t.accentDim; spokeCol.a = 0.5f;
        for (int b = 0; b < 360; b += 30) DrawRay(c, c, b, 0f, rPix, spokeCol, 1);
        DrawRay(c, c, 0f, rPix - 10f, rPix, t.textDim, 3); // north tick

        // Ship heading.
        if (Game.State != null)
        {
            Color hc = t.text; hc.a = 0.6f;
            DrawRay(c, c, (float)Game.State.Ship.headingDeg, 0f, rPix * 0.12f, hc, 3);
        }

        // Echo horizon arc of the ping in flight.
        if (p != null && p.Pinging)
        {
            float hr = (float)(p.EchoHorizonAu / scale) * rPix;
            if (hr <= rPix)
            {
                float half = p.PendingMode == RadarPingMode.Sweep ? p.PendingHalfWidthDeg : 3f;
                DrawCircle(c, c, hr, t.accent, p.PendingBearingDeg - half, p.PendingBearingDeg + half);
            }
        }

        // Tracks: bearing line, then a range dot + radial sigma bar if we have a usable range.
        if (Game.State != null)
        {
            IList<Track> all = Game.State.Tracks.All;
            for (int i = 0; i < all.Count; i++)
            {
                Track tr = all[i];
                bool isSel = selected != null && tr.id == selected.id;
                Color lc = tr.Locked ? t.text : t.danger;
                lc.a = isSel ? 0.9f : 0.35f;
                DrawRay(c, c, tr.bearing, rPix * 0.14f, rPix, lc, isSel ? 2 : 1);

                RangeEstimate re = tr.range;
                if (re.valid && (re.Observable || tr.radarFix.valid))
                {
                    float au = (float)(re.range / GameConstants.GAME_UNITS_PER_UA);
                    float sig = (float)(re.rangeSigma / GameConstants.GAME_UNITS_PER_UA);
                    if (au <= scale)
                    {
                        Color dc = tr.Locked ? (isSel ? t.accent : t.text) : t.danger;
                        DrawRay(c, c, tr.bearing, Mathf.Max(0f, (au - sig) / scale * rPix),
                                Mathf.Min(rPix, (au + sig) / scale * rPix), dc, 1);
                        DrawDot(PolarToPixel(tr.bearing, au / scale * rPix), 3, dc);
                    }
                }
            }
        }

        // Returns, fading with age.
        if (p != null)
        {
            IList<RadarReturn> rets = p.Returns;
            for (int i = 0; i < rets.Count; i++)
            {
                Vector2 rp;
                if (!ReturnToPixel(rets[i], rPix, out rp)) continue;
                Color rc = rets[i].markedTrackId != 0 ? t.good : t.warning;
                rc.a = Mathf.Lerp(0.15f, 1f, p.Freshness(rets[i]));
                DrawDot(new Vector2(rp.x + c, rp.y + c), 2, rc);
            }
        }

        _texture.SetPixels32(_px);
        _texture.Apply(false);
    }

    /// <summary>Return position relative to the scope centre, in texture pixels. False if beyond the scale.</summary>
    private bool ReturnToPixel(RadarReturn r, float rPix, out Vector2 rel)
    {
        float f = (float)(r.rangeAu / ScaleAu);
        rel = Vector2.zero;
        if (f > 1f) return false;
        float b = r.bearingDeg * Mathf.Deg2Rad;
        rel = new Vector2(Mathf.Sin(b), Mathf.Cos(b)) * (f * rPix);
        return true;
    }

    private static Vector2 PolarToPixel(float bearingDeg, float radiusPx)
    {
        float c = ScopeSize * 0.5f, b = bearingDeg * Mathf.Deg2Rad;
        return new Vector2(c + Mathf.Sin(b) * radiusPx, c + Mathf.Cos(b) * radiusPx);
    }

    private void DrawRay(float cx, float cy, float bearingDeg, float r0, float r1, Color col, int thickness)
    {
        float b = bearingDeg * Mathf.Deg2Rad, sx = Mathf.Sin(b), sy = Mathf.Cos(b);
        int half = (thickness - 1) / 2;
        for (float r = r0; r <= r1; r += 0.7f)
        {
            int x = Mathf.FloorToInt(cx + sx * r), y = Mathf.FloorToInt(cy + sy * r);
            for (int o = -half; o <= half; o++)
            {
                // Thicken perpendicular-ish: along x for mostly-vertical rays, along y otherwise.
                if (Mathf.Abs(sy) > Mathf.Abs(sx)) Plot(x + o, y, col); else Plot(x, y + o, col);
            }
        }
    }

    private void DrawCircle(float cx, float cy, float radius, Color col, float fromDeg, float toDeg)
    {
        if (radius <= 0.5f) return;
        float span = toDeg - fromDeg;
        int steps = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * radius * span / 360f * 1.4f));
        for (int s = 0; s <= steps; s++)
        {
            float b = (fromDeg + span * s / steps) * Mathf.Deg2Rad;
            Plot(Mathf.FloorToInt(cx + Mathf.Sin(b) * radius), Mathf.FloorToInt(cy + Mathf.Cos(b) * radius), col);
        }
    }

    private void DrawDot(Vector2 centre, int radius, Color col)
    {
        int cx = Mathf.FloorToInt(centre.x), cy = Mathf.FloorToInt(centre.y);
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                if (dx * dx + dy * dy <= radius * radius + 1) Plot(cx + dx, cy + dy, col);
    }

    private void Plot(int x, int y, Color col)
    {
        if (x < 0 || y < 0 || x >= ScopeSize || y >= ScopeSize) return;
        int i = y * ScopeSize + x;
        _px[i] = Blend(_px[i], col);
    }

    private static Color32 Blend(Color32 dst, Color src)
    {
        float a = Mathf.Clamp01(src.a);
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(dst.r, src.r * 255f, a)), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(dst.g, src.g * 255f, a)), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(dst.b, src.b * 255f, a)), 0, 255),
            255);
    }
    #endregion
}
