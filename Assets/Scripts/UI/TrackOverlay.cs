using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Per track: a short "now" tick + name tag at the top of the waterfall image (current bearing), plus a pixel
/// chain tracing the track's own bearing HISTORY across the image. The waterfall image is world-bearing-
/// centered (WaterfallProcessor lays contacts out in the fixed world frame, not ship-relative), so both the
/// "now" tick and every history dot just use the sample's own world bearing directly — no heading correction
/// needed, and a contact sits still in the image even as the ship turns. A separate, distinctly-colored
/// heading tick (drawn once, always on) sweeps across instead, marking where the ship is currently pointed.
/// Color tiers: selected = accent, still searching (not locked) = danger — the same red as its row in the
/// Track panel and its DSP tick, so "not locked yet" reads the same everywhere — locked = good.
/// </summary>
public sealed class TrackOverlay
{
    private const float TickHeight = 14f;   // px: the "now" tick, replacing the old full-height bar
    private const float TickWidth = 2f;
    private const float DotSize = 3f;
    private const float HeadingTickWidth = 3f;

    private sealed class TrackVisual
    {
        public RectTransform tick;
        public Image tickImage;
        public TextMeshProUGUI label;
        public readonly List<RectTransform> dots = new List<RectTransform>();
        public readonly List<Image> dotImages = new List<Image>();
        public int dotsUsed;
    }

    private RectTransform _root;
    private Image _bandA, _bandB;
    private readonly List<TrackVisual> _visuals = new List<TrackVisual>();

    private RectTransform _headingTick;
    private Image _headingTickImage;
    private TextMeshProUGUI _headingLabel;

    public void Build(RectTransform parent)
    {
        _root = UIKit.Node("TrackOverlay", parent);
        UIKit.Stretch(_root);

        Color c = UITheme.Current.accent;
        c.a = 0.12f;
        _bandA = UIKit.AddPanel(_root, "SectorA", c);
        _bandB = UIKit.AddPanel(_root, "SectorB", c);
        _bandA.gameObject.SetActive(false);
        _bandB.gameObject.SetActive(false);

        BuildHeadingTick();
    }

    // A single always-on tick marking the ship's current heading, in its own color so it's never mistaken for
    // a track. Sweeps across the image as the ship turns, since the image itself no longer does.
    private void BuildHeadingTick()
    {
        UITheme t = UITheme.Current;
        Image tick = UIKit.AddPanel(_root, "HeadingTick", t.warning);
        _headingTick = tick.rectTransform;
        _headingTickImage = tick;

        _headingLabel = UIKit.AddLabel(_headingTick, Loc.Get("ui.heading.tick"), t.fontSizeSmall, t.warning, TextAlignmentOptions.Center);
        RectTransform lr = _headingLabel.rectTransform;
        lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0.5f, 1f);
        lr.anchoredPosition = new Vector2(0f, -2f);
        lr.sizeDelta = new Vector2(60f, 22f);
    }

    // Draws the wake sector (only when limited) as a translucent band, split in two where it wraps.
    // sectorCenterDeg is already a world bearing (WakeMonitor's own doc comment), and the image is now
    // world-bearing-centered too, so no heading correction is needed here anymore.
    private void RefreshSector()
    {
        WakeMonitor w = Game.Wake;
        bool on = w != null && w.useSector;
        if (!on) { _bandA.gameObject.SetActive(false); _bandB.gameObject.SetActive(false); return; }

        float rel = BearingMath.Wrap180(w.sectorCenterDeg);
        float x0 = (rel - w.sectorHalfDeg + 180f) / 360f;
        float x1 = (rel + w.sectorHalfDeg + 180f) / 360f;

        if (x0 >= 0f && x1 <= 1f) { SetBand(_bandA, x0, x1); _bandB.gameObject.SetActive(false); }
        else if (x0 < 0f)         { SetBand(_bandA, 0f, x1); SetBand(_bandB, 1f + x0, 1f); }
        else                      { SetBand(_bandA, x0, 1f); SetBand(_bandB, 0f, x1 - 1f); }
    }

    private static void SetBand(Image band, float x0, float x1)
    {
        band.gameObject.SetActive(true);
        RectTransform r = band.rectTransform;
        r.anchorMin = new Vector2(Mathf.Clamp01(x0), 0f);
        r.anchorMax = new Vector2(Mathf.Clamp01(x1), 1f);
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    /// <summary>processor may be null (sensor off / not built yet) — history chains are simply skipped then;
    /// the "now" ticks still show using the track's last-known (world) bearing.</summary>
    public void Refresh(TrackManager tracks, float headingDeg, WaterfallProcessor processor)
    {
        if (_root == null) return;
        UITheme t = UITheme.Current;
        RefreshSector();
        RefreshHeadingTick(headingDeg);

        IList<Track> all = tracks.All;
        for (int i = 0; i < all.Count; i++)
        {
            Track tr = all[i];
            TrackVisual v = GetVisual(i);
            v.tick.gameObject.SetActive(true);

            bool selected = tr.id == tracks.SelectedId;
            bool searching = tr.status != TrackStatus.Confirmed;
            Color color = selected ? t.accent : (searching ? t.danger : t.good);
            float alpha = selected ? 0.95f : (searching ? 0.85f : 0.55f);
            Color tickColor = color; tickColor.a = alpha;

            float xNow = (BearingMath.Wrap180(tr.bearing) + 180f) / 360f;
            v.tick.anchorMin = v.tick.anchorMax = v.tick.pivot = new Vector2(xNow, 1f);
            v.tick.anchoredPosition = Vector2.zero;
            v.tick.sizeDelta = new Vector2(TickWidth, TickHeight);
            v.tickImage.color = tickColor;
            v.label.color = color;
            UIKit.SetText(v.label, tr.name);

            RefreshHistory(v, tr, processor, color);
        }
        for (int i = all.Count; i < _visuals.Count; i++)
        {
            _visuals[i].tick.gameObject.SetActive(false);
            HideDots(_visuals[i], 0);
        }
    }

    private void RefreshHeadingTick(float headingDeg)
    {
        float x = (BearingMath.Wrap180(headingDeg) + 180f) / 360f;
        _headingTick.anchorMin = _headingTick.anchorMax = _headingTick.pivot = new Vector2(x, 1f);
        _headingTick.anchoredPosition = Vector2.zero;
        _headingTick.sizeDelta = new Vector2(HeadingTickWidth, TickHeight);
    }

    // Places one dot per history sample that still falls within the waterfall's buffered window, at the
    // world bearing/row it was ACTUALLY detected on — the pixel chain. World-bearing-centered now, so the
    // sample's own bearing is enough; no per-sample heading correction needed (contacts don't move as the
    // ship turns, only the heading tick does).
    private void RefreshHistory(TrackVisual v, Track tr, WaterfallProcessor processor, Color color)
    {
        int used = 0;
        if (processor != null)
        {
            List<BearingSample> history = tr.history;
            Color dotColor = color; dotColor.a = 0.7f;

            for (int i = history.Count - 1; i >= 0; i--)
            {
                BearingSample s = history[i];
                if (!processor.TryGetRowFraction(s.time, out float yFrac)) break; // older samples scrolled off too

                float xFrac = (BearingMath.Wrap180(s.bearing) + 180f) / 360f;
                RectTransform dot = GetDot(v, used);
                dot.gameObject.SetActive(true);
                dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(xFrac, yFrac);
                dot.anchoredPosition = Vector2.zero;
                v.dotImages[used].color = dotColor;
                used++;
            }
        }
        HideDots(v, used);
        v.dotsUsed = used;
    }

    private static void HideDots(TrackVisual v, int fromIndex)
    {
        for (int i = fromIndex; i < v.dots.Count; i++)
            if (v.dots[i].gameObject.activeSelf) v.dots[i].gameObject.SetActive(false);
    }

    private RectTransform GetDot(TrackVisual v, int index)
    {
        while (v.dots.Count <= index)
        {
            Image dot = UIKit.AddPanel(_root, "Dot", Color.white);
            RectTransform rt = dot.rectTransform;
            rt.sizeDelta = new Vector2(DotSize, DotSize);
            v.dots.Add(rt);
            v.dotImages.Add(dot);
        }
        return v.dots[index];
    }

    private TrackVisual GetVisual(int index)
    {
        while (_visuals.Count <= index)
        {
            var v = new TrackVisual();
            Image tick = UIKit.AddPanel(_root, "Tick", Color.white);
            v.tick = tick.rectTransform;
            v.tickImage = tick;

            v.label = UIKit.AddLabel(v.tick, "", UITheme.Current.fontSizeSmall, Color.white, TextAlignmentOptions.Center);
            RectTransform lr = v.label.rectTransform;
            lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0.5f, 1f);
            lr.anchoredPosition = new Vector2(0f, -2f);
            lr.sizeDelta = new Vector2(80f, 22f);

            _visuals.Add(v);
        }
        return _visuals[index];
    }
}
