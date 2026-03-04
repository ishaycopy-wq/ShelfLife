using UnityEngine;

/// <summary>
/// Personality face that appears on a product after catch or miss.
/// Child SpriteRenderer — disabled by default, triggers probabilistically.
/// Scale animation: 0 → 1 in 0.15s, hold 1.2s, 1 → 0 in 0.3s.
///
/// Per CLAUDE.md:
///   - Personality triggers: 40% chance, max 1 per 20 seconds
///   - Face animation: 0.15s open, 1.2s hold, 0.3s close
///   - During Visual Silence: 80% trigger chance
/// </summary>
public sealed class FaceOverlay : MonoBehaviour
{
    // ── Config ──────────────────────────────────────────────────────
    const float k_NormalChance   = 0.40f;   // 40% chance normally
    const float k_SilenceChance  = 0.80f;   // 80% during Visual Silence
    const float k_GlobalCooldown = 20f;     // max 1 face per 20 seconds (shared)
    const float k_OpenDuration   = 0.15f;   // scale 0 → 1
    const float k_HoldDuration   = 1.2f;    // hold at full size
    const float k_CloseDuration  = 0.3f;    // scale 1 → 0

    // ── Shared cooldown ─────────────────────────────────────────────
    static float s_LastTriggerTime = -999f;

    // ── Child refs ──────────────────────────────────────────────────
    SpriteRenderer _faceSprite;

    // ── Animation state ─────────────────────────────────────────────
    enum Phase { Idle, Opening, Holding, Closing }
    Phase _phase = Phase.Idle;
    float _phaseTimer;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        // Find the child SpriteRenderer (added by ShelfSceneSetup)
        _faceSprite = GetComponentInChildren<SpriteRenderer>(true);
        if (_faceSprite != null)
        {
            _faceSprite.transform.localScale = Vector3.zero;
            _faceSprite.enabled = false;
        }
    }

    /// <summary>
    /// Call from GameplayLinker on catch or miss.
    /// Rolls the dice and maybe shows the face.
    /// </summary>
    public void TryTrigger()
    {
        if (_faceSprite == null) return;
        if (_phase != Phase.Idle) return; // already animating

        // Global cooldown check
        if (Time.time - s_LastTriggerTime < k_GlobalCooldown) return;

        // Roll the dice — higher chance during Visual Silence
        float chance = k_NormalChance;
        if (VisualSilenceController.Instance != null &&
            VisualSilenceController.Instance.IsSilent)
        {
            chance = k_SilenceChance;
        }

        TriggerInternal(chance);
    }

    /// <summary>
    /// Call from GameplayLinker for rejected products — uses explicit chance (80%).
    /// Still respects global cooldown and animation state.
    /// </summary>
    public void TryTriggerWithChance(float overrideChance)
    {
        if (_faceSprite == null) return;
        if (_phase != Phase.Idle) return;

        if (Time.time - s_LastTriggerTime < k_GlobalCooldown) return;

        TriggerInternal(overrideChance);
    }

    void TriggerInternal(float chance)
    {
        if (Random.value > chance) return;

        // Trigger!
        s_LastTriggerTime = Time.time;
        _faceSprite.enabled = true;
        _faceSprite.transform.localScale = Vector3.zero;
        _phase = Phase.Opening;
        _phaseTimer = 0f;

        Debug.Log($"[FaceOverlay] Face triggered on '{transform.parent?.name ?? name}'");
    }

    void Update()
    {
        if (_phase == Phase.Idle) return;

        _phaseTimer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Opening:
            {
                float t = Mathf.Clamp01(_phaseTimer / k_OpenDuration);
                _faceSprite.transform.localScale = Vector3.one * t;
                if (t >= 1f)
                {
                    _phase = Phase.Holding;
                    _phaseTimer = 0f;
                }
                break;
            }

            case Phase.Holding:
            {
                // Billboard toward camera
                Camera cam = Camera.main;
                if (cam != null)
                    _faceSprite.transform.forward = cam.transform.forward;

                if (_phaseTimer >= k_HoldDuration)
                {
                    _phase = Phase.Closing;
                    _phaseTimer = 0f;
                }
                break;
            }

            case Phase.Closing:
            {
                float t = Mathf.Clamp01(_phaseTimer / k_CloseDuration);
                _faceSprite.transform.localScale = Vector3.one * (1f - t);
                if (t >= 1f)
                {
                    _faceSprite.enabled = false;
                    _faceSprite.transform.localScale = Vector3.zero;
                    _phase = Phase.Idle;
                }
                break;
            }
        }
    }
}
