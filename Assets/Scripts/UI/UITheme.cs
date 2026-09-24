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

    [Header("Waterfall overlays (drawn over the green waterfall, so never green themselves)")]
    public Color trackLocked   = new Color(0.78f, 0.45f, 1.00f, 1f); // purple
    public Color trackSelected = new Color(1.00f, 0.55f, 0.95f, 1f); // bright magenta
    public Color headingColor  = new Color(0.35f, 0.65f, 1.00f, 1f); // blue: the ship's heading tick

    /// <summary>Colour of a track's marks on the waterfall and its DSP strip. Searching stays the danger red
    /// (same as its Track panel row); locked and selected use purples, which stand out on the green image.</summary>
    public Color WaterfallTrackColor(bool selected, bool searching)
    {
        return selected ? trackSelected : (searching ? danger : trackLocked);
    }

    [Header("Nav map")]
    public Color navOrbit  = new Color(0.35f, 0.95f, 0.45f, 1f); // ship's own conic - exact, so it gets the accent colour
    public Color navBody   = new Color(0.85f, 0.85f, 0.60f, 1f); // primary marker
    public Color navNode   = new Color(1.00f, 0.85f, 0.30f, 1f); // Pe/Ap markers
    public Color navPlane  = new Color(0.55f, 0.70f, 1.00f, 1f); // AN/DN reference-plane crossings

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
    // Runtime: Current is a CLONE of the asset (or of the C# defaults when there's no asset), with the player's
    // Settings (colour preset, text size) applied on top. The asset itself is never modified at runtime, so
    // changing the theme in play mode can't write into Assets/Resources/UITheme.asset.
    private static UITheme _current;
    private static UITheme _base;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _current = null; _base = null; }

    public static UITheme Current
    {
        get
        {
            if (_current == null) Rebuild();
            return _current;
        }
    }

    /// <summary>Re-derives Current from the asset + Settings. Screens capture colours and sizes when they are
    /// built, so after this the UI has to be rebuilt too (GameUI.RebuildUI does both).</summary>
    public static void Rebuild()
    {
        if (_base == null)
        {
            var found = Resources.LoadAll<UITheme>("");
            _base = found.Length > 0 ? found[0] : CreateInstance<UITheme>();
        }
        if (_current != null && _current != _base) Destroy(_current);
        _current = Instantiate(_base);
        _current.name = "UITheme (runtime)";
        SettingsData s = Settings.Data;
        ApplyPreset(_current, s.theme);
        ApplyTextSize(_current, s.textSize);
    }

    private static void ApplyTextSize(UITheme t, TextSize size)
    {
        float k = size == TextSize.Small ? 0.85f : size == TextSize.Large ? 1.2f : 1f;
        t.fontSizeSmall = Mathf.RoundToInt(t.fontSizeSmall * k);
        t.fontSizeBody  = Mathf.RoundToInt(t.fontSizeBody * k);
        t.fontSizeTitle = Mathf.RoundToInt(t.fontSizeTitle * k);
    }

    /// <summary>Colour presets. Phosphor keeps the asset's own colours (the reference look). The others
    /// re-tint the monochrome family (surfaces, text, accent, buttons) and leave warning/danger alone, so
    /// "needs attention" reads the same in every theme (see the class doc).</summary>
    private static void ApplyPreset(UITheme t, ThemePreset preset)
    {
        switch (preset)
        {
            case ThemePreset.Amber:
                Mono(t, new Color(1.00f, 0.72f, 0.28f), new Color(0.07f, 0.045f, 0.01f));
                t.warning = new Color(1.00f, 0.95f, 0.55f, 1f); // must still stand out from an amber UI
                break;
            case ThemePreset.Cyan:
                Mono(t, new Color(0.45f, 0.85f, 1.00f), new Color(0.01f, 0.05f, 0.08f));
                break;
            case ThemePreset.HighContrast:
                t.panelColor    = new Color(0f, 0f, 0f, 0.97f);
                t.barBack       = new Color(0.12f, 0.12f, 0.12f, 1f);
                t.text          = Color.white;
                t.textDim       = new Color(0.78f, 0.78f, 0.78f, 1f);
                t.accent        = new Color(1.00f, 0.92f, 0.20f, 1f);
                t.accentDim     = new Color(0.45f, 0.40f, 0.05f, 1f);
                t.good          = new Color(0.40f, 1.00f, 0.40f, 1f);
                t.buttonNormal  = new Color(0.16f, 0.16f, 0.16f, 1f);
                t.buttonHover   = new Color(0.30f, 0.30f, 0.30f, 1f);
                t.buttonPressed = new Color(0.08f, 0.08f, 0.08f, 1f);
                break;
        }
    }

    // One hue, several brightness levels: the same structure as the phosphor-green default.
    private static void Mono(UITheme t, Color hue, Color surface)
    {
        t.panelColor    = new Color(surface.r, surface.g, surface.b, 0.94f);
        t.barBack       = Color.Lerp(surface, hue, 0.08f);
        t.text          = Color.Lerp(hue, Color.white, 0.55f);
        t.textDim       = Color.Lerp(surface, hue, 0.55f);
        t.accent        = hue;
        t.accentDim     = Color.Lerp(surface, hue, 0.35f);
        t.good          = Color.Lerp(hue, Color.white, 0.25f);
        t.storageColor  = Color.Lerp(hue, Color.white, 0.15f);
        t.buttonNormal  = Color.Lerp(surface, hue, 0.10f);
        t.buttonHover   = Color.Lerp(surface, hue, 0.20f);
        t.buttonPressed = Color.Lerp(surface, hue, 0.05f);
        t.text.a = t.textDim.a = t.accent.a = t.accentDim.a = t.good.a = t.storageColor.a = 1f;
        t.barBack.a = t.buttonNormal.a = t.buttonHover.a = t.buttonPressed.a = 1f;
    }
}
