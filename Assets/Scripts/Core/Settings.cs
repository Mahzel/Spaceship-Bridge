using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Player-rebindable actions. Order matters: it indexes the binding array in SettingsData.</summary>
public enum GameAction
{
    AimLeft, AimRight, AimUp, AimDown, Fine, Confirm,
    Pause, WarpUp, WarpDown, Menu
}

public enum TextSize { Small, Normal, Large }
public enum ThemePreset { Phosphor, Amber, Cyan, HighContrast }

/// <summary>
/// Everything the Settings screen edits. Serialized as JSON in PlayerPrefs (key "settings.v1"), so it survives
/// between sessions. No game save exists yet: a quit ends the expedition, but the settings stay.
/// </summary>
[Serializable]
public sealed class SettingsData
{
    // Display
    public float uiScale = 1f;                 // 0.7 .. 1.5, applied to the canvas scaler
    public TextSize textSize = TextSize.Normal;
    public ThemePreset theme = ThemePreset.Phosphor;

    // Time
    public int  defaultWarp = 0;               // index into GameClock.WarpLadder, applied at every launch
    public bool startPaused = false;           // a new probe starts with the clock paused
    public bool pauseOnFocusLoss = true;       // alt-tab pauses, coming back resumes

    // Controls: two slots per GameAction (primary, secondary), Key names; "" = unbound.
    public string[] bindings = Settings.DefaultBindings();
}

/// <summary>
/// Global settings access: Settings.Data to read, Settings.Save() after editing (raises Changed). Loaded on
/// first use. Missing or unreadable data falls back to defaults field by field (JsonUtility keeps the class
/// initialisers for anything absent), so adding a setting later doesn't wipe the others.
/// </summary>
public static class Settings
{
    private const string PrefKey = "settings.v1";
    public const int Slots = 2;

    public const float UiScaleMin = 0.7f, UiScaleMax = 1.5f;

    private static SettingsData _data;
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _data = null; Changed = null; }

    public static SettingsData Data
    {
        get
        {
            if (_data == null) Load();
            return _data;
        }
    }

    public static void Load()
    {
        _data = null;
        try
        {
            string json = PlayerPrefs.GetString(PrefKey, "");
            if (!string.IsNullOrEmpty(json)) _data = JsonUtility.FromJson<SettingsData>(json);
        }
        catch (Exception e) { Debug.LogWarning("[Settings] unreadable, using defaults: " + e.Message); }
        if (_data == null) _data = new SettingsData();
        Sanitize(_data);
    }

    public static void Save()
    {
        Sanitize(Data);
        try
        {
            PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(_data));
            PlayerPrefs.Save();
        }
        catch (Exception e) { Debug.LogWarning("[Settings] could not save: " + e.Message); }
        if (Changed != null) Changed();
    }

    private static void Sanitize(SettingsData d)
    {
        d.uiScale = Mathf.Clamp(d.uiScale <= 0f ? 1f : d.uiScale, UiScaleMin, UiScaleMax);
        d.defaultWarp = Mathf.Clamp(d.defaultWarp, 0, GameClock.WarpLadder.Length - 1);
        int n = Enum.GetValues(typeof(GameAction)).Length * Slots;
        if (d.bindings == null || d.bindings.Length != n)
        {
            string[] def = DefaultBindings();
            if (d.bindings != null)
                for (int i = 0; i < Math.Min(n, d.bindings.Length); i++) def[i] = d.bindings[i];
            d.bindings = def;
        }
    }

    public static string[] DefaultBindings()
    {
        int count = Enum.GetValues(typeof(GameAction)).Length;
        var b = new string[count * Slots];
        for (int i = 0; i < b.Length; i++) b[i] = "";
        Set(b, GameAction.AimLeft,  Key.LeftArrow,  Key.None);
        Set(b, GameAction.AimRight, Key.RightArrow, Key.None);
        Set(b, GameAction.AimUp,    Key.UpArrow,    Key.None);
        Set(b, GameAction.AimDown,  Key.DownArrow,  Key.None);
        Set(b, GameAction.Fine,     Key.LeftShift,  Key.RightShift);
        Set(b, GameAction.Confirm,  Key.Enter,      Key.NumpadEnter);
        Set(b, GameAction.Pause,    Key.Space,      Key.None);
        Set(b, GameAction.WarpUp,   Key.Period,     Key.NumpadPlus);
        Set(b, GameAction.WarpDown, Key.Comma,      Key.NumpadMinus);
        Set(b, GameAction.Menu,     Key.Escape,     Key.None);
        return b;
    }

    private static void Set(string[] b, GameAction a, Key k1, Key k2)
    {
        b[(int)a * Slots]     = k1 == Key.None ? "" : k1.ToString();
        b[(int)a * Slots + 1] = k2 == Key.None ? "" : k2.ToString();
    }
}

/// <summary>
/// Reads the rebindable actions from the keyboard (Input System). Blocked while a text field has focus or
/// while a menu has taken over input (Blocked), so typing a track name or rebinding a key never fires a
/// game action. Menu is still readable while blocked, through MenuPressedRaw.
/// </summary>
public static class InputMap
{
    /// <summary>Set by the menu while it's open: gameplay actions read as idle.</summary>
    public static bool Blocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Blocked = false; }

    public static Key GetKey(GameAction a, int slot)
    {
        string name = Settings.Data.bindings[(int)a * Settings.Slots + slot];
        if (string.IsNullOrEmpty(name)) return Key.None;
        Key k;
        return Enum.TryParse(name, out k) ? k : Key.None;
    }

    public static void SetKey(GameAction a, int slot, Key k)
    {
        Settings.Data.bindings[(int)a * Settings.Slots + slot] = k == Key.None ? "" : k.ToString();
    }

    /// <summary>Held down (for continuous slews and modifiers).</summary>
    public static bool Held(GameAction a)
    {
        if (!Usable()) return false;
        return Is(a, true);
    }

    /// <summary>Went down this frame.</summary>
    public static bool Pressed(GameAction a)
    {
        if (!Usable()) return false;
        return Is(a, false);
    }

    /// <summary>The Menu action regardless of Blocked (the menu itself needs it to close).</summary>
    public static bool MenuPressedRaw()
    {
        if (Keyboard.current == null || TextFieldFocused()) return false;
        return Is(GameAction.Menu, false);
    }

    private static bool Is(GameAction a, bool held)
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return false;
        for (int s = 0; s < Settings.Slots; s++)
        {
            Key k = GetKey(a, s);
            if (k == Key.None) continue;
            var ctl = kb[k];
            if (ctl == null) continue;
            if (held ? ctl.isPressed : ctl.wasPressedThisFrame) return true;
        }
        return false;
    }

    private static bool Usable()
    {
        return !Blocked && Keyboard.current != null && !TextFieldFocused();
    }

    public static bool TextFieldFocused()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        GameObject sel = es != null ? es.currentSelectedGameObject : null;
        if (sel == null) return false;
        var field = sel.GetComponent<TMPro.TMP_InputField>();
        return field != null && field.isFocused;
    }

    /// <summary>Player-facing name of a key ("Left Arrow", "Space", "-").</summary>
    public static string KeyLabel(Key k)
    {
        if (k == Key.None) return "--";
        Keyboard kb = Keyboard.current;
        if (kb != null && kb[k] != null && !string.IsNullOrEmpty(kb[k].displayName)) return kb[k].displayName;
        return k.ToString();
    }
}
