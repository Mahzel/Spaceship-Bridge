using TMPro;
using UnityEngine;

/// <summary>
/// The look of every code-built screen: colors, font, sizes, spacing. Change it here, everything follows.
/// Create one with Assets > Create > Bridge > UI Theme and put it anywhere under a Resources folder
/// (e.g. Assets/Resources/UITheme.asset). Without an asset, these defaults are used.
/// Sizes are in reference pixels (the canvas scales from 1920x1080).
///
/// Palette: phosphor-green console. Text/accent/good all sit in the green family on purpose - the classic
/// monochrome sensor-terminal look, where the whole display defaults to one color and only the two states
/// that actually need to grab your eye (warning, danger) break from it. warning stays amber and danger
/// stays red regardless of theme - overriding those into the green family would blunt exactly the contrast
/// that makes them readable as "something needs attention."
/// </summary>
[CreateAssetMenu(menuName = "Bridge/UI Theme", fileName = "UITheme")]
public class UITheme : ScriptableObject
{
    [Header("Font (empty = TMP default)")]
    public TMP_FontAsset font;

    [Header("Surfaces")]
    public Color panelColor = new Color(0.02f, 0.07f, 0.03f, 0.94f);
    public Color dimColor   = new Color(0f, 0f, 0f, 0.70f);
    public Color barBack    = new Color(0.06f, 0.12f, 0.07f, 1f);

    [Header("Text")]
    public Color text    = new Color(0.78f, 0.98f, 0.80f, 1f);
    public Color textDim = new Color(0.40f, 0.58f, 0.42f, 1f);

    [Header("Accents")]
    public Color accent    = new Color(0.35f, 0.95f, 0.45f, 1f);
    public Color accentDim = new Color(0.13f, 0.42f, 0.18f, 1f);
    public Color good      = new Color(0.55f, 1.00f, 0.35f, 1f);
    public Color warning   = new Color(1.00f, 0.70f, 0.20f, 1f);
    public Color danger    = new Color(1.00f, 0.30f, 0.30f, 1f);
    public Color hydrogenColor = new Color(0.40f, 0.85f, 0.85f, 1f);
    public Color storageColor  = new Color(0.65f, 0.90f, 0.40f, 1f);

    [Header("Buttons")]
    public Color buttonNormal  = new Color(0.06f, 0.14f, 0.08f, 1f);
    public Color buttonHover   = new Color(0.12f, 0.24f, 0.14f, 1f);
    public Color buttonPressed = new Color(0.04f, 0.10f, 0.05f, 1f);

    [Header("Sizes")]
    public int   fontSizeSmall = 15;
    public int   fontSizeBody  = 20;
    public int   fontSizeTitle = 36;
    public float padding = 12f;
    public float spacing = 8f;
    public float statusBarHeight = 76f;

    // ------------------------------------------------------------------
    private static UITheme _current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _current = null; }

    public static UITheme Current
    {
        get
        {
            if (_current == null)
            {
                var found = Resources.LoadAll<UITheme>("");
                _current = found.Length > 0 ? found[0] : CreateInstance<UITheme>();
            }
            return _current;
        }
    }
}
