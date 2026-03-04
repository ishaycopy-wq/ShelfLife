#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Debug menu items for CameraStateManager — force camera state transitions.
/// Play Mode only. ShelfLife/Camera/ menu group.
/// </summary>
public static class CameraDebug
{
    [MenuItem("ShelfLife/Camera/Force WideShot")]
    static void ForceWideShot()
    {
        if (CameraStateManager.Instance != null)
            CameraStateManager.Instance.ForceState(CameraStateManager.CameraState.WideShot);
        else
            Debug.LogWarning("[CameraDebug] CameraStateManager not found — enter Play Mode first.");
    }

    [MenuItem("ShelfLife/Camera/Force FollowShot")]
    static void ForceFollowShot()
    {
        if (CameraStateManager.Instance != null)
            CameraStateManager.Instance.ForceState(CameraStateManager.CameraState.FollowShot);
        else
            Debug.LogWarning("[CameraDebug] CameraStateManager not found — enter Play Mode first.");
    }

    [MenuItem("ShelfLife/Camera/Force EngagementShot")]
    static void ForceEngagementShot()
    {
        if (CameraStateManager.Instance != null)
            CameraStateManager.Instance.ForceState(CameraStateManager.CameraState.EngagementShot);
        else
            Debug.LogWarning("[CameraDebug] CameraStateManager not found — enter Play Mode first.");
    }
}
#endif
