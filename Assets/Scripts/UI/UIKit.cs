using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Small builders for the design kit: panels, labels, buttons, layout stacks and bars.
/// Everything reads UITheme.Current, so screens never hard-code colors or sizes.
/// </summary>
public static class UIKit
{
    private static UITheme T => UITheme.Current;

    // --- Basics ------------------------------------------------------------
    public static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    public static void Stretch(RectTransform rt, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    public static void SetText(TMP_Text label, string value)
    {
        if (label.text != value) label.text = value;
    }

    public static LayoutElement Size(RectTransform rt, float preferredWidth = -1f, float minHeight = -1f, float flexibleWidth = -1f)
    {
        var le = rt.gameObject.GetComponent<LayoutElement>();
        if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
        if (preferredWidth >= 0f) le.preferredWidth = preferredWidth;
        if (minHeight >= 0f)      le.minHeight = minHeight;
        if (flexibleWidth >= 0f)  le.flexibleWidth = flexibleWidth;
        return le;
    }

    // --- Widgets -----------------------------------------------------------
    public static Image AddPanel(Transform parent, string name, Color color)
    {
        var rt = Node(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>A plain RawImage node for a code-built texture (sensor displays, etc). Not raycast-blocking.</summary>
    public static RawImage AddRawImage(Transform parent, string name, Color tint)
    {
        var rt = Node(name, parent);
        var img = rt.gameObject.AddComponent<RawImage>();
        img.color = tint;
        img.raycastTarget = false;
        return img;
    }

    public static TextMeshProUGUI AddLabel(Transform parent, string text, int size, Color color,
                                           TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var rt = Node("Label", parent);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (T.font != null) tmp.font = T.font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }

    public static Button AddButton(Transform parent, string text, UnityAction onClick,
                                   float minWidth = 0f, float minHeight = 40f)
    {
        var rt = Node("Button", parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white; // the ColorBlock below does the tinting
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        ColorBlock cb = btn.colors;
        cb.normalColor      = T.buttonNormal;
        cb.highlightedColor = T.buttonHover;
        cb.pressedColor     = T.buttonPressed;
        cb.selectedColor    = T.buttonNormal;
        cb.disabledColor    = new Color(T.buttonNormal.r, T.buttonNormal.g, T.buttonNormal.b, 0.4f);
        cb.colorMultiplier  = 1f;
        cb.fadeDuration     = 0.05f;
        btn.colors = cb;

        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = minHeight;
        if (minWidth > 0f) le.minWidth = minWidth;

        var label = AddLabel(rt, text, T.fontSizeBody, T.text, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);

        if (onClick != null) btn.onClick.AddListener(onClick);
        return btn;
    }

    /// <summary>Single-line text input. Returns the field; onEndEdit fires when it loses focus or Enter is pressed.</summary>
    public static TMP_InputField AddInputField(Transform parent, string placeholder, int characterLimit,
                                               UnityAction<string> onEndEdit, float width = 200f, float height = 34f)
    {
        var root = Node("Input", parent);
        var bg = root.gameObject.AddComponent<Image>();
        bg.color = T.barBack;
        Size(root, preferredWidth: width, minHeight: height);

        var viewport = Node("Text Area", root);
        Stretch(viewport, 8f, 4f, 8f, 4f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var text = AddLabel(viewport, "", T.fontSizeSmall, T.text, TextAlignmentOptions.MidlineLeft);
        Stretch(text.rectTransform);
        var ph = AddLabel(viewport, placeholder, T.fontSizeSmall, T.textDim, TextAlignmentOptions.MidlineLeft);
        ph.fontStyle = FontStyles.Italic;
        Stretch(ph.rectTransform);

        var input = root.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = bg;
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = ph;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = characterLimit;
        if (onEndEdit != null) input.onEndEdit.AddListener(onEndEdit);
        return input;
    }

    /// <summary>Marks a button as the selected option (e.g. the current warp level).</summary>
    public static void SetButtonActive(Button btn, bool active)
    {
        ColorBlock cb = btn.colors;
        Color target = active ? T.accentDim : T.buttonNormal;
        if (cb.normalColor == target) return;
        cb.normalColor = target;
        cb.selectedColor = target;
        btn.colors = cb;
    }

    // --- Layout ------------------------------------------------------------
    public static HorizontalLayoutGroup HStack(RectTransform rt, float spacing, int padding, bool expandWidth = false)
    {
        var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        g.spacing = spacing;
        g.padding = new RectOffset(padding, padding, padding, padding);
        g.childControlWidth = true;
        g.childControlHeight = true;
        g.childForceExpandWidth = expandWidth;
        g.childForceExpandHeight = false;
        return g;
    }

    public static VerticalLayoutGroup VStack(RectTransform rt, float spacing, int padding, bool expandWidth = true)
    {
        var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        g.spacing = spacing;
        g.padding = new RectOffset(padding, padding, padding, padding);
        g.childControlWidth = true;
        g.childControlHeight = true;
        g.childForceExpandWidth = expandWidth;
        g.childForceExpandHeight = false;
        return g;
    }

    public static RectTransform AddSpacer(Transform parent, float height = 0f, float flexibleWidth = 0f)
    {
        var rt = Node("Spacer", parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.flexibleWidth = flexibleWidth;
        return rt;
    }
}

/// <summary>A horizontal fill bar with a title on the left and a value on the right.</summary>
public sealed class UIBar
{
    private RectTransform _fill;
    private Image _fillImage;
    private TextMeshProUGUI _value;

    public static UIBar Create(Transform parent, string title, float width, float height)
    {
        UITheme t = UITheme.Current;
        var bar = new UIBar();

        Image back = UIKit.AddPanel(parent, "Bar", t.barBack);
        UIKit.Size(back.rectTransform, preferredWidth: width, minHeight: height);

        Image fill = UIKit.AddPanel(back.transform, "Fill", t.accent);
        bar._fill = fill.rectTransform;
        bar._fillImage = fill;
        bar._fill.anchorMin = Vector2.zero;
        bar._fill.anchorMax = Vector2.one;
        bar._fill.offsetMin = new Vector2(2f, 2f);
        bar._fill.offsetMax = new Vector2(-2f, -2f);

        var titleLabel = UIKit.AddLabel(back.transform, title, t.fontSizeSmall, t.text, TextAlignmentOptions.MidlineLeft);
        UIKit.Stretch(titleLabel.rectTransform, left: 10f, right: 10f);

        bar._value = UIKit.AddLabel(back.transform, "", t.fontSizeSmall, t.text, TextAlignmentOptions.MidlineRight);
        UIKit.Stretch(bar._value.rectTransform, left: 10f, right: 10f);

        return bar;
    }

    public void Set(float fraction01, string valueText, Color fillColor)
    {
        fraction01 = Mathf.Clamp01(fraction01);
        // The fill is inset by 2px; scale its right anchor edge by the fraction.
        _fill.anchorMax = new Vector2(fraction01, 1f);
        _fillImage.color = fillColor;
        UIKit.SetText(_value, valueText);
    }
}
