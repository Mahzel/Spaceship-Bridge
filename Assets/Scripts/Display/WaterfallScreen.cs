using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders the waterfall sensor. Fully code-built (UIKit), same plain-class pattern as the other three sensor
/// screens. All the actual work (line generation, CFAR, tracking) lives in WaterfallProcessor
/// (Game.State.Waterfall), ticked every real frame from RunDriver regardless of which console mode is showing
/// — this screen just draws whatever the processor already has. Unlike Imager/Spectrometer, Hide() is a
/// no-op: switching away from this mode does NOT pause detection, only the sensor's own power button does
/// that (see WaterfallProcessor's own doc comment for why).
///
/// Aiming: the array always hears every bearing, but only a fan of elevation around its TILT (Y). Tilt with
/// the TILT -/+ steppers, the Up/Down arrows, or by dragging the waterfall image vertically. X is a bearing
/// CURSOR, not a steer: Left/Right arrows move it, Enter or MARK seeds a track there. Clicking the image or
/// the DSP strip marks a bearing directly (and moves the cursor there). Shift makes keys fine.
/// With a track SELECTED, marking MOVES that track to the new bearing instead of creating one (the button
/// reads MOVE); deselect it (click its button in the Track panel again) to create new tracks.
///
/// Zoom (display only, WaterfallView): ZOOM -/+ or the mouse wheel over the image magnify a slice of the
/// circle (x1..x32); processing stays full-circle. Drag the image sideways to pan while zoomed; CTR centres
/// on the cursor; the cursor keys scroll the view to keep the cursor on screen. An azimuth scale under the
/// image labels the visible bearings.
/// </summary>
public sealed class WaterfallScreen
{
    private const int DisplayW = 860;
    private const int DisplayH = 440; // taller plot area (was 260) - UI sizing only, WaterfallTexture's own
                                       // pixel height comes from Bins/lines in WaterfallProcessor, unaffected
    private const int DspH = 70;
    private const float DspTickWidth = 1.5f;
    private const float TiltStepDeg = 5f;
    private const float TiltKeyDegPerSec = 20f;
    private const float TiltDragDegPerPx = 0.25f;
    private const float CursorKeyDegPerSec = 45f;

    private const float ScaleH = 18f;
    private static readonly float[] ScaleSteps = { 0.5f, 1f, 2f, 5f, 10f, 15f, 30f, 45f, 90f };
    private const int MaxScaleLabels = 16;

    private readonly WaterfallView _view = new WaterfallView();
    private RectTransform _scaleArea;
    private readonly List<TextMeshProUGUI> _scaleLabels = new List<TextMeshProUGUI>();
    private readonly List<Image> _scaleTicks = new List<Image>();
    private float _scaleCenter = float.NaN, _scaleSpan = float.NaN;
    private TextMeshProUGUI _zoomLabel;

    private GameObject _root;
    private Image _cursor;                    // full-height line, shown for a moment after the cursor moves
    private Image _cursorTickTop, _cursorTickBottom; // always-on edge ticks: they don't hide the history
    private float _cursorShownAt = -100f;     // unscaled time of the last cursor move
    private const float CursorLineHold = 3f, CursorLineFade = 0.6f, CursorLineAlpha = 0.7f;
    private float _cursorBearing;
    private TextMeshProUGUI _tiltLabel, _cursorLabel, _markLabel;

    private RawImage _image;
    private TrackOverlay _overlay;
    private LineGraphic _dspLine;
    private RectTransform _dspArea;
    private float[] _dspNormalized = new float[0];
    private readonly List<Image> _dspTicks = new List<Image>();

    private Button _powerButton, _integrationButton;
    private TextMeshProUGUI _powerLabel, _integrationLabel, _pixelLabel;
    private bool _integrationOn = true;
    private int _pixelSize = 1;

    private WaterfallProcessor Processor { get { return Game.State != null ? Game.State.Waterfall : null; } }

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform root = UIKit.Node("Waterfall", parent);
        _root = root.gameObject;
        UIKit.Size(root, flexibleWidth: 1f);
        var v = UIKit.VStack(root, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        BuildDisplay(root);
        BuildScale(root);
        BuildDsp(root);
        BuildControls(root);

        WaterfallProcessor p = Processor;
        if (p != null) _integrationOn = p.IntegrationOn;

        return root.gameObject;
    }

    private void BuildDisplay(Transform parent)
    {
        RectTransform area = UIKit.Node("DisplayArea", parent);
        UIKit.Size(area, preferredWidth: DisplayW, minHeight: DisplayH);

        _image = UIKit.AddRawImage(area, "Waterfall", Color.white);
        UIKit.Stretch(_image.rectTransform);
        _image.raycastTarget = true; // needed to receive the mark-a-bearing click and the tilt drag below
        var aim = _image.gameObject.AddComponent<PointerAim>();
        aim.OnClick = OnDisplayClicked;
        aim.OnDragged = (delta, local, rt) =>
        {
            Tilt(delta.y * TiltDragDegPerPx);
            if (_view.Zoomed && rt.rect.width > 1f) _view.Pan(-delta.x / rt.rect.width * _view.Span); // grab-pan
        };
        aim.OnScrolled = (wheel, local, rt) =>
        {
            float frac = rt.rect.width > 1f ? Mathf.Clamp01(local.x / rt.rect.width + 0.5f) : 0.5f;
            _view.ZoomBy(wheel > 0f ? 1 : -1, frac);
        };

        _overlay = new TrackOverlay();
        _overlay.Build(area);

        Color cc = UITheme.Current.warning; cc.a = CursorLineAlpha;
        _cursor = UIKit.AddPanel(area, "Cursor", cc);
        _cursor.raycastTarget = false;
        RectTransform crt = _cursor.rectTransform;
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(1.5f, 0f);

        cc.a = 1f;
        _cursorTickTop = CursorTick(area, "CursorTickTop", cc, 1f);
        _cursorTickBottom = CursorTick(area, "CursorTickBottom", cc, 0f);
    }

    private static Image CursorTick(Transform parent, string name, Color color, float edgeY)
    {
        Image tick = UIKit.AddPanel(parent, name, color);
        tick.raycastTarget = false;
        RectTransform rt = tick.rectTransform;
        rt.pivot = new Vector2(0.5f, edgeY);
        rt.sizeDelta = new Vector2(3f, 12f);
        return tick;
    }

    private void PlaceCursor()
    {
        float cx = _view.Frac(_cursorBearing);
        bool visible = _view.Visible(cx);

        float age = Time.unscaledTime - _cursorShownAt;
        float lineAlpha = age <= CursorLineHold ? 1f : 1f - Mathf.Clamp01((age - CursorLineHold) / CursorLineFade);
        bool lineOn = visible && lineAlpha > 0f;
        if (_cursor.gameObject.activeSelf != lineOn) _cursor.gameObject.SetActive(lineOn);
        if (lineOn)
        {
            Color c = _cursor.color; c.a = CursorLineAlpha * lineAlpha; _cursor.color = c;
            RectTransform crt = _cursor.rectTransform;
            crt.anchorMin = new Vector2(cx, 0f);
            crt.anchorMax = new Vector2(cx, 1f);
            crt.anchoredPosition = Vector2.zero;
        }

        foreach (Image tick in new[] { _cursorTickTop, _cursorTickBottom })
        {
            if (tick.gameObject.activeSelf != visible) tick.gameObject.SetActive(visible);
            if (!visible) continue;
            float y = tick == _cursorTickTop ? 1f : 0f;
            RectTransform rt = tick.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(cx, y);
            rt.anchoredPosition = Vector2.zero;
        }
    }

    // Azimuth scale: labelled ticks at "nice" bearing steps for the current view, rebuilt only when it changes.
    private void BuildScale(Transform parent)
    {
        _scaleArea = UIKit.Node("AzimuthScale", parent);
        UIKit.Size(_scaleArea, preferredWidth: DisplayW, minHeight: ScaleH);
        UITheme t = UITheme.Current;
        for (int i = 0; i < MaxScaleLabels; i++)
        {
            Image tick = UIKit.AddPanel(_scaleArea, "Tick", t.textDim);
            RectTransform tr = tick.rectTransform;
            tr.pivot = new Vector2(0.5f, 1f);
            tr.sizeDelta = new Vector2(1f, 5f);
            tick.gameObject.SetActive(false);
            _scaleTicks.Add(tick);

            TextMeshProUGUI lbl = UIKit.AddLabel(_scaleArea, "", 11, t.textDim, TextAlignmentOptions.Center);
            RectTransform lr = lbl.rectTransform;
            lr.pivot = new Vector2(0.5f, 1f);
            lr.sizeDelta = new Vector2(48f, 14f);
            lbl.gameObject.SetActive(false);
            _scaleLabels.Add(lbl);
        }
    }

    private void RefreshScale()
    {
        if (_view.Center == _scaleCenter && _view.Span == _scaleSpan) return;
        _scaleCenter = _view.Center;
        _scaleSpan = _view.Span;

        float span = _view.Span;
        float step = ScaleSteps[ScaleSteps.Length - 1];
        for (int i = 0; i < ScaleSteps.Length; i++)
            if (span / ScaleSteps[i] <= 12f) { step = ScaleSteps[i]; break; }

        // First multiple of `step` at or right of the left edge, in unwrapped degrees relative to the view.
        float left = _view.Center - 0.5f * span;
        float first = Mathf.Ceil(left / step) * step;
        int used = 0;
        for (float b = first; b <= left + span + 1e-3f && used < MaxScaleLabels; b += step)
        {
            float frac = (b - left) / span;
            if (frac < 0.015f || frac > 0.985f) continue; // an edge label would be half clipped
            Image tick = _scaleTicks[used];
            TextMeshProUGUI lbl = _scaleLabels[used];
            tick.gameObject.SetActive(true);
            lbl.gameObject.SetActive(true);
            RectTransform tr = tick.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(frac, 1f);
            tr.anchoredPosition = Vector2.zero;
            RectTransform lr = lbl.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(frac, 1f);
            lr.anchoredPosition = new Vector2(0f, -4f);
            float w = BearingMath.Wrap360(b);
            if (w > 359.95f) w = 0f;
            UIKit.SetText(lbl, step < 1f ? w.ToString("000.0") : w.ToString("000"));
            used++;
        }
        for (int i = used; i < MaxScaleLabels; i++)
        {
            if (_scaleTicks[i].gameObject.activeSelf) _scaleTicks[i].gameObject.SetActive(false);
            if (_scaleLabels[i].gameObject.activeSelf) _scaleLabels[i].gameObject.SetActive(false);
        }
    }

    private void Zoom(int steps)
    {
        float cf = _view.Frac(_cursorBearing);
        _view.ZoomBy(steps, _view.Visible(cf) ? cf : 0.5f);
    }

    private void BuildDsp(Transform parent)
    {
        UITheme t = UITheme.Current;
        Image areaImage = UIKit.AddPanel(parent, "DspArea", t.barBack);
        RectTransform area = areaImage.rectTransform;
        UIKit.Size(area, preferredWidth: DisplayW, minHeight: DspH);
        _dspArea = area;

        areaImage.raycastTarget = true; // the DSP strip can mark a bearing too
        var dspClick = area.gameObject.AddComponent<ClickForwarder>();
        dspClick.OnClick = OnDisplayClicked;

        RectTransform lineRt = UIKit.Node("DSPLine", area);
        UIKit.Stretch(lineRt, 2f, 2f, 2f, 2f);
        _dspLine = lineRt.gameObject.AddComponent<LineGraphic>();
        _dspLine.raycastTarget = false;
        _dspLine.color = t.accent;
    }

    // Click-to-mark: the display and the DSP strip are both full-width (DisplayW), so the same local-x-to-
    // world-bearing conversion applies to either. The waterfall is now world-bearing-centered (0,0 = the
    // rect's pivot, which UIKit.Node leaves at the default 0.5,0.5), so no heading offset is needed here.
    private void OnDisplayClicked(Vector2 localPoint, RectTransform rt)
    {
        if (Game.State == null) return;
        WaterfallProcessor p = Processor;
        if (p == null) return;

        float xFrac = Mathf.Clamp01(localPoint.x / rt.rect.width + 0.5f);
        float bearing = _view.Bearing(xFrac);
        _cursorBearing = bearing;
        _cursorShownAt = Time.unscaledTime;
        MarkAt(bearing);
    }

    private void MarkAt(float bearing)
    {
        WaterfallProcessor p = Processor;
        if (Game.State == null || p == null) return;
        double time = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
        TrackManager tm = Game.State.Tracks;
        if (tm.Find(tm.SelectedId) != null) tm.Retarget(tm.SelectedId, time, bearing);
        else tm.MarkBearing(time, bearing, p.spec.maxTracks);
    }

    /// <summary>Point-sample a texture with far more bins than screen pixels (a fine tier zoomed out) and the
    /// GPU's nearest-neighbour minification aliases into a moire that reads as a diagonal streak drifting up the
    /// image as it scrolls - worse the finer the array (Mk III's beam is a quarter of Mk I's, so Bins can run to
    /// ~4x more before UsefulBins/maxBins caps it). Point stays crisp (and un-aliased) whenever the visible
    /// slice actually fits in the display's pixels; only switch to the mip-mapped Trilinear filter when it's
    /// genuinely being minified.</summary>
    private void RefreshFilterMode(WaterfallProcessor p)
    {
        float visibleBins = p.Bins / _view.Zoom;
        FilterMode desired = visibleBins > DisplayW ? FilterMode.Trilinear : FilterMode.Point;
        if (p.Texture.filterMode != desired) p.Texture.filterMode = desired;
    }

    private void Tilt(float deltaDeg)
    {
        WaterfallProcessor p = Processor;
        if (p != null) p.AimElevationDeg += deltaDeg;
    }

    private void BuildControls(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform row = UIKit.Node("Row", parent);
        UIKit.HStack(row, 6f, 0, expandWidth: true);

        _powerButton = UIKit.AddButton(row, "", OnTogglePower, 130f, 32f);
        _powerLabel = _powerButton.GetComponentInChildren<TextMeshProUGUI>();

        _integrationButton = UIKit.AddButton(row, "", OnToggleIntegration, 110f, 32f);
        _integrationLabel = _integrationButton.GetComponentInChildren<TextMeshProUGUI>();


        RectTransform tiltGroup = UIKit.Node("Tilt", row);
        UIKit.HStack(tiltGroup, 2f, 0);
        UIKit.AddLabel(tiltGroup, Loc.Get("ui.aim.tilt"), t.fontSizeSmall, t.textDim);
        UIKit.AddButton(tiltGroup, "-", () => Tilt(-TiltStepDeg), 28f, 28f);
        _tiltLabel = UIKit.AddLabel(tiltGroup, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_tiltLabel.rectTransform, preferredWidth: 120f);
        UIKit.AddButton(tiltGroup, "+", () => Tilt(TiltStepDeg), 28f, 28f);

        RectTransform curGroup = UIKit.Node("Cursor", row);
        UIKit.HStack(curGroup, 2f, 0);
        _cursorLabel = UIKit.AddLabel(curGroup, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_cursorLabel.rectTransform, preferredWidth: 96f);
        Button mark = UIKit.AddButton(curGroup, Loc.Get("ui.aim.mark"), () => { _cursorShownAt = Time.unscaledTime; MarkAt(_cursorBearing); }, 70f, 28f);
        _markLabel = mark.GetComponentInChildren<TextMeshProUGUI>();

        RectTransform row2 = UIKit.Node("Row2", parent);
        var h2 = UIKit.HStack(row2, 12f, 0);
        h2.childAlignment = TextAnchor.MiddleLeft;
        RectTransform pixelGroup = UIKit.Node("Pixel", row2);
        UIKit.HStack(pixelGroup, 2f, 0);
        UIKit.AddLabel(pixelGroup, "PX", t.fontSizeSmall, t.textDim);
        UIKit.AddButton(pixelGroup, "-", () => AdjustPixelSize(-1), 28f, 28f);
        _pixelLabel = UIKit.AddLabel(pixelGroup, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_pixelLabel.rectTransform, preferredWidth: 28f);
        UIKit.AddButton(pixelGroup, "+", () => AdjustPixelSize(1), 28f, 28f);

        RectTransform zoomGroup = UIKit.Node("Zoom", row2);
        UIKit.HStack(zoomGroup, 2f, 0);
        UIKit.AddLabel(zoomGroup, Loc.Get("ui.wf.zoom"), t.fontSizeSmall, t.textDim);
        UIKit.AddButton(zoomGroup, "-", () => Zoom(-1), 28f, 28f);
        _zoomLabel = UIKit.AddLabel(zoomGroup, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_zoomLabel.rectTransform, preferredWidth: 130f);
        UIKit.AddButton(zoomGroup, "+", () => Zoom(1), 28f, 28f);
        UIKit.AddButton(row2, Loc.Get("ui.wf.center"), () => { _cursorShownAt = Time.unscaledTime; _view.CenterOn(_cursorBearing); }, 60f, 28f);
        UIKit.AddLabel(row2, Loc.Get("ui.wf.zoomhint"), t.fontSizeSmall, t.textDim);
    }

    private void OnTogglePower()
    {
        if (Game.State == null) return;
        WaterfallProcessor p = Processor;
        bool on = p == null || !p.Enabled;
        Game.State.SetWaterfallEnabled(on);
    }

    private void OnToggleIntegration()
    {
        _integrationOn = !_integrationOn;
        WaterfallProcessor p = Processor;
        if (p != null) p.IntegrationOn = _integrationOn;
    }

    private void AdjustPixelSize(int delta)
    {
        _pixelSize = Mathf.Clamp(_pixelSize + delta, 1, 8);
    }

    /// <summary>Called by SensorConsole when another mode is selected. No-op: the sensor keeps listening (and
    /// drawing power) whether or not this screen is the one showing — only the power button stops it.</summary>
    public void Hide() { }

    /// <summary>Called every frame by SensorConsole, regardless of whether this mode is the one showing.</summary>
    public void Refresh(float unscaledDeltaSeconds)
    {
        WaterfallProcessor p = Processor;
        UIKit.SetText(_pixelLabel, _pixelSize.ToString());

        if (p == null)
        {
            UIKit.SetText(_powerLabel, Loc.Get("ui.screen.power.off"));
            return;
        }

        if (_image.texture != p.Texture) _image.texture = p.Texture;
        RefreshFilterMode(p);
        Rect uv = p.UvRect;
        float ux, uw;
        _view.UvX(out ux, out uw);
        uv.x = ux; uv.width = uw;
        _image.uvRect = uv;

        if (p.LatestLine != null) DrawDsp(p.LatestLine);

        UIKit.SetText(_powerLabel, Loc.Get(p.Enabled ? "ui.screen.power.on" : "ui.screen.power.off"));
        UIKit.SetButtonActive(_powerButton, p.Enabled);
        UIKit.SetText(_integrationLabel, _integrationOn ? "INTEG ON" : "INTEG OFF");
        UIKit.SetButtonActive(_integrationButton, _integrationOn);

        // Keyboard aim only while this tab is the one showing.
        if (_root != null && _root.activeInHierarchy)
        {
            Vector2 slew = AimKeys.Slew();
            if (slew.y != 0f) Tilt(slew.y * TiltKeyDegPerSec * unscaledDeltaSeconds);
            if (slew.x != 0f)
            {
                // Slower when zoomed, so the cursor moves at about the same speed on screen.
                _cursorBearing = BearingMath.Wrap360(_cursorBearing + slew.x * CursorKeyDegPerSec / _view.Zoom * unscaledDeltaSeconds);
                _view.KeepVisible(_cursorBearing);
                _cursorShownAt = Time.unscaledTime;
            }
            if (AimKeys.ConfirmPressed()) { _cursorShownAt = Time.unscaledTime; MarkAt(_cursorBearing); }
        }
        UIKit.SetText(_tiltLabel, Loc.Get("ui.aim.tilt.value", p.AimElevationDeg, p.spec.fanHalfWidthElDeg));
        UIKit.SetText(_cursorLabel, Loc.Get("ui.aim.cursor", _cursorBearing));
        bool moving = Game.State != null && Game.State.Tracks.Find(Game.State.Tracks.SelectedId) != null;
        UIKit.SetText(_markLabel, Loc.Get(moving ? "ui.aim.move" : "ui.aim.mark"));
        PlaceCursor();

        if (Game.State != null)
        {
            float headingDeg = (float)Game.State.Ship.headingDeg;
            _overlay.Refresh(Game.State.Tracks, headingDeg, p, _view);
            RefreshDspTicks(Game.State.Tracks, headingDeg);
        }

        RefreshScale();
        UIKit.SetText(_zoomLabel, Loc.Get("ui.wf.zoom.value", _view.Zoom, _view.Span));
    }

    // Thin ticks under the DSP trace at every track's CURRENT bearing (the DSP strip only ever shows the
    // newest line, so unlike the waterfall image's pixel chain there's no history to place here — just "does
    // this track's estimate sit on the peak that's visible right now"). A searching (not yet locked) track
    // shows red, same as its tick on the waterfall image and its row in the Track panel — one consistent
    // "not locked yet" signal everywhere. World-bearing-centered, same as the waterfall image: no heading
    // offset (headingDeg is unused here now, kept only so callers don't need to change).
    private void RefreshDspTicks(TrackManager tracks, float headingDeg)
    {
        UITheme t = UITheme.Current;
        IList<Track> all = tracks.All;
        int used = 0;

        for (int i = 0; i < all.Count; i++)
        {
            Track tr = all[i];

            float x = _view.Frac(tr.bearing);
            if (!_view.Visible(x)) continue;
            Image tick = GetDspTick(used);
            tick.gameObject.SetActive(true);

            bool selected = tr.id == tracks.SelectedId;
            bool searching = tr.status != TrackStatus.Confirmed;
            Color color = t.WaterfallTrackColor(selected, searching);
            color.a = selected ? 0.95f : 0.85f;
            tick.color = color;

            RectTransform rt = tick.rectTransform;
            rt.anchorMin = new Vector2(x, 0f);
            rt.anchorMax = new Vector2(x, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(DspTickWidth, 0f);
            rt.anchoredPosition = Vector2.zero;

            used++;
        }

        for (int i = used; i < _dspTicks.Count; i++)
            if (_dspTicks[i].gameObject.activeSelf) _dspTicks[i].gameObject.SetActive(false);
    }

    private Image GetDspTick(int index)
    {
        while (_dspTicks.Count <= index)
        {
            Image tick = UIKit.AddPanel(_dspArea, "Tick", Color.white);
            tick.raycastTarget = false;
            _dspTicks.Add(tick);
        }
        return _dspTicks[index];
    }

    // Resamples the visible part of the raw DSP line at evenly spaced screen positions (linear interpolation
    // between bins, wrapping at +/-180), so it lines up with the zoomed image and the ticks. _pixelSize thins the
    // points (cosmetic, matching the old size slider). Normalized 0..1 (0.5 = zero) for LineGraphic, over the
    // visible samples only.
    private void DrawDsp(float[] line)
    {
        int n = line.Length;
        if (n < 2) return;
        float visibleBins = n / _view.Zoom;
        int step = Mathf.Max(1, _pixelSize);
        int count = Mathf.Clamp(Mathf.CeilToInt(visibleBins * 2f) + 1, 2, DisplayW) / step;
        count = Mathf.Max(2, count);
        if (_dspNormalized.Length < count) _dspNormalized = new float[count];

        float maxAbs = 1e-6f;
        for (int k = 0; k < count; k++)
        {
            float b = _view.Bearing(k / (float)(count - 1));
            float pos = (BearingMath.Wrap180(b) + 180f) / 360f * n - 0.5f; // bin i is centred at (i + 0.5) / n
            int i0 = Mathf.FloorToInt(pos);
            float f = pos - i0;
            float v = Mathf.Lerp(line[((i0 % n) + n) % n], line[(((i0 + 1) % n) + n) % n], f);
            _dspNormalized[k] = v;
            maxAbs = Mathf.Max(maxAbs, Mathf.Abs(v));
        }

        for (int k = 0; k < count; k++)
            _dspNormalized[k] = 0.5f + 0.5f * Mathf.Clamp(_dspNormalized[k] / maxAbs, -1f, 1f);

        _dspLine.SetValues(_dspNormalized, count);
    }
}
