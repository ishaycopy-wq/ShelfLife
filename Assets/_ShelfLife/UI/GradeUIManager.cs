using UnityEngine;

/// <summary>
/// Listens for ScoreManager catch events and spawns a world-space floating
/// TextMesh showing the price earned (e.g., "+₪89.90") in gold.
/// Uses legacy TextMesh for guaranteed rendering without font asset issues.
/// </summary>
public sealed class GradeUIManager : MonoBehaviour
{
    // ── Config ──────────────────────────────────────────────────────
    const float k_CharacterSize = 0.1f;
    const int   k_FontSize      = 50;
    const float k_OffsetY       = 0.4f;

    // Gold price color — #FFD23F
    static readonly Color s_PriceColor = new Color32(255, 210, 63, 255);

    // ─────────────────────────────────────────────────────────────────
    // Subscribe in Start() — all Awake() singletons are guaranteed set by then
    void Start()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreAdded += HandleScoreAdded;
        else
            Debug.LogError("[GradeUIManager] ScoreManager.Instance is NULL in Start — floating text won't work!");
    }

    void OnDisable()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreAdded -= HandleScoreAdded;
    }

    // ─────────────────────────────────────────────────────────────────
    void HandleScoreAdded(float earnedPrice, Vector3 catchPosition)
    {
        // ── Create world-space TextMesh ─────────────────────────────
        string priceText = $"+₪{earnedPrice:F2}";

        GameObject go = new GameObject($"PriceText_{earnedPrice:F0}");
        go.transform.position = catchPosition + Vector3.up * k_OffsetY;

        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text          = priceText;
        tm.fontSize      = k_FontSize;
        tm.characterSize = k_CharacterSize;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = s_PriceColor;
        tm.fontStyle     = FontStyle.Bold;

        // Render on top
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
            mr.sortingOrder = 100;

        // Billboard toward camera
        Camera cam = Camera.main;
        if (cam != null)
            go.transform.forward = cam.transform.forward;

        // Rise + fade + self-destruct
        go.AddComponent<FloatingText>();

        Debug.Log($"[GradeUIManager] Spawned floating text '{priceText}' at {catchPosition}");
    }
}
