using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Provides visual and haptic feedback on a successful catch:
///  - Gold screen flash (alpha 0.25 → 0 over 0.1s)
///  - Haptic vibration on mobile (Handheld.Vibrate)
/// Creates its own Canvas/Image at runtime. No coroutines — Update-driven.
/// </summary>
public sealed class CatchFeedback : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static CatchFeedback Instance { get; private set; }

    // ── Config ──────────────────────────────────────────────────────
    const float k_FlashDuration = 0.1f;
    const float k_FlashAlpha    = 0.25f;
    static readonly Color s_FlashColor = new Color(1f, 0.82f, 0.24f); // gold #FFD23F

    // ── State ───────────────────────────────────────────────────────
    Image _flashImage;
    float _flashTimer;
    bool  _isFlashing;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[CatchFeedback] Duplicate destroyed on '{gameObject.name}'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        CreateFlashOverlay();

        // Subscribe to score events — every catch triggers a flash
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreAdded += HandleCatch;
        else
            Debug.LogError("[CatchFeedback] ScoreManager.Instance is NULL in Start!");
    }

    void OnDisable()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreAdded -= HandleCatch;
    }

    void Update()
    {
        if (!_isFlashing) return;

        _flashTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_flashTimer / k_FlashDuration);

        // Fade alpha from peak → 0
        Color c = s_FlashColor;
        c.a = k_FlashAlpha * (1f - t);
        _flashImage.color = c;

        if (t >= 1f)
        {
            _isFlashing = false;
            _flashImage.gameObject.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    void HandleCatch(float earnedPrice, Vector3 catchPos)
    {
        TriggerFlash();
        TriggerHaptic();
    }

    /// <summary>Trigger the gold screen flash.</summary>
    public void TriggerFlash()
    {
        if (_flashImage == null) return;

        _flashTimer = 0f;
        _isFlashing = true;
        _flashImage.gameObject.SetActive(true);

        Color c = s_FlashColor;
        c.a = k_FlashAlpha;
        _flashImage.color = c;
    }

    /// <summary>Short haptic vibration on mobile devices.</summary>
    static void TriggerHaptic()
    {
#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
#endif
    }

    // ─────────────────────────────────────────────────────────────────
    void CreateFlashOverlay()
    {
        // Canvas — highest sorting order, no raycasting (pass-through)
        GameObject canvasGo = new GameObject("CatchFlashCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        // No GraphicRaycaster — clicks pass through

        // Full-screen image
        GameObject imgGo = new GameObject("FlashImage");
        imgGo.transform.SetParent(canvasGo.transform, false);

        RectTransform rt = imgGo.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _flashImage = imgGo.AddComponent<Image>();
        _flashImage.color = Color.clear;
        _flashImage.raycastTarget = false;
        imgGo.SetActive(false);

        Debug.Log("[CatchFeedback] Flash overlay created.");
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
