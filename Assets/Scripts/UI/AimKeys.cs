using UnityEngine;

/// <summary>
/// Keyboard slew for whichever aimable sensor screen is showing, read through the rebindable InputMap
/// (Settings > Controls): AimLeft/Right/Up/Down give an axis in -1..1, Fine scales it by FineFactor. The
/// screens call this only while their own tab is visible, so only one sensor slews at a time. Silent while
/// a text field has focus or a menu is open (InputMap handles both).
/// </summary>
public static class AimKeys
{
    public const float FineFactor = 0.2f;

    /// <summary>x = Right - Left, y = Up - Down, scaled by FineFactor while Fine is held. Zero if unavailable.</summary>
    public static Vector2 Slew()
    {
        float x = (InputMap.Held(GameAction.AimRight) ? 1f : 0f) - (InputMap.Held(GameAction.AimLeft) ? 1f : 0f);
        float y = (InputMap.Held(GameAction.AimUp) ? 1f : 0f) - (InputMap.Held(GameAction.AimDown) ? 1f : 0f);
        Vector2 v = new Vector2(x, y);
        if (InputMap.Held(GameAction.Fine)) v *= FineFactor;
        return v;
    }

    /// <summary>True on the frame Confirm went down.</summary>
    public static bool ConfirmPressed() { return InputMap.Pressed(GameAction.Confirm); }
}
