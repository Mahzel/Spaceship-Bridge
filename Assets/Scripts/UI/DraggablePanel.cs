using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Lets the player drag a panel by any empty part of it (clicks on its buttons still work). The panel is kept
/// inside the canvas, and its position is remembered between sessions.
/// The panel's own Image must have raycastTarget on, and its labels must not (UIKit labels already don't).
/// </summary>
public class DraggablePanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform _rt;
    private RectTransform _parent;
    private Canvas _canvas;
    private string _key;
    private bool _needsClamp = true;

    public static void Attach(RectTransform panel, string key)
    {
        var d = panel.gameObject.AddComponent<DraggablePanel>();
        d._rt = panel;
        d._key = key;
        d.Load();
    }

    private void Awake()
    {
        if (_rt == null) _rt = (RectTransform)transform;
    }

    private void Load()
    {
        try
        {
            if (PlayerPrefs.HasKey(Prefix + ".x"))
                _rt.anchoredPosition = new Vector2(PlayerPrefs.GetFloat(Prefix + ".x"), PlayerPrefs.GetFloat(Prefix + ".y"));
        }
        catch (System.Exception) { }
    }

    private string Prefix { get { return "ui.pos." + _key; } }

    private void LateUpdate()
    {
        if (!_needsClamp) return;
        _needsClamp = false;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_rt);
        Clamp();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        _parent = (RectTransform)_rt.parent;
        _canvas = GetComponentInParent<Canvas>().rootCanvas;
        transform.SetAsLastSibling(); // most recently touched panel on top
    }

    public void OnDrag(PointerEventData e)
    {
        if (_canvas == null) return;
        _rt.anchoredPosition += e.delta / _canvas.scaleFactor;
        Clamp();
    }

    public void OnEndDrag(PointerEventData e)
    {
        try
        {
            PlayerPrefs.SetFloat(Prefix + ".x", _rt.anchoredPosition.x);
            PlayerPrefs.SetFloat(Prefix + ".y", _rt.anchoredPosition.y);
        }
        catch (System.Exception) { }
    }

    // Keeps the whole panel inside its parent rect.
    private void Clamp()
    {
        var parent = (RectTransform)_rt.parent;
        if (parent == null) return;

        var corners = new Vector3[4];
        _rt.GetWorldCorners(corners);

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector3 l = parent.InverseTransformPoint(corners[i]);
            if (l.x < minX) minX = l.x;
            if (l.x > maxX) maxX = l.x;
            if (l.y < minY) minY = l.y;
            if (l.y > maxY) maxY = l.y;
        }

        Rect pr = parent.rect;
        float dx = 0f, dy = 0f;
        if (minX < pr.xMin) dx = pr.xMin - minX; else if (maxX > pr.xMax) dx = pr.xMax - maxX;
        if (minY < pr.yMin) dy = pr.yMin - minY; else if (maxY > pr.yMax) dy = pr.yMax - maxY;
        if (dx != 0f || dy != 0f) _rt.anchoredPosition += new Vector2(dx, dy);
    }
}
