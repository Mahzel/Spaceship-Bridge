using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws a polyline as ONE UI mesh (a single draw call), replacing one-GameObject-per-segment.
/// Values are normalized 0..1 and spread evenly across the rect width; 0 = bottom, 1 = top.
/// Colour comes from Graphic.color.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class LineGraphic : MaskableGraphic
{
    [Min(0.5f)] public float thickness = 1.5f;

    private float[] _values = new float[0];
    private int _count;

    public void SetValues(float[] normalized, int count)
    {
        count = Mathf.Min(count, normalized.Length);
        if (_values.Length < count) _values = new float[count];
        System.Array.Copy(normalized, _values, count);
        _count = count;
        SetVerticesDirty();
    }

    public void Clear()
    {
        _count = 0;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_count < 2) return;

        Rect r    = GetPixelAdjustedRect();
        float half = thickness * 0.5f;
        var quad   = new UIVertex[4];

        Vector2 prev = PointAt(0, r);
        for (int i = 1; i < _count; i++)
        {
            Vector2 cur = PointAt(i, r);
            Vector2 dir = cur - prev;
            if (dir.sqrMagnitude > 1e-6f)
            {
                dir.Normalize();
                Vector2 n = new Vector2(-dir.y, dir.x) * half;

                quad[0] = Vertex(prev - n);
                quad[1] = Vertex(prev + n);
                quad[2] = Vertex(cur + n);
                quad[3] = Vertex(cur - n);
                vh.AddUIVertexQuad(quad);
            }
            prev = cur;
        }
    }

    private Vector2 PointAt(int i, Rect r)
    {
        float x = r.xMin + (float)i / (_count - 1) * r.width;
        float y = r.yMin + _values[i] * r.height;
        return new Vector2(x, y);
    }

    private UIVertex Vertex(Vector2 pos)
    {
        UIVertex v = UIVertex.simpleVert;
        v.color    = color;
        v.position = pos;
        return v;
    }
}
