using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mesh-drawn primitive for the navigation map (see handoff-navigation-ui.md): polylines, dashed polylines,
/// circles/rings, arcs and a couple of filled marker glyphs, built as a single mesh each redraw. Lines stay
/// crisp at any zoom (unlike a Texture2D scope such as RadarScreen) and rebuilding a few hundred vertices is
/// cheap enough to do on every redraw tick.
///
/// Usage: call Clear(), then any number of Add*() calls (coordinates are local to this RectTransform, pixels,
/// origin wherever the caller puts it), then Rebuild() once. Not a general vector-graphics library - just the
/// primitives the nav screens need.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MapCanvas : MaskableGraphic
{
    private struct Poly
    {
        public List<Vector2> pts;
        public float width;     // >0 = stroked polyline; -1 = filled fan; -2 = filled triangle
        public Color32 color;
        public bool loop;
    }

    private const float FilledFan = -1f;
    private const float FilledTri = -2f;

    private readonly List<Poly> _polys = new List<Poly>();

    protected override void Awake()
    {
        base.Awake();
        color = Color.white; // vertex colours carry the real colour; a tinted base color would double up
        raycastTarget = false;
    }

    public void Clear() => _polys.Clear();

    /// <summary>Call once after all Add*() calls for this redraw.</summary>
    public void Rebuild() => SetVerticesDirty();

    // --- strokes -------------------------------------------------------------------------------------------
    public void AddLine(Vector2 a, Vector2 b, float width, Color color)
    {
        var pts = new List<Vector2>(2) { a, b };
        _polys.Add(new Poly { pts = pts, width = Mathf.Max(0.5f, width), color = color, loop = false });
    }

    public void AddPolyline(IList<Vector2> points, float width, Color color, bool loop = false)
    {
        if (points == null || points.Count < 2) return;
        _polys.Add(new Poly { pts = new List<Vector2>(points), width = Mathf.Max(0.5f, width), color = color, loop = loop });
    }

    /// <summary>Same as AddPolyline, but split into on/off segments of dashLen/gapLen along the path.</summary>
    public void AddDashedPolyline(IList<Vector2> points, float width, Color color, float dashLen, float gapLen, bool loop = false)
    {
        if (points == null || points.Count < 2 || dashLen <= 0f) return;
        int n = points.Count;
        int segCount = loop ? n : n - 1;
        float phase = 0f;
        bool on = true;
        for (int i = 0; i < segCount; i++)
        {
            Vector2 a = points[i], b = points[(i + 1) % n];
            float segLen = Vector2.Distance(a, b);
            if (segLen < 1e-6f) continue;
            float pos = 0f;
            while (pos < segLen)
            {
                float budget = (on ? dashLen : gapLen) - phase;
                float step = Mathf.Min(budget, segLen - pos);
                if (on)
                {
                    Vector2 p0 = Vector2.Lerp(a, b, pos / segLen);
                    Vector2 p1 = Vector2.Lerp(a, b, (pos + step) / segLen);
                    AddLine(p0, p1, width, color);
                }
                pos += step;
                phase += step;
                if (phase >= (on ? dashLen : gapLen) - 1e-4f) { phase = 0f; on = !on; }
            }
        }
    }

    public void AddCircle(Vector2 center, float radius, float width, Color color, int segments = 64)
        => AddPolyline(RingPoints(center, radius, segments), width, color, true);

    public void AddArc(Vector2 center, float radius, float fromDeg, float toDeg, float width, Color color, int segments = 32)
    {
        segments = Mathf.Max(2, segments);
        var pts = new List<Vector2>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            float d = Mathf.Lerp(fromDeg, toDeg, i / (float)segments) * Mathf.Deg2Rad;
            pts.Add(center + new Vector2(Mathf.Sin(d), Mathf.Cos(d)) * radius); // bearing convention: 0 = up, clockwise
        }
        AddPolyline(pts, width, color, false);
    }

    // --- marker glyphs ---------------------------------------------------------------------------------------
    public void AddCross(Vector2 center, float size, float width, Color color)
    {
        AddLine(center + new Vector2(-size, 0f), center + new Vector2(size, 0f), width, color);
        AddLine(center + new Vector2(0f, -size), center + new Vector2(0f, size), width, color);
    }

    public void AddDot(Vector2 center, float radius, Color color, int segments = 14)
        => _polys.Add(new Poly { pts = RingPoints(center, radius, segments), width = FilledFan, color = color, loop = true });

    public void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Color color)
        => _polys.Add(new Poly { pts = new List<Vector2> { a, b, c }, width = FilledTri, color = color, loop = false });

    /// <summary>An arrowhead pointing along dir (need not be normalised), tip at 'tip'.</summary>
    public void AddArrow(Vector2 tip, Vector2 dir, float length, float halfWidth, Color color)
    {
        if (dir.sqrMagnitude < 1e-8f) return;
        dir = dir.normalized;
        Vector2 n = new Vector2(-dir.y, dir.x) * halfWidth;
        Vector2 back = tip - dir * length;
        AddTriangle(tip, back + n, back - n, color);
    }

    private static List<Vector2> RingPoints(Vector2 center, float radius, int segments)
    {
        segments = Mathf.Max(3, segments);
        var pts = new List<Vector2>(segments);
        for (int i = 0; i < segments; i++)
        {
            float d = i / (float)segments * Mathf.PI * 2f;
            pts.Add(center + new Vector2(Mathf.Cos(d), Mathf.Sin(d)) * radius);
        }
        return pts;
    }

    // --- mesh build --------------------------------------------------------------------------------------------
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        foreach (Poly p in _polys)
        {
            if (p.width == FilledFan) AppendFilledFan(vh, p.pts, p.color);
            else if (p.width == FilledTri) AppendTriangle(vh, p.pts, p.color);
            else AppendStroke(vh, p.pts, p.width, p.color, p.loop);
        }
    }

    private static void AppendTriangle(VertexHelper vh, List<Vector2> pts, Color32 color)
    {
        if (pts.Count < 3) return;
        int b = vh.currentVertCount;
        for (int i = 0; i < 3; i++) vh.AddVert(pts[i], color, Vector2.zero);
        vh.AddTriangle(b, b + 1, b + 2);
    }

    private static void AppendFilledFan(VertexHelper vh, List<Vector2> pts, Color32 color)
    {
        if (pts.Count < 3) return;
        Vector2 center = Vector2.zero;
        foreach (Vector2 p in pts) center += p;
        center /= pts.Count;

        int b = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i < pts.Count; i++) vh.AddVert(pts[i], color, Vector2.zero);
        for (int i = 0; i < pts.Count; i++)
        {
            int i0 = b + 1 + i, i1 = b + 1 + (i + 1) % pts.Count;
            vh.AddTriangle(b, i0, i1);
        }
    }

    /// <summary>A quad per segment, no mitre joins - fine at the line widths this UI uses (a few px).</summary>
    private static void AppendStroke(VertexHelper vh, List<Vector2> pts, float width, Color32 color, bool loop)
    {
        if (pts.Count < 2) return;
        float hw = Mathf.Max(0.5f, width) * 0.5f;
        int segCount = loop ? pts.Count : pts.Count - 1;
        for (int i = 0; i < segCount; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Count];
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 1e-10f) continue;
            dir.Normalize();
            Vector2 n = new Vector2(-dir.y, dir.x) * hw;

            int vb = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero);
            vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero);
            vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(vb, vb + 1, vb + 2);
            vh.AddTriangle(vb, vb + 2, vb + 3);
        }
    }
}
