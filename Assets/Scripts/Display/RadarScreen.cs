using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders the radar sensor. Fully code-built (UIKit), same plain-class pattern as the other console screens.
/// All the actual work (firing, the propagation-delay countdown, resolving) lives in RadarProcessor
/// (Game.State.Radar), ticked every real frame from RunDriver regardless of which console mode is showing —
/// same reasoning as WaterfallScreen: a ping in flight shouldn't stall just because the player looked away.
/// Hide() is a no-op for the same reason.
///
/// Two modes, chosen here in the UI (RadarProcessor itself is stateless about which mode is "current" outside
/// of whatever's actually pending/resolved): SWEEP is free-aim (click the strip to pick a bearing) with an
/// adjustable, wide beam and never reveals identity — just "something's out there, this far" or nothing.
/// TRACK aims automatically at the Track panel's currently selected track, with a fixed hair-thin beam, and a
/// hit both reports a precise range + a radial-velocity reading AND writes that range straight into the
/// track's own RangeEstimate (see RadarProcessor.Tick) — a real alternative to bearing-only TMA.
/// </summary>
public sealed class RadarScreen
{
    private const int DisplayW = 860;
    private const float AimAreaHeight = 46f;
    private const float FallbackBeamMin = 5f, FallbackBeamMax = 90f;

    private RadarPingMode _uiMode = RadarPingMode.Sweep;
    private float _aimBearingDeg;
    private float _beamHalfWidthDeg = 30f;

    private RectTransform _aimArea;
    private Image _aimBandA, _aimBandB, _aimTick;

    private Button _sweepModeButton, _trackModeButton, _fireButton, _widthMinusButton, _widthPlusButton;
    private TextMeshProUGUI _widthLabel, _targetLabel, _statusLabel;
    private RectTransform _sweepRow, _widthRow, _targetRow;

    private RadarProcessor Processor { get { return Game.State != null ? Game.State.Radar : null; } }

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform root = UIKit.Node("Radar", parent);
        UIKit.Size(root, flexibleWidth: 1f);
        var v = UIKit.VStack(root, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        BuildModeRow(root);
        BuildAimArea(root);
        BuildWidthRow(root);
        BuildTargetRow(root);
        BuildFireRow(root);

        _statusLabel = UIKit.AddLabel(root, "", t.fontSizeSmall, t.textDim);

        return root.gameObject;
    }

    private void BuildModeRow(Transform parent)
    {
        RectTransform row = UIKit.Node("Mode", parent);
        UIKit.HStack(row, 6f, 0, expandWidth: true);
        _sweepModeButton = UIKit.AddButton(row, Loc.Get("ui.radar.mode.sweep"), () => SetMode(RadarPingMode.Sweep), 0f, 34f);
        _trackModeButton = UIKit.AddButton(row, Loc.Get("ui.radar.mode.track"), () => SetMode(RadarPingMode.Track), 0f, 34f);
    }

    private void BuildAimArea(Transform parent)
    {
        UITheme t = UITheme.Current;
        Image areaImage = UIKit.AddPanel(parent, "AimArea", t.barBack);
        _aimArea = areaImage.rectTransform;
        UIKit.Size(_aimArea, preferredWidth: DisplayW, minHeight: AimAreaHeight);
        areaImage.raycastTarget = true;
        var click = _aimArea.gameObject.AddComponent<ClickForwarder>();
        click.OnClick = OnAimClicked;

        Color bandColor = t.accent; bandColor.a = 0.20f;
        _aimBandA = UIKit.AddPanel(_aimArea, "BandA", bandColor);
        _aimBandB = UIKit.AddPanel(_aimArea, "BandB", bandColor);
        _aimBandB.gameObject.SetActive(false);

        _aimTick = UIKit.AddPanel(_aimArea, "Tick", t.accent);
        RectTransform tr = _aimTick.rectTransform;
        tr.pivot = new Vector2(0.5f, 0.5f);
        tr.sizeDelta = new Vector2(3f, 0f);
    }

    private void BuildWidthRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform row = UIKit.Node("Width", parent);
        _widthRow = row;
        var h = UIKit.HStack(row, 4f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;
        UIKit.AddLabel(row, Loc.Get("ui.radar.beam"), t.fontSizeSmall, t.textDim);
        _widthMinusButton = UIKit.AddButton(row, "-", () => AdjustBeamWidth(-5f), 28f, 28f);
        _widthLabel = UIKit.AddLabel(row, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_widthLabel.rectTransform, preferredWidth: 50f);
        _widthPlusButton = UIKit.AddButton(row, "+", () => AdjustBeamWidth(5f), 28f, 28f);
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
        UIKit.HStack(row, 6f, 0, expandWidth: true);
        _fireButton = UIKit.AddButton(row, Loc.Get("ui.radar.fire"), OnFireClicked, 140f, 36f);
    }

    private void SetMode(RadarPingMode mode) { _uiMode = mode; }

    // World-bearing-centered, same convention as the waterfall image and every track bearing in this UI.
    private void OnAimClicked(Vector2 localPoint, RectTransform rt)
    {
        float xFrac = Mathf.Clamp01(localPoint.x / rt.rect.width + 0.5f);
        _aimBearingDeg = BearingMath.Wrap360(xFrac * 360f - 180f);
    }

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
        if (p == null) return;

        string whyNot;
        if (_uiMode == RadarPingMode.Sweep)
        {
            p.FireSweep(_aimBearingDeg, _beamHalfWidthDeg, out whyNot);
        }
        else
        {
            int trackId = Game.State != null ? Game.State.Tracks.SelectedId : 0;
            p.FireTrack(trackId, out whyNot);
        }
    }

    /// <summary>No-op: like the waterfall, a pending ping keeps counting down whether or not this tab is
    /// showing — only RadarProcessor.Tick (driven by RunDriver) governs that.</summary>
    public void Hide() { }

    public void Refresh(float unscaledDeltaSeconds)
    {
        RadarProcessor p = Processor;

        UIKit.SetButtonActive(_sweepModeButton, _uiMode == RadarPingMode.Sweep);
        UIKit.SetButtonActive(_trackModeButton, _uiMode == RadarPingMode.Track);

        bool sweep = _uiMode == RadarPingMode.Sweep;
        if (_aimArea.gameObject.activeSelf != sweep) _aimArea.gameObject.SetActive(sweep);
        if (_widthRow.gameObject.activeSelf != sweep) _widthRow.gameObject.SetActive(sweep);
        if (_targetRow.gameObject.activeSelf != !sweep) _targetRow.gameObject.SetActive(!sweep);

        if (sweep)
        {
            UIKit.SetText(_widthLabel, Loc.Get("ui.radar.beam.value", _beamHalfWidthDeg));
            RefreshAimBand();
        }
        else
        {
            Track tr = Game.State != null ? Game.State.Tracks.Find(Game.State.Tracks.SelectedId) : null;
            UIKit.SetText(_targetLabel, tr != null ? Loc.Get("ui.radar.target", tr.name) : Loc.Get("ui.radar.target.none"));
        }

        bool canFire = p != null && !p.Pinging && (sweep || (Game.State != null && Game.State.Tracks.SelectedId != 0));
        _fireButton.interactable = canFire;

        RefreshStatus(p);
    }

    private void RefreshAimBand()
    {
        float x0 = (BearingMath.Wrap180(_aimBearingDeg - _beamHalfWidthDeg) + 180f) / 360f;
        float x1 = (BearingMath.Wrap180(_aimBearingDeg + _beamHalfWidthDeg) + 180f) / 360f;
        // Wrap-safe band, same two-segment approach as TrackOverlay's wake-sector band.
        if (x0 <= x1) { SetBand(_aimBandA, x0, x1); _aimBandB.gameObject.SetActive(false); }
        else          { SetBand(_aimBandA, 0f, x1); SetBand(_aimBandB, x0, 1f); }

        float tickX = (BearingMath.Wrap180(_aimBearingDeg) + 180f) / 360f;
        RectTransform tr = _aimTick.rectTransform;
        tr.anchorMin = new Vector2(tickX, 0f);
        tr.anchorMax = new Vector2(tickX, 1f);
        tr.anchoredPosition = Vector2.zero;
    }

    private static void SetBand(Image band, float x0, float x1)
    {
        band.gameObject.SetActive(true);
        RectTransform r = band.rectTransform;
        r.anchorMin = new Vector2(Mathf.Clamp01(x0), 0f);
        r.anchorMax = new Vector2(Mathf.Clamp01(x1), 1f);
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    private void RefreshStatus(RadarProcessor p)
    {
        if (p == null) { UIKit.SetText(_statusLabel, ""); return; }

        if (p.Pinging)
        {
            UIKit.SetText(_statusLabel, Loc.Get("ui.radar.pinging", p.EtaRealSeconds));
            return;
        }

        RadarPingResult r = p.LastResult;
        if (r == null) { UIKit.SetText(_statusLabel, ""); return; }

        if (!r.hit) { UIKit.SetText(_statusLabel, Loc.Get("ui.radar.noreturn")); return; }

        if (r.mode == RadarPingMode.Sweep)
            UIKit.SetText(_statusLabel, Loc.Get("ui.radar.result.range", r.rangeAu));
        else
            UIKit.SetText(_statusLabel, Loc.Get("ui.radar.result.rangerate", r.rangeAu, r.radialVelocityKmS));
    }
}
