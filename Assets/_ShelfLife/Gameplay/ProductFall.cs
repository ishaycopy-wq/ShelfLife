using System;
using UnityEngine;

/// <summary>
/// Drives a product's fall from shelf to floor using AnimationCurves.
/// Supports both normal drops (StartFall) and launched forward arcs (LaunchForward).
/// Each launch gets a FallStyle (weighted by product shape) that determines arc height,
/// flight duration, in-air rotation, hang time at peak, and post-landing personality.
/// No Rigidbody, no coroutines — pure curve-driven movement in Update.
/// </summary>
public sealed class ProductFall : MonoBehaviour
{
    // ── Fall Style System ─────────────────────────────────────────────
    public enum FallStyle { TiredRoll, DramaticTumble, PanicBounce, DeadWeight, ChaoticSpin }

    readonly struct StyleConfig
    {
        public readonly float arcHeight;
        public readonly float duration;
        public readonly float rotationDeg;
        public readonly float hangTime;
        public readonly PostLandPhase postLand;

        public StyleConfig(float arc, float dur, float rot, float hang, PostLandPhase post)
        {
            arcHeight   = arc;
            duration    = dur;
            rotationDeg = rot;
            hangTime    = hang;
            postLand    = post;
        }
    }

    static StyleConfig GetStyleConfig(FallStyle style)
    {
        switch (style)
        {
            // TiredRoll: low lazy arc, slowest flight, gentle tilt, lazy roll on landing
            case FallStyle.TiredRoll:
                return new StyleConfig(0.3f, 1.8f, 45f, 0.05f, PostLandPhase.SlowRoll);

            // DramaticTumble: tall theatrical arc, full rotation, dramatic peak hang, wobble
            case FallStyle.DramaticTumble:
                return new StyleConfig(0.8f, 1.5f, 360f, 0.1f, PostLandPhase.Wobble);

            // PanicBounce: medium arc, fast and frantic, rapid spin, bounces on floor
            case FallStyle.PanicBounce:
                return new StyleConfig(0.5f, 1.3f, 180f, 0.03f, PostLandPhase.Bounce);

            // DeadWeight: barely any arc, fast heavy drop, no spin, no hang, impact squash
            case FallStyle.DeadWeight:
                return new StyleConfig(0.2f, 1.2f, 0f, 0f, PostLandPhase.Squash);

            // ChaoticSpin: medium-high arc, wild 720 spin, moderate hang, vibrate on landing
            case FallStyle.ChaoticSpin:
                return new StyleConfig(0.6f, 1.6f, 720f, 0.08f, PostLandPhase.Vibrate);

            default:
                return GetStyleConfig(FallStyle.TiredRoll);
        }
    }

    /// <summary>
    /// Pick a random style weighted by product shape.
    /// Spheres favor roll/bounce; boxes favor tumble/thud.
    /// </summary>
    static FallStyle RandomStyleForShape(bool isSphere)
    {
        float roll = UnityEngine.Random.value;
        if (isSphere)
        {
            // Round products: roll or bounce naturally
            if (roll < 0.35f) return FallStyle.TiredRoll;
            if (roll < 0.65f) return FallStyle.PanicBounce;
            if (roll < 0.85f) return FallStyle.ChaoticSpin;
            return FallStyle.DramaticTumble;
        }
        // Boxy products: tumble or thud
        if (roll < 0.30f) return FallStyle.DramaticTumble;
        if (roll < 0.60f) return FallStyle.DeadWeight;
        if (roll < 0.80f) return FallStyle.ChaoticSpin;
        return FallStyle.TiredRoll;
    }

    /// <summary>Apply +/-10% random variation to a float value.</summary>
    static float Vary(float value)
    {
        return value * UnityEngine.Random.Range(0.9f, 1.1f);
    }

    // ── Events ──────────────────────────────────────────────────────
    /// <summary>Fired when this product finishes falling and hits the floor.</summary>
    public static event Action<GameObject> OnProductHitFloor;

    // ── Config ──────────────────────────────────────────────────────
    [Tooltip("Curve for fall progress (0-1 time, 0-1 normalized). Used by both normal falls and launches.")]
    public AnimationCurve fallCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Duration for a normal (non-launched) fall in seconds.")]
    public float fallDuration = 0.8f;

    const float k_FloorY = 0.15f;

    // ── Pulse config ────────────────────────────────────────────────
    const float k_PulsePeriod    = 0.3f;
    const float k_PulseAmplitude = 0.1f;

    // ── Catch shrink config ─────────────────────────────────────────
    const float k_ShrinkDuration = 0.15f;

    // ── Public read-only state ──────────────────────────────────────
    /// <summary>True while the fall animation is playing.</summary>
    public bool IsFalling   => _isFalling;
    /// <summary>
    /// True after the product physically reached the floor. For launched products,
    /// this is set at impact (before post-landing animation). The OnProductHitFloor
    /// event fires later, after post-landing completes — during that window the
    /// product is still catchable (IsPostLanding == true).
    /// </summary>
    public bool HasLanded   { get; private set; }
    /// <summary>True after the player tapped the product mid-fall (caught it).</summary>
    public bool IsCaught    { get; private set; }
    /// <summary>True when another product was caught while this one was falling.</summary>
    public bool IsRejected  { get; set; }
    /// <summary>True during post-landing behavior phase — still catchable.</summary>
    public bool IsPostLanding => _postLandPhase != PostLandPhase.None;
    /// <summary>The fall style assigned to this product's current launch.</summary>
    public FallStyle CurrentStyle => _currentStyle;
    /// <summary>Normalized fall progress: 0 = just started, 1 = hit the floor.</summary>
    public float FallProgress => _isFalling || HasLanded || IsCaught
        ? Mathf.Clamp01(_elapsed / _activeDuration)
        : 0f;

    // ── Core state ──────────────────────────────────────────────────
    bool    _isFalling;
    float   _elapsed;
    float   _startY;
    Vector3 _originalScale;
    Vector3 _shelfPosition;     // original shelf position saved at Awake for reset
    float   _pulseTimer;
    bool    _isShrinking;
    float   _shrinkTimer;
    float   _activeDuration;    // effective fall/flight duration for FallProgress

    // ── Launch state ────────────────────────────────────────────────
    bool    _isLaunching;
    Vector3 _launchStart;
    Vector3 _launchTarget;
    float   _rollDirection;     // +1 or -1, derived from launch trajectory

    // ── Style state (set per-launch) ────────────────────────────────
    FallStyle   _currentStyle;
    StyleConfig _styleConfig;
    float       _effectiveDuration;
    float       _effectiveArcHeight;
    float       _effectiveHangTime;
    float       _effectiveRotation;

    // ── Post-landing ────────────────────────────────────────────────
    enum PostLandPhase { None, SlowRoll, Wobble, Bounce, Squash, Vibrate }
    PostLandPhase _postLandPhase = PostLandPhase.None;
    float         _postLandTimer;
    float         _postLandEffDuration;
    Vector3       _postLandStartPos;

    // ── Post-landing config ─────────────────────────────────────────
    const float k_SlowRollDuration  = 0.8f;    // TiredRoll: lazy deceleration
    const float k_SlowRollDistance  = 0.25f;
    const float k_WobbleDuration    = 0.5f;    // DramaticTumble: decaying oscillation
    const float k_WobbleAngle       = 15f;
    const float k_WobbleFrequency   = 12f;
    const float k_BounceDuration    = 0.6f;    // PanicBounce: diminishing bounces
    const float k_BounceBaseHeight  = 0.15f;
    const int   k_BounceCount       = 3;
    const float k_SquashDuration    = 0.15f;   // DeadWeight: impact compression
    const float k_SquashScaleY      = 0.8f;
    const float k_SquashScaleXZ     = 1.2f;
    const float k_VibrateDuration   = 0.3f;    // ChaoticSpin: position jitter
    const float k_VibrateAmplitude  = 0.02f;
    const float k_VibrateFrequency  = 30f;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        _originalScale = transform.localScale;
        _shelfPosition = transform.position;
    }

    // ── StartFall (normal shelf drop, no style) ──────────────────────

    /// <summary>
    /// Begin a normal fall from current Y position down to floor.
    /// Safe to call multiple times — restarts the fall.
    /// </summary>
    public void StartFall()
    {
        _startY         = transform.position.y;
        _elapsed        = 0f;
        _activeDuration = fallDuration;
        _isFalling      = true;
        _isLaunching    = false;
        HasLanded       = false;
        IsCaught        = false;
        IsRejected      = false;
        _isShrinking    = false;
        _pulseTimer     = 0f;
        _postLandPhase  = PostLandPhase.None;
        transform.localScale = _originalScale;
    }

    // ── LaunchForward (chaos arc with style personality) ──────────────

    /// <summary>
    /// Launch this product in a forward arc from shelf to target floor position.
    /// Assigns a FallStyle (weighted by product shape) that determines arc, rotation,
    /// hang time, and post-landing behavior. All timing gets +/-10% variation.
    /// </summary>
    public void LaunchForward(Vector3 targetFloorPos)
    {
        _launchStart   = transform.position;
        _launchTarget  = new Vector3(targetFloorPos.x, k_FloorY, targetFloorPos.z);
        _rollDirection = Mathf.Sign(targetFloorPos.x - transform.position.x);

        // Assign style based on product shape, with +/-10% variation on all values
        bool isSphere          = TryGetComponent<SphereCollider>(out _);
        _currentStyle          = RandomStyleForShape(isSphere);
        _styleConfig           = GetStyleConfig(_currentStyle);
        _effectiveDuration     = Vary(_styleConfig.duration);
        _effectiveArcHeight    = Vary(_styleConfig.arcHeight);
        _effectiveHangTime     = Vary(_styleConfig.hangTime);
        _effectiveRotation     = Vary(_styleConfig.rotationDeg);
        _activeDuration        = _effectiveDuration + _effectiveHangTime;

        _elapsed       = 0f;
        _isLaunching   = true;
        _isFalling     = true;
        HasLanded      = false;
        IsCaught       = false;
        IsRejected     = false;
        _isShrinking   = false;
        _pulseTimer    = 0f;
        _postLandPhase = PostLandPhase.None;
        transform.localScale = _originalScale;
        transform.rotation   = Quaternion.identity;

        Debug.Log($"[ProductFall] '{name}' LAUNCHED as {_currentStyle} — " +
                  $"arc {_effectiveArcHeight:F2}, dur {_effectiveDuration:F2}s, " +
                  $"hang {_effectiveHangTime:F3}s, rot {_effectiveRotation:F0}deg");
    }

    // ── CatchProduct ─────────────────────────────────────────────────

    /// <summary>
    /// Call when the player taps this product mid-fall or during post-landing.
    /// Starts the scale-to-zero shrink animation and marks it as caught.
    /// </summary>
    public void CatchProduct()
    {
        if (IsCaught) return;
        if (!_isFalling && _postLandPhase == PostLandPhase.None) return;

        IsCaught       = true;
        _isFalling     = false;
        _isLaunching   = false;
        _postLandPhase = PostLandPhase.None;
        _isShrinking   = true;
        _shrinkTimer   = 0f;

        transform.localScale = _originalScale;
        transform.rotation   = Quaternion.identity;

        Debug.Log($"[ProductFall] '{name}' CAUGHT at progress {FallProgress:F2}");
    }

    // ── ResetToShelf ──────────────────────────────────────────────────

    /// <summary>
    /// Reset product to its original shelf state. Called during "NEXT SHIFT" reset.
    /// Restores position, scale, rotation, and all state flags.
    /// </summary>
    public void ResetToShelf()
    {
        _isFalling     = false;
        _isLaunching   = false;
        HasLanded      = false;
        IsCaught       = false;
        IsRejected     = false;
        _isShrinking   = false;
        _postLandPhase = PostLandPhase.None;
        _elapsed       = 0f;
        _pulseTimer    = 0f;

        transform.position   = _shelfPosition;
        transform.localScale = _originalScale;
        transform.rotation   = Quaternion.identity;
    }

    // ── Update ───────────────────────────────────────────────────────

    void Update()
    {
        // ── Catch shrink animation ───────────────────────────────────
        if (_isShrinking)
        {
            _shrinkTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_shrinkTimer / k_ShrinkDuration);
            transform.localScale = _originalScale * (1f - t);
            if (t >= 1f)
            {
                _isShrinking = false;
                gameObject.SetActive(false);
            }
            return;
        }

        // ── Style-driven launch arc ──────────────────────────────────
        if (_isLaunching && _isFalling && !IsCaught)
        {
            UpdateLaunchArc();
            return;
        }

        // ── Post-landing behavior ────────────────────────────────────
        if (_postLandPhase != PostLandPhase.None)
        {
            UpdatePostLanding();
            return;
        }

        // ── Normal fall (non-launched, no style) ─────────────────────
        if (!_isFalling || IsCaught) return;

        _elapsed += Time.deltaTime;
        ApplyPulse();

        float t2         = Mathf.Clamp01(_elapsed / fallDuration);
        float curveValue = fallCurve.Evaluate(t2);
        float fallY      = Mathf.LerpUnclamped(_startY, k_FloorY, curveValue);

        Vector3 pos = transform.position;
        pos.y = fallY;
        transform.position = pos;

        if (t2 >= 1f)
        {
            _isFalling = false;
            HasLanded  = true;
            transform.localScale = _originalScale;
            OnProductHitFloor?.Invoke(gameObject);
        }
    }

    // ── Launch Arc (style-driven) ────────────────────────────────────

    void UpdateLaunchArc()
    {
        _elapsed += Time.deltaTime;

        float totalFlight = _effectiveDuration + _effectiveHangTime;

        // Remap elapsed into arc-t, inserting a freeze at the peak for hang time
        float arcT   = RemapWithHang(_elapsed, _effectiveDuration, _effectiveHangTime);
        float curved = fallCurve.Evaluate(arcT);

        ApplyPulse();

        // XZ: lerp from start to target
        float newX = Mathf.Lerp(_launchStart.x, _launchTarget.x, curved);
        float newZ = Mathf.Lerp(_launchStart.z, _launchTarget.z, curved);

        // Y: linear descent + sin arc for parabolic height
        float baseY     = Mathf.Lerp(_launchStart.y, k_FloorY, curved);
        float arcOffset = _effectiveArcHeight * Mathf.Sin(curved * Mathf.PI);
        float newY      = baseY + arcOffset;

        transform.position = new Vector3(newX, newY, newZ);

        // In-flight rotation (style-specific Z-axis spin)
        if (_effectiveRotation > 0f)
        {
            float rotProgress = Mathf.Clamp01(_elapsed / totalFlight);
            float rotAngle    = _effectiveRotation * rotProgress * _rollDirection;
            transform.rotation = Quaternion.Euler(0f, 0f, rotAngle);
        }

        // Landing check
        if (_elapsed >= totalFlight)
        {
            _isFalling   = false;
            _isLaunching = false;
            HasLanded    = true;
            transform.localScale = _originalScale;
            transform.position   = _launchTarget;
            transform.rotation   = Quaternion.identity;

            EnterPostLanding();
        }
    }

    /// <summary>
    /// Remap elapsed time into arc progress (0-1), inserting a hang-time freeze near peak.
    /// Pre-peak: linear. During hang: frozen at peakRatio. Post-hang: resume offset.
    /// </summary>
    static float RemapWithHang(float elapsed, float baseDuration, float hangTime)
    {
        if (hangTime <= 0f)
            return Mathf.Clamp01(elapsed / baseDuration);

        const float peakRatio = 0.48f;              // freeze just before visual peak
        float peakTime = baseDuration * peakRatio;

        if (elapsed <= peakTime)
            return elapsed / baseDuration;           // pre-peak: normal progression

        if (elapsed <= peakTime + hangTime)
            return peakRatio;                        // during hang: frozen at peak

        return Mathf.Clamp01((elapsed - hangTime) / baseDuration);  // post-hang: resume
    }

    // ── Pulse ────────────────────────────────────────────────────────

    void ApplyPulse()
    {
        _pulseTimer += Time.deltaTime;
        float pulseCycle = (_pulseTimer % k_PulsePeriod) / k_PulsePeriod;
        float pulseScale = 1f + k_PulseAmplitude * Mathf.Sin(pulseCycle * Mathf.PI * 2f);
        transform.localScale = _originalScale * pulseScale;
    }

    // ── Post-Landing System ──────────────────────────────────────────

    void EnterPostLanding()
    {
        _postLandTimer    = 0f;
        _postLandStartPos = transform.position;

        _postLandPhase = _styleConfig.postLand;

        // Compute effective duration with +/-10% variation (applied once at start)
        switch (_postLandPhase)
        {
            case PostLandPhase.SlowRoll: _postLandEffDuration = Vary(k_SlowRollDuration); break;
            case PostLandPhase.Wobble:   _postLandEffDuration = Vary(k_WobbleDuration);   break;
            case PostLandPhase.Bounce:   _postLandEffDuration = Vary(k_BounceDuration);   break;
            case PostLandPhase.Squash:   _postLandEffDuration = Vary(k_SquashDuration);   break;
            case PostLandPhase.Vibrate:  _postLandEffDuration = Vary(k_VibrateDuration);  break;
            default:                     _postLandEffDuration = 0.3f;                     break;
        }

        Debug.Log($"[ProductFall] '{name}' landing as {_currentStyle} -> {_postLandPhase} ({_postLandEffDuration:F2}s)");
    }

    void UpdatePostLanding()
    {
        _postLandTimer += Time.deltaTime;

        switch (_postLandPhase)
        {
            case PostLandPhase.SlowRoll: UpdateSlowRoll(); break;
            case PostLandPhase.Wobble:   UpdateWobble();   break;
            case PostLandPhase.Bounce:   UpdateBounce();   break;
            case PostLandPhase.Squash:   UpdateSquash();   break;
            case PostLandPhase.Vibrate:  UpdateVibrate();  break;
        }
    }

    // ── SlowRoll (TiredRoll): lazy roll in launch direction ──────────

    void UpdateSlowRoll()
    {
        float t     = Mathf.Clamp01(_postLandTimer / _postLandEffDuration);
        float eased = 1f - (1f - t) * (1f - t);    // ease-out quadratic
        float rollX = _postLandStartPos.x + k_SlowRollDistance * eased * _rollDirection;
        transform.position = new Vector3(rollX, _postLandStartPos.y, _postLandStartPos.z);

        if (t >= 1f) FinishPostLanding();
    }

    // ── Wobble (DramaticTumble): decaying Z-oscillation in place ─────

    void UpdateWobble()
    {
        float t     = Mathf.Clamp01(_postLandTimer / _postLandEffDuration);
        float decay = 1f - t;                       // linear decay envelope
        float angle = k_WobbleAngle * decay * Mathf.Sin(_postLandTimer * k_WobbleFrequency);
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (t >= 1f) FinishPostLanding();
    }

    // ── Bounce (PanicBounce): diminishing height bounces ─────────────

    void UpdateBounce()
    {
        float t = Mathf.Clamp01(_postLandTimer / _postLandEffDuration);

        // Divide timeline into k_BounceCount bounces
        float bounceProgress = t * k_BounceCount;
        int   currentBounce  = Mathf.Min((int)bounceProgress, k_BounceCount - 1);
        float withinBounce   = bounceProgress - currentBounce;

        // Each successive bounce is half the previous height
        float heightMul = 1f / (1f + currentBounce);   // 1.0, 0.5, 0.33
        float bounceY   = k_BounceBaseHeight * heightMul * Mathf.Sin(withinBounce * Mathf.PI);

        Vector3 pos = _postLandStartPos;
        pos.y += bounceY;
        transform.position = pos;

        if (t >= 1f) FinishPostLanding();
    }

    // ── Squash (DeadWeight): impact compression then restore ─────────

    void UpdateSquash()
    {
        float t = Mathf.Clamp01(_postLandTimer / _postLandEffDuration);

        float scaleY;
        float scaleXZ;

        if (t < 0.5f)
        {
            // Compress: scale Y down, XZ out
            float halfT = t * 2f;
            scaleY  = Mathf.Lerp(1f, k_SquashScaleY, halfT);
            scaleXZ = Mathf.Lerp(1f, k_SquashScaleXZ, halfT);
        }
        else
        {
            // Restore: snap back to original
            float halfT = (t - 0.5f) * 2f;
            scaleY  = Mathf.Lerp(k_SquashScaleY, 1f, halfT);
            scaleXZ = Mathf.Lerp(k_SquashScaleXZ, 1f, halfT);
        }

        transform.localScale = new Vector3(
            _originalScale.x * scaleXZ,
            _originalScale.y * scaleY,
            _originalScale.z * scaleXZ);

        if (t >= 1f) FinishPostLanding();
    }

    // ── Vibrate (ChaoticSpin): decaying position jitter ──────────────

    void UpdateVibrate()
    {
        float t         = Mathf.Clamp01(_postLandTimer / _postLandEffDuration);
        float decay     = 1f - t;
        float amplitude = k_VibrateAmplitude * decay;

        float offsetX = amplitude * Mathf.Sin(_postLandTimer * k_VibrateFrequency * Mathf.PI * 2f);
        float offsetZ = amplitude * Mathf.Cos(_postLandTimer * k_VibrateFrequency * Mathf.PI * 2f);

        transform.position = _postLandStartPos + new Vector3(offsetX, 0f, offsetZ);

        if (t >= 1f) FinishPostLanding();
    }

    // ── Finish Post-Landing ──────────────────────────────────────────

    void FinishPostLanding()
    {
        _postLandPhase       = PostLandPhase.None;
        transform.localScale = _originalScale;
        transform.rotation   = Quaternion.identity;
        OnProductHitFloor?.Invoke(gameObject);
        Debug.Log($"[ProductFall] '{name}' post-landing complete ({_currentStyle}).");
    }
}
