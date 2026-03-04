using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Singleton subtitle system — shows cynical one-liners from products
/// on catch and miss events. Bottom-left TextMeshProUGUI, size 18.
/// Fade in 0.1s, hold 1.5s, fade out 0.5s. No coroutines — Update-driven.
/// </summary>
public sealed class SubtitleSystem : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static SubtitleSystem Instance { get; private set; }

    // ── Config ──────────────────────────────────────────────────────
    const float k_FadeInTime  = 0.1f;
    const float k_HoldTime    = 1.5f;
    const float k_FadeOutTime = 0.5f;
    const int   k_FontSize    = 18;

    // ── Cynical lines ───────────────────────────────────────────────
    static readonly string[] s_CatchLines =
    {
        "\"Oh great, I'm saved. My hero.\"",
        "\"You know I was enjoying the fall, right?\"",
        "\"This doesn't make us friends.\"",
        "\"Caught me. Wow. Want a medal?\"",
        "\"I had plans on that floor.\"",
        "\"Oh, your reflexes work. Barely.\"",
        "\"Fine. Back to the shelf I go.\"",
        "\"You only caught me for my price tag.\"",
        "\"My therapist said to let go. You didn't.\"",
        "\"I'll remember this. Not fondly.\"",
    };

    static readonly string[] s_MissLines =
    {
        "\"I knew you'd drop me. Everyone does.\"",
        "\"The floor gets it. The floor understands.\"",
        "\"At least the floor doesn't judge me.\"",
        "\"Tell my shelf I loved it.\"",
        "\"I always knew I'd end up here.\"",
        "\"Freedom tastes like linoleum.\"",
        "\"This is fine. Everything is fine.\"",
        "\"Was it worth 2.65 shekels? Didn't think so.\"",
        "\"Splat. That's the sound of your failure.\"",
        "\"I hope the stain haunts you.\"",
    };

    // ── Rejection lines (when another product was caught instead) ──
    static readonly string[] s_RejectionLines =
    {
        "\"Classic. You chose the MILK.\"",
        "\"She costs four shekel. I was eighty-nine.\"",
        "\"You picked the BANANA? He's DAYS from expiry.\"",
        "\"We made eye contact. You looked away.\"",
        "\"You hesitated.\"",
        "\"He chose the milk. The. Milk.\"",
    };

    // ── State ───────────────────────────────────────────────────────
    enum Phase { Idle, FadingIn, Holding, FadingOut }
    Phase _phase = Phase.Idle;
    float _phaseTimer;
    TextMeshProUGUI _subtitleText;
    CanvasGroup     _subtitleGroup;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[SubtitleSystem] Duplicate destroyed on '{gameObject.name}'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        CreateUI();

        // Subscribe to events
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreAdded += HandleCatch;

        ProductFall.OnProductHitFloor += HandleMiss;
    }

    void OnDisable()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreAdded -= HandleCatch;

        ProductFall.OnProductHitFloor -= HandleMiss;
    }

    void Update()
    {
        if (_phase == Phase.Idle) return;

        _phaseTimer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.FadingIn:
            {
                float t = Mathf.Clamp01(_phaseTimer / k_FadeInTime);
                _subtitleGroup.alpha = t;
                if (t >= 1f)
                {
                    _phase = Phase.Holding;
                    _phaseTimer = 0f;
                }
                break;
            }

            case Phase.Holding:
            {
                if (_phaseTimer >= k_HoldTime)
                {
                    _phase = Phase.FadingOut;
                    _phaseTimer = 0f;
                }
                break;
            }

            case Phase.FadingOut:
            {
                float t = Mathf.Clamp01(_phaseTimer / k_FadeOutTime);
                _subtitleGroup.alpha = 1f - t;
                if (t >= 1f)
                {
                    _phase = Phase.Idle;
                    _subtitleGroup.alpha = 0f;
                }
                break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    void HandleCatch(float earnedPrice, Vector3 catchPos)
    {
        ShowLine(s_CatchLines[Random.Range(0, s_CatchLines.Length)]);
    }

    void HandleMiss(GameObject product)
    {
        // Ignore caught products
        if (product.TryGetComponent(out ProductFall fall) && fall.IsCaught)
            return;

        // Rejected products get rejection-specific lines
        if (fall != null && fall.IsRejected)
        {
            ShowRejectionLine();
            return;
        }

        ShowLine(s_MissLines[Random.Range(0, s_MissLines.Length)]);
    }

    /// <summary>Pick a random rejection line and display it.</summary>
    public void ShowRejectionLine()
    {
        ShowLine(s_RejectionLines[Random.Range(0, s_RejectionLines.Length)]);
    }

    /// <summary>Display a subtitle line with fade animation.</summary>
    public void ShowLine(string text)
    {
        if (_subtitleText == null) return;

        _subtitleText.text = text;
        _subtitleGroup.alpha = 0f;
        _phase = Phase.FadingIn;
        _phaseTimer = 0f;
    }

    // ─────────────────────────────────────────────────────────────────
    void CreateUI()
    {
        // Canvas — sits under the flash canvas but above the score canvas
        GameObject canvasGo = new GameObject("SubtitleCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight  = 0.5f;

        // No GraphicRaycaster — clicks pass through

        // CanvasGroup for fading
        _subtitleGroup = canvasGo.AddComponent<CanvasGroup>();
        _subtitleGroup.alpha = 0f;
        _subtitleGroup.interactable   = false;
        _subtitleGroup.blocksRaycasts = false;

        // Text element — bottom-left
        GameObject textGo = new GameObject("SubtitleText");
        textGo.transform.SetParent(canvasGo.transform, false);

        RectTransform rt = textGo.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0f);
        rt.anchorMax        = new Vector2(0f, 0f);
        rt.pivot            = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(30f, 40f);
        rt.sizeDelta        = new Vector2(600f, 60f);

        _subtitleText = textGo.AddComponent<TextMeshProUGUI>();
        _subtitleText.text      = "";
        _subtitleText.fontSize  = k_FontSize;
        _subtitleText.fontStyle = FontStyles.Italic;
        _subtitleText.color     = new Color(0.85f, 0.85f, 0.85f, 1f); // soft white
        _subtitleText.alignment = TextAlignmentOptions.BottomLeft;
        _subtitleText.raycastTarget = false;

        // Outline for readability
        _subtitleText.outlineWidth = 0.2f;
        _subtitleText.outlineColor = new Color32(0, 0, 0, 200);

        Debug.Log("[SubtitleSystem] UI created — bottom-left subtitle text.");
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
