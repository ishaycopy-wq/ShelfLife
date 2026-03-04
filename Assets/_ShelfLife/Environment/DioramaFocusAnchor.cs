using UnityEngine;

/// <summary>
/// Marks a Transform as the DOF focus target for the diorama camera.
/// CameraStateManager can reference DioramaFocusAnchor.Instance.FocusPoint
/// to dynamically compute focus distance. Does NOT modify camera rotation.
/// </summary>
public sealed class DioramaFocusAnchor : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static DioramaFocusAnchor Instance { get; private set; }

    /// <summary>World-space position of the DOF focus target.</summary>
    public Vector3 FocusPoint => transform.position;

    // ────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[DioramaFocusAnchor] Duplicate destroyed on '{gameObject.name}'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ── Editor visualization ────────────────────────────────────────
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        // Always-visible yellow crosshair at focus point
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.25f);

        // Draw lines along axes for easy identification
        float lineLen = 0.4f;
        Gizmos.DrawLine(transform.position - Vector3.right * lineLen,
                        transform.position + Vector3.right * lineLen);
        Gizmos.DrawLine(transform.position - Vector3.up * lineLen,
                        transform.position + Vector3.up * lineLen);
        Gizmos.DrawLine(transform.position - Vector3.forward * lineLen,
                        transform.position + Vector3.forward * lineLen);
    }

    void OnDrawGizmosSelected()
    {
        // Larger sphere when selected
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawSphere(transform.position, 0.5f);
    }
#endif
}
