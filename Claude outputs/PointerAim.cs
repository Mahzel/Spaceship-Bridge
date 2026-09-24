using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Click-or-drag hook for an aimable sensor display (its Graphic must have raycastTarget = true). Like
/// ClickForwarder, it reports points in the element's LOCAL space (0,0 = the rect's pivot), but it tells a
/// click apart from a drag: once the pointer has moved more than DragThresholdPx since it went down, the gesture
/// is a drag, and the click that Unity would otherwise still send on release is swallowed. So one display can
/// both "click to act" (mark, recenter, pick a return) and "drag to slew".
/// </summary>
public sealed class PointerAim : MonoBehaviour, IPointerDownHandler, IPointerClickHandler,
                                IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    /// <summary>Mouse wheel over the element: (wheel delta, +up; local point, rect).</summary>
    public System.Action<float, Vector2, RectTransform> OnScrolled;

    public void OnScroll(PointerEventData e)
    {
        if (OnScrolled == null || e.scrollDelta.y == 0f) return;
        Vector2 local;
        if (ToLocal(e, out local)) OnScrolled(e.scrollDelta.y, local, Rt);
    }

    public const float DragThresholdPx = 4f;

    /// <summary>A click that was not part of a drag: (local point, rect).</summary>
    public System.Action<Vector2, RectTransform> OnClick;

    /// <summary>Every drag step: (local delta since the last step, current local point, rect).</summary>
    public System.Action<Vector2, Vector2, RectTransform> OnDragged;

    private bool _dragging;
    private Vector2 _lastLocal;

    private RectTransform Rt { get { return (RectTransform)transform; } }

    private bool ToLocal(PointerEventData e, out Vector2 local)
    {
        Camera cam = e.pressEventCamera != null ? e.pressEventCamera : e.enterEventCamera;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(Rt, e.position, cam, out local);
    }

    public void OnPointerDown(PointerEventData e)
    {
        _dragging = false;
        ToLocal(e, out _lastLocal);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        // EventSystem's own drag threshold already applies; ours only guards against very low settings.
        if ((e.position - e.pressPosition).sqrMagnitude < DragThresholdPx * DragThresholdPx) return;
        _dragging = true;
        ToLocal(e, out _lastLocal);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!_dragging)
        {
            if ((e.position - e.pressPosition).sqrMagnitude < DragThresholdPx * DragThresholdPx) return;
            _dragging = true;
        }
        Vector2 local;
        if (!ToLocal(e, out local)) return;
        Vector2 delta = local - _lastLocal;
        _lastLocal = local;
        if (OnDragged != null && delta.sqrMagnitude > 0f) OnDragged(delta, local, Rt);
    }

    public void OnEndDrag(PointerEventData e) { /* _dragging stays true until the click it would cause is swallowed */ }

    public void OnPointerClick(PointerEventData e)
    {
        if (_dragging) { _dragging = false; return; }
        Vector2 local;
        if (OnClick != null && ToLocal(e, out local)) OnClick(local, Rt);
    }
}
