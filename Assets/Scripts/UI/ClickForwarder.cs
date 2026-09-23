using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Tiny reusable click hook for a raycastable UI element (its Graphic must have raycastTarget = true).
/// Reports the click's LOCAL point within its own RectTransform (0,0 = the rect's pivot) plus that
/// RectTransform itself, so a caller can turn it into a fraction/bearing/whatever without repeating the
/// ScreenPointToLocalPointInRectangle boilerplate each time. Used by WaterfallScreen to let the player mark a
/// bearing by clicking the waterfall image or the DSP strip.
/// </summary>
public sealed class ClickForwarder : MonoBehaviour, IPointerClickHandler
{
    public System.Action<Vector2, RectTransform> OnClick;

    public void OnPointerClick(PointerEventData e)
    {
        if (OnClick == null) return;
        RectTransform rt = (RectTransform)transform;
        Vector2 local;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out local))
            OnClick(local, rt);
    }
}
