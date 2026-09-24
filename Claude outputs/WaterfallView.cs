using UnityEngine;

/// <summary>
/// What slice of the 360-degree waterfall is on screen: a centre bearing and a span. Purely visual: the
/// processor always works on the full circle (detection, tracking and power are unchanged by zoom). Every
/// piece of the waterfall screen (image uvRect, track overlay, DSP strip, cursor, azimuth scale, clicks) maps
/// bearings through this, so they can't disagree.
/// Frac: 0 = left edge of the view, 1 = right edge. At zoom x1 the view is centred on bearing 0 and
/// Frac(b) == (Wrap180(b) + 180) / 360, exactly the old full-circle layout.
/// </summary>
public sealed class WaterfallView
{
    public static readonly float[] Zooms = { 1f, 2f, 4f, 8f, 16f, 32f };

    private int _zoomIndex;
    private float _center; // world bearing, [0, 360)

    public int ZoomIndex { get { return _zoomIndex; } }
    public float Zoom { get { return Zooms[_zoomIndex]; } }
    public float Span { get { return 360f / Zoom; } }
    public float Center { get { return _zoomIndex == 0 ? 0f : _center; } }
    public bool Zoomed { get { return _zoomIndex > 0; } }

    /// <summary>Screen fraction of a world bearing. May fall outside [0, 1] (off-screen) when zoomed.</summary>
    public float Frac(float bearingDeg)
    {
        return BearingMath.Diff(bearingDeg, Center) / Span + 0.5f;
    }

    public bool Visible(float frac) { return frac >= 0f && frac <= 1f; }

    /// <summary>World bearing at a screen fraction.</summary>
    public float Bearing(float frac)
    {
        return BearingMath.Wrap360(Center + (frac - 0.5f) * Span);
    }

    /// <summary>Zooms by `steps` levels, keeping the bearing under screen fraction `anchorFrac` where it is.</summary>
    public void ZoomBy(int steps, float anchorFrac)
    {
        float anchor = Bearing(anchorFrac);
        int ni = Mathf.Clamp(_zoomIndex + steps, 0, Zooms.Length - 1);
        if (ni == _zoomIndex) return;
        _zoomIndex = ni;
        if (_zoomIndex == 0) { _center = 0f; return; }
        // Put `anchor` back at anchorFrac under the new span.
        _center = BearingMath.Wrap360(anchor - (anchorFrac - 0.5f) * Span);
    }

    public void Pan(float deltaDeg)
    {
        if (_zoomIndex == 0) return;
        _center = BearingMath.Wrap360(_center + deltaDeg);
    }

    /// <summary>Scrolls the view just enough to bring a bearing on screen (with a small margin).</summary>
    public void KeepVisible(float bearingDeg, float marginFrac = 0.05f)
    {
        if (_zoomIndex == 0) return;
        float f = Frac(bearingDeg);
        if (f < marginFrac) Pan((f - marginFrac) * Span);
        else if (f > 1f - marginFrac) Pan((f - (1f - marginFrac)) * Span);
    }

    public void CenterOn(float bearingDeg) { if (_zoomIndex > 0) _center = BearingMath.Wrap360(bearingDeg); }

    /// <summary>RawImage.uvRect x/width for the waterfall texture (whose U runs -180..+180 over 0..1; the
    /// texture's U wrap must be Repeat for views that straddle +/-180).</summary>
    public void UvX(out float x, out float width)
    {
        width = Span / 360f;
        x = (BearingMath.Wrap180(Center) + 180f) / 360f - 0.5f * width;
    }
}
