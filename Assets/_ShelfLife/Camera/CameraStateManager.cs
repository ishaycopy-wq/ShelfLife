using System;
using UnityEngine;

/// <summary>
/// Cinematic camera state manager — 3 camera presets driven by StealthBehavior
/// state changes. WideShot (baseline), FollowShot (pursuit), EngagementShot
/// (ambush/clumsy). Update-driven, AnimationCurve EaseInOut transitions.
/// No Cinemachine — pure Camera.main transform lerping.
/// Diorama perspective: fixed rotation Euler(47, 36, 0) at all times.
/// Tracks both X and Z axes for 3D mini-mart framing. EngagementShot tracks customer.
/// </summary>
public sealed class CameraStateManager : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static CameraStateManager Instance { get; private set; }

    // ── Camera States ───────────────────────────────────────────────
    public enum CameraState { WideShot, FollowShot, EngagementShot }
    CameraState _state = CameraState.WideShot;
    public CameraState CurrentState => _state;

    // ── Config: Transition Times (slow, gentle drifts) ──────────────
    const float k_WideTransitionTime       = 2.5f;  // pull-back to wide (most relaxed)
    const float k_FollowTransitionTime     = 2.0f;  // lean-in to follow
    const float k_EngagementTransitionTime = 1.5f;  // approach to engagement

    // ── Config: FOV ─────────────────────────────────────────────────
    const float k_WideFOV       = 27f;
    const float k_FollowFOV    = 23f;
    const float k_EngagementFOV = 22f;   // medium shot — must see shelf + both characters

    // ── Config: Shared diorama angle ────────────────────────────────
    // Diorama Cinematic Angle v3 – Controlled Entrance View
    // All states share the SAME rotation: tilted down 47° AND rotated 36° on Y.
    // Only position and FOV change between states.
    static readonly Quaternion k_DioramaRot = Quaternion.Euler(47f, 36f, 0f);

    // ── Config: WideShot position ───────────────────────────────────
    // SYNC: ShelfSceneSetup must match these values for initial camera setup
    static readonly Vector3 k_WidePos = new Vector3(-4.8f, 6.9f, -6.2f);

    // ── Config: FollowShot ──────────────────────────────────────────
    const float k_FollowY       = 6.9f;           // same height as WideShot
    const float k_FollowXOffset = -4.8f;           // lateral offset for diorama angle
    const float k_FollowZOffset = -6.2f;           // Z distance behind midpoint

    // ── Config: EngagementShot (medium shot — tracks customer) ──────
    const float k_EngagementY       = 5.8f;       // lower than Follow for tighter framing (~84%)
    const float k_EngagementXOffset = -4.0f;      // proportional to Follow (~83%)
    const float k_EngagementZOffset = -5.2f;      // proportional to Follow (~84%)

    // ── Config: SmoothDamp tracking ─────────────────────────────────
    const float k_TrackingSmoothTime = 0.8f;  // slow, cinematic tracking

    // ── Config: Slow-mo FOV dip (additive overlay) ──────────────────
    const float k_FovDipAmount       = 3f;    // FOV reduction in degrees
    const float k_FovDipDownDuration = 1f;    // real-time seconds to dip down
    const float k_FovDipUpDuration   = 0.5f;  // real-time seconds to return

    // ── Movement curve ──────────────────────────────────────────────
    static readonly AnimationCurve s_TransitionCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ── Transition state ────────────────────────────────────────────
    bool       _transitioning;
    float      _transitionTimer;
    float      _transitionDuration;
    Vector3    _fromPos;
    Quaternion _fromRot;
    float      _fromFOV;
    Vector3    _toPos;
    Quaternion _toRot;
    float      _toFOV;

    // ── SmoothDamp velocity refs ────────────────────────────────────
    Vector3 _followVelocity;
    Vector3 _engagementVelocity;

    // ── Cached references ───────────────────────────────────────────
    Camera    _cam;
    Transform _playerTransform;
    Transform _customerTransform;

    // ── FOV dip state (additive overlay during slow-mo) ─────────────
    enum FovDipPhase { Inactive, DippingDown, DippingUp }
    FovDipPhase _fovDipPhase = FovDipPhase.Inactive;
    float       _fovDipTimer;
    float       _fovDipOffset;    // current additive FOV offset (negative)

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[CameraStateManager] Duplicate destroyed on '{gameObject.name}'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        _cam = Camera.main;
        if (_cam == null)
            Debug.LogError("[CameraStateManager] Camera.main is null — camera system disabled!");

        // Cache character transforms via singletons where possible
        if (StealthBehavior.Instance != null)
            _playerTransform = StealthBehavior.Instance.transform;
        else
            Debug.LogWarning("[CameraStateManager] StealthBehavior.Instance is null — player tracking disabled.");

        // CustomerCapsule has no singleton — find by name (created by ShelfSceneSetup)
        GameObject customer = GameObject.Find("CustomerCapsule");
        if (customer != null)
            _customerTransform = customer.transform;
        else
            Debug.LogWarning("[CameraStateManager] CustomerCapsule not found — follow midpoint will use player only.");

        // Subscribe to stealth state changes
        StealthBehavior.OnStateChanged += HandleStateChanged;

        // Subscribe to first-catch slow-mo for FOV dip
        CustomerController.OnFirstCatchSlowMo += TriggerSlowMoFOV;

        Debug.Log("[CameraStateManager] System ready — WideShot baseline.");
    }

    void Update()
    {
        if (_cam == null) return;

        if (_transitioning)
        {
            UpdateTransition();
        }
        else if (_state == CameraState.FollowShot)
        {
            UpdateFollowTracking();
        }
        else if (_state == CameraState.EngagementShot)
        {
            UpdateEngagementTracking();
        }

        // FOV dip overlay — additive, runs on unscaledDeltaTime (independent of timeScale)
        UpdateFovDip();
    }

    // ── State Change Handler ────────────────────────────────────────

    void HandleStateChanged(StealthBehavior.Phase phase)
    {
        // Map StealthBehavior.Phase → CameraState
        switch (phase)
        {
            case StealthBehavior.Phase.DepressedIdle:
                TransitionTo(CameraState.WideShot);
                break;
            case StealthBehavior.Phase.EagerPursuit:
                TransitionTo(CameraState.FollowShot);
                break;
            case StealthBehavior.Phase.ClumsyInterrupt:
            case StealthBehavior.Phase.AmbushReady:
                TransitionTo(CameraState.EngagementShot);
                break;
        }
    }

    // ── Transition Logic ────────────────────────────────────────────

    void TransitionTo(CameraState newState)
    {
        if (_cam == null) return;
        if (_state == newState && !_transitioning) return;

        // CurrentState reflects the target immediately (consistent with StealthBehavior)
        _state = newState;
        _transitioning = true;
        _transitionTimer = 0f;

        // Reset SmoothDamp velocities on state change
        _followVelocity = Vector3.zero;
        _engagementVelocity = Vector3.zero;

        // Capture current camera state
        _fromPos = _cam.transform.position;
        _fromRot = _cam.transform.rotation;
        _fromFOV = _cam.fieldOfView;

        // Compute target state — all states share k_DioramaRot
        _toRot = k_DioramaRot;

        switch (newState)
        {
            case CameraState.WideShot:
                _toPos = k_WidePos;
                _toFOV = k_WideFOV;
                _transitionDuration = k_WideTransitionTime;
                break;

            case CameraState.FollowShot:
                _toPos = ComputeFollowPosition();
                _toFOV = k_FollowFOV;
                _transitionDuration = k_FollowTransitionTime;
                break;

            case CameraState.EngagementShot:
                _toPos = ComputeEngagementPosition();
                _toFOV = k_EngagementFOV;
                _transitionDuration = k_EngagementTransitionTime;
                break;
        }

        Debug.Log($"[CameraStateManager] Transition → {newState} ({_transitionDuration}s)");
    }

    void UpdateTransition()
    {
        // NOTE: Uses Time.deltaTime intentionally — during slow-mo, camera transitions
        // slow down proportionally for a cinematic effect. FOV dip uses unscaledDeltaTime.
        _transitionTimer += Time.deltaTime;
        float t = _transitionDuration > 0f
            ? Mathf.Clamp01(_transitionTimer / _transitionDuration)
            : 1f;
        float curved = s_TransitionCurve.Evaluate(t);

        _cam.transform.position = Vector3.Lerp(_fromPos, _toPos, curved);
        _cam.transform.rotation = Quaternion.Slerp(_fromRot, _toRot, curved);
        _cam.fieldOfView = Mathf.Lerp(_fromFOV, _toFOV, curved);

        if (t >= 1f)
        {
            _transitioning = false;
        }
    }

    // ── Follow Shot Dynamic Tracking ────────────────────────────────

    Vector3 ComputeFollowPosition()
    {
        float midX = 0f;
        float midZ = 0f;

        if (_playerTransform != null && _customerTransform != null)
        {
            midX = (_playerTransform.position.x + _customerTransform.position.x) / 2f;
            midZ = (_playerTransform.position.z + _customerTransform.position.z) / 2f;
        }
        else if (_playerTransform != null)
        {
            midX = _playerTransform.position.x;
            midZ = _playerTransform.position.z;
        }

        return new Vector3(midX + k_FollowXOffset, k_FollowY, midZ + k_FollowZOffset);
    }

    void UpdateFollowTracking()
    {
        // SmoothDamp XZ toward midpoint — Y stays fixed at k_FollowY
        Vector3 target = ComputeFollowPosition();
        Vector3 smoothed = Vector3.SmoothDamp(
            _cam.transform.position, target, ref _followVelocity, k_TrackingSmoothTime);
        smoothed.y = k_FollowY;
        _cam.transform.position = smoothed;
    }

    // ── Engagement Shot Tracking ────────────────────────────────────

    Vector3 ComputeEngagementPosition()
    {
        // Tracks customer (not player) — camera follows the action target
        if (_customerTransform == null) return _cam.transform.position;

        return new Vector3(
            _customerTransform.position.x + k_EngagementXOffset,
            k_EngagementY,
            _customerTransform.position.z + k_EngagementZOffset
        );
    }

    void UpdateEngagementTracking()
    {
        // SmoothDamp XZ toward customer — Y stays fixed at k_EngagementY
        // Rotation is fixed (k_DioramaRot) — no dynamic LookAt needed
        Vector3 target = ComputeEngagementPosition();
        Vector3 smoothed = Vector3.SmoothDamp(
            _cam.transform.position, target, ref _engagementVelocity, k_TrackingSmoothTime);
        smoothed.y = k_EngagementY;
        _cam.transform.position = smoothed;
    }

    // ── Slow-Mo FOV Dip (additive overlay) ─────────────────────────

    /// <summary>
    /// Begin a cinematic FOV dip: reduce FOV by k_FovDipAmount over k_FovDipDownDuration (real time),
    /// then return over k_FovDipUpDuration (real time). Called once per first-catch slow-mo.
    /// </summary>
    public void TriggerSlowMoFOV()
    {
        _fovDipPhase  = FovDipPhase.DippingDown;
        _fovDipTimer  = 0f;
        _fovDipOffset = 0f;
        Debug.Log("[CameraStateManager] Slow-mo FOV dip started.");
    }

    void UpdateFovDip()
    {
        if (_fovDipPhase == FovDipPhase.Inactive) return;

        // Remove previous frame's offset before computing new one
        _cam.fieldOfView -= _fovDipOffset;

        _fovDipTimer += Time.unscaledDeltaTime;

        if (_fovDipPhase == FovDipPhase.DippingDown)
        {
            float t = Mathf.Clamp01(_fovDipTimer / k_FovDipDownDuration);
            _fovDipOffset = -k_FovDipAmount * t;

            if (t >= 1f)
            {
                _fovDipPhase = FovDipPhase.DippingUp;
                _fovDipTimer = 0f;
            }
        }
        else if (_fovDipPhase == FovDipPhase.DippingUp)
        {
            float t = Mathf.Clamp01(_fovDipTimer / k_FovDipUpDuration);
            _fovDipOffset = -k_FovDipAmount * (1f - t);

            if (t >= 1f)
            {
                _fovDipOffset = 0f;
                _fovDipPhase = FovDipPhase.Inactive;
                Debug.Log("[CameraStateManager] Slow-mo FOV dip complete.");
            }
        }

        // Re-apply additive offset
        _cam.fieldOfView += _fovDipOffset;
    }

    // ── Next Shift Reset ─────────────────────────────────────────────

    /// <summary>
    /// Return camera to WideShot. Called by ReceiptScreen on "NEXT SHIFT".
    /// </summary>
    public void TransitionToWideShot()
    {
        // Reset FOV dip if active
        if (_fovDipPhase != FovDipPhase.Inactive)
        {
            _cam.fieldOfView -= _fovDipOffset;
            _fovDipOffset = 0f;
            _fovDipPhase  = FovDipPhase.Inactive;
        }

        TransitionTo(CameraState.WideShot);
        Debug.Log("[CameraStateManager] Reset to WideShot for new shift.");
    }

    // ── Debug API ──────────────────────────────────────────────────
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void ForceState(CameraState state)
    {
        TransitionTo(state);
        Debug.Log($"[CameraStateManager] DEBUG — forced state: {state}");
    }
#endif

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        StealthBehavior.OnStateChanged -= HandleStateChanged;
        CustomerController.OnFirstCatchSlowMo -= TriggerSlowMoFOV;

        if (Instance == this)
            Instance = null;
    }
}
