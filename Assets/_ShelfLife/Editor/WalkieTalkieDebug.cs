using UnityEditor;
using UnityEngine;

public static class WalkieTalkieDebug
{
    [MenuItem("ShelfLife/Test Walkie-Talkie")]
    static void TestWalkieTalkie()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[WalkieTalkie] Enter Play mode first — Instance is null in Edit mode.");
            return;
        }

        if (WalkieTalkieSystem.Instance == null)
        {
            Debug.LogError("[WalkieTalkie] Instance is NULL. Is WalkieTalkieSystem on the InputManager GameObject?");
            return;
        }

        WalkieTalkieSystem.Instance.PlayRandom();
        Debug.Log("[WalkieTalkie] Debug — triggered random line from editor menu.");
    }
}
