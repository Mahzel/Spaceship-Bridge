using UnityEngine;

/// <summary>
/// TEMPORARY DEBUG. Logs the call stack whenever the object it is attached to gets disabled,
/// to find out who deactivates the player ship. Delete once the bug is fixed.
/// </summary>
public class ShipDeactivationTrace : MonoBehaviour
{
    private void OnDisable()
    {
        Debug.LogWarning($"[ShipDeactivationTrace] '{name}' désactivé (activeSelf={gameObject.activeSelf}, "
            + $"parent={(transform.parent ? transform.parent.name : "(racine)")}).\n"
            + StackTraceUtility.ExtractStackTrace());
    }
}
