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
/// </summary>
public sealed class WaterfallScreen
{
    private const int DisplayW = 860;
    private const int DisplayH = 260;
    private const int DspH = 70;
    private const float DspTickWidth = 1.5f;

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
        UIKit.Size(root, flexibleWidth: 1f);
        var v = UIKit.VStack(root, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        BuildDisplay(root);
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
        _image.raycastTarget = true; // needed to receive the mark-a-bearing click below
        var imgClick = _image.gameObject.AddComponent<ClickForwarder>();
        imgClick.OnClick = OnDisplayClicked;

        _overlay = new TrackOverlay();
        _overlay.Build(area);
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
        float bearing = BearingMath.Wrap360(xFrac * 360f - 180f);
        double time = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
        Game.State.Tracks.MarkBearing(time, bearing, p.spec.maxTracks);
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

        RectTransform pixelGroup = UIKit.Node("Pixel", row);
        UIKit.HStack(pixelGroup, 2f, 0);
        UIKit.AddLabel(pixelGroup, "PX", t.fontSizeSmall, t.textDim);
        UIKit.AddButton(pixelGroup, "-", () => AdjustPixelSize(-1), 28f, 28f);
        _pixelLabel = UIKit.AddLabel(pixelGroup, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_pixelLabel.rectTransform, preferredWidth: 28f);
        UIKit.AddButton(pixelGroup, "+", () => AdjustPixelSize(1), 28f, 28f);
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
        _image.uvRect = p.UvRect;

        if (p.LatestLine != null) DrawDsp(p.LatestLine);

        UIKit.SetText(_powerLabel, Loc.Get(p.Enabled ? "ui.screen.power.on" : "ui.screen.power.off"));
        UIKit.SetButtonActive(_powerButton, p.Enabled);
        UIKit.SetText(_integrationLabel, _integrationOn ? "INTEG ON" : "INTEG OFF");
        UIKit.SetButtonActive(_integrationButton, _integrationOn);

        if (Game.State != null)
        {
            float headingDeg = (float)Game.State.Ship.headingDeg;
            _overlay.Refresh(Game.State.Tracks, headingDeg, p);
            RefreshDspTicks(Game.State.Tracks, headingDeg);
        }
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

            Image tick = GetDspTick(used);
            tick.gameObject.SetActive(true);

            bool selected = tr.id == tracks.SelectedId;
            bool searching = tr.status != TrackStatus.Confirmed;
            Color color = selected ? t.accent : (searching ? t.danger : t.good);
            color.a = selected ? 0.95f : (searching ? 0.85f : 0.6f);
            tick.color = color;

            float x = (BearingMath.Wrap180(tr.bearing) + 180f) / 360f;
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

    // Downsamples the raw DSP line by _pixelSize (cosmetic point density, matching the old size slider) and
    // normalizes it 0..1 (0.5 = zero) for LineGraphic, which expects values in that range.
    private void DrawDsp(float[] line)
    {
        int step = Mathf.Max(1, _pixelSize);
        int count = Mathf.Max(1, line.Length / step);
        if (_dspNormalized.Length < count) _dspNormalized = new float[count];

        float maxAbs = 1e-6f;
        for (int i = 0; i < count; i++) maxAbs = Mathf.Max(maxAbs, Mathf.Abs(line[i * step]));

        for (int i = 0; i < count; i++)
            _dspNormalized[i] = 0.5f + 0.5f * Mathf.Clamp(line[i * step] / maxAbs, -1f, 1f);

        _dspLine.SetValues(_dspNormalized, count);
    }
}
