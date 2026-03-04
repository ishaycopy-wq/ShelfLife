using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visual Silence — 1.5 seconds of quiet after a product hits the floor.
/// Fades HUD to 0, drops AudioListener volume, pauses spawner.
/// Non-retriggerable while active. No coroutines — Update-driven timer.
///
/// Per CLAUDE.md: "Visual Silence: 1.5s after cascade, NO UI, NO sound"
/// </summary>
public sealed class VisualSilenceController : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static VisualSilenceController Instance { get; private set; }

    // ── Config ──────────────────────────────────────────────────────
    const float k_SilenceDuration = 1.5f;
    const float k_FadeOutTime     = 0.2f;   // UI fades to 0 over this
    const float k_AudioFadeTime   = 0.3f;   // audio dips over this
    const float k_MinVolume       = 0.1f;   // quiet but not mute

    // ── Public state ────────────────────────────────────────────────
    /// <summary>True while the Visual Silence period is active.</summary>
    public bool IsSilent { get; private set; }

    // ── State ───────────────────────────────────────────────────────
    float _silenceTimer;
    float _audioFadeTimer;
    bool  _fadingOut;    // true = fading into silence
    bool  _fadingIn;     // true = restoring from silence
    CanvasGroup _hudGroup;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[VisualSilence] Duplicate destroyed on '{gameObject.name}'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // Subscribe to floor hits
        ProductFall.OnProductHitFloor += HandleMiss;
    }

    void OnDisable()
    {
        ProductFall.OnProductHitFloor -= HandleMiss;
    }

    void Update()
    {
        if (!IsSilent) return;

        // ── Fade OUT phase (entering silence) ─────────────────────
        if (_fadingOut)
        {
            _audioFadeTimer += Time.deltaTime;
            float audioT = Mathf.Clamp01(_audioFadeTimer / k_AudioFadeTime);
            AudioListener.volume = Mathf.Lerp(1f, k_MinVolume, audioT);

            float uiT = Mathf.Clamp01(_audioFadeTimer / k_FadeOutTime);
            if (_hudGroup != null)
                _hudGroup.alpha = 1f - uiT;

            if (audioT >= 1f && uiT >= 1f)
                _fadingOut = false;
        }

        // ── Silence timer ─────────────────────────────────────────
        _silenceTimer += Time.deltaTime;

        if (_silenceTimer >= k_SilenceDuration && !_fadingIn)
        {
            _fadingIn = true;
            _audioFadeTimer = 0f;
        }

        // ── Fade IN phase (restoring) ─────────────────────────────
        if (_fadingIn)
        {
            _audioFadeTimer += Time.deltaTime;
            float audioT = Mathf.Clamp01(_audioFadeTimer / k_AudioFadeTime);
            AudioListener.volume = Mathf.Lerp(k_MinVolume, 1f, audioT);

            float uiT = Mathf.Clamp01(_audioFadeTimer / k_FadeOutTime);
            if (_hudGroup != null)
                _hudGroup.alpha = uiT;

            if (audioT >= 1f)
            {
                // Fully restored
                IsSilent  = false;
                _fadingIn = false;
                AudioListener.volume = 1f;
                if (_hudGroup != null)
                    _hudGroup.alpha = 1f;

                // Resume spawner (unless shift already ended — ScoreManager controls that)
                if (ProductSpawner.Instance != null &&
                    ScoreManager.Instance != null &&
                    ScoreManager.Instance.FloorMeter < 3)
                {
                    ProductSpawner.Instance.isPaused = false;
                }

                Debug.Log("[VisualSilence] Silence ended — restored.");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    void HandleMiss(GameObject product)
    {
        // Safety: ignore caught products
        if (product.TryGetComponent(out ProductFall fall) && fall.IsCaught)
            return;

        // During cascade, don't trigger per-product — CascadeSystem
        // will call TriggerSilence() once after the entire cascade ends
        if (CascadeSystem.Instance != null && CascadeSystem.Instance.IsCascading)
            return;

        // Non-retriggerable while already in silence
        if (IsSilent) return;

        BeginSilence();
    }

    /// <summary>
    /// Public entry point for triggering silence from external systems
    /// (e.g. CascadeSystem calls this once after a full cascade completes).
    /// </summary>
    public void TriggerSilence()
    {
        if (IsSilent) return;
        BeginSilence();
    }

    /// <summary>Start the Visual Silence period.</summary>
    void BeginSilence()
    {
        IsSilent        = true;
        _silenceTimer   = 0f;
        _audioFadeTimer = 0f;
        _fadingOut      = true;
        _fadingIn       = false;

        // Find or create CanvasGroup on the ScoreManager's canvas
        EnsureHudGroup();

        // Pause the spawner during silence
        if (ProductSpawner.Instance != null)
            ProductSpawner.Instance.isPaused = true;

        Debug.Log("[VisualSilence] Silence STARTED — 1.5s, fading UI + audio.");
    }

    /// <summary>
    /// Finds the ScoreCanvas and adds/gets a CanvasGroup for fading.
    /// Cached after first call.
    /// </summary>
    void EnsureHudGroup()
    {
        if (_hudGroup != null) return;

        GameObject scoreCanvas = GameObject.Find("ScoreCanvas");
        if (scoreCanvas != null)
        {
            _hudGroup = scoreCanvas.GetComponent<CanvasGroup>();
            if (_hudGroup == null)
                _hudGroup = scoreCanvas.AddComponent<CanvasGroup>();
        }
        else
        {
            Debug.LogWarning("[VisualSilence] ScoreCanvas not found — UI won't fade.");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        // Restore audio if destroyed during silence
        AudioListener.volume = 1f;
        if (Instance == this)
            Instance = null;
    }
}
