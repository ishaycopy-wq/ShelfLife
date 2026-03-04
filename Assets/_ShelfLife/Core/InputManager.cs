using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Singleton input handler. Detects taps (mouse in Editor, touch on mobile)
/// and fires OnProductTapped for catchable products or OnCustomerTapped
/// for the customer capsule. Uses SphereCast for forgiving mobile hit area.
/// </summary>
public sealed class InputManager : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static InputManager Instance { get; private set; }

    // ── Events ──────────────────────────────────────────────────────
    /// <summary>Fired when a tap/click hits a collider with ProductFall.</summary>
    public static event Action<GameObject> OnProductTapped;

    /// <summary>Fired when a tap/click hits the CustomerCapsule.</summary>
    public static event Action OnCustomerTapped;

    // ── Config ──────────────────────────────────────────────────────
    const float k_SphereCastRadius = 0.05f;
    const float k_MaxRayDistance   = 100f;

    // ── Cached refs (NEVER FindObjectOfType in Update) ──────────────
    Camera _mainCam;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[InputManager] Duplicate destroyed on '{gameObject.name}'. Singleton lives on '{Instance.gameObject.name}'.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        CacheCamera();
        Debug.Log($"[InputManager] Awake — singleton set. Camera: {(_mainCam != null ? _mainCam.name : "NULL")}");
    }

    void Start()
    {
        // Second chance — Camera.main may not resolve during Awake
        if (_mainCam == null)
        {
            CacheCamera();
            Debug.Log($"[InputManager] Start — camera retry: {(_mainCam != null ? _mainCam.name : "STILL NULL")}");
        }
    }

    void Update()
    {
        if (!WasTapPressedThisFrame(out Vector2 screenPos))
            return;

        // ── DEBUG 1: Input detected ─────────────────────────────────
        Debug.Log($"[InputManager] TAP DETECTED at screen ({screenPos.x:F0}, {screenPos.y:F0})");

        // ── DEBUG 2: Camera check ───────────────────────────────────
        if (_mainCam == null)
        {
            CacheCamera();
            if (_mainCam == null)
            {
                Debug.LogError("[InputManager] Camera.main is NULL — cannot raycast. Is camera tagged 'MainCamera'?");
                return;
            }
            Debug.Log($"[InputManager] Camera re-acquired: {_mainCam.name}");
        }

        Ray ray = _mainCam.ScreenPointToRay(screenPos);

        // ── DEBUG 3: Draw ray in Scene view ─────────────────────────
        Debug.DrawRay(ray.origin, ray.direction * k_MaxRayDistance, Color.red, 2f);
        Debug.Log($"[InputManager] Ray origin: {ray.origin}, direction: {ray.direction}");

        // ── DEBUG 4: SphereCast result ──────────────────────────────
        if (Physics.SphereCast(ray, k_SphereCastRadius, out RaycastHit hit, k_MaxRayDistance))
        {
            GameObject hitObj = hit.collider.gameObject;
            Debug.Log($"[InputManager] SphereCast HIT: '{hitObj.name}' (tag: {hitObj.tag}, layer: {hitObj.layer}, distance: {hit.distance:F2})");

            // Priority 1: Falling or post-landing product → catch
            if (hitObj.TryGetComponent(out ProductFall fall) && (fall.IsFalling || fall.IsPostLanding))
            {
                int listenerCount = OnProductTapped != null ? OnProductTapped.GetInvocationList().Length : 0;
                Debug.Log($"[InputManager] FIRING OnProductTapped for '{hitObj.name}' — {listenerCount} listener(s)");
                OnProductTapped?.Invoke(hitObj);
            }
            // Priority 2: Customer capsule → trigger pursuit
            else if (hitObj.name == "CustomerCapsule")
            {
                int listenerCount = OnCustomerTapped != null ? OnCustomerTapped.GetInvocationList().Length : 0;
                Debug.Log($"[InputManager] FIRING OnCustomerTapped — {listenerCount} listener(s)");
                OnCustomerTapped?.Invoke();
            }
            else
            {
                Debug.LogWarning($"[InputManager] Hit '{hitObj.name}' but not a valid target — ignoring.");
            }
        }
        else
        {
            Debug.LogWarning("[InputManager] SphereCast hit NOTHING. Ray missed all colliders.");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    void CacheCamera()
    {
        _mainCam = Camera.main;
    }

    /// <summary>
    /// Returns true on the frame a tap/click begins.
    /// Editor: Mouse left-button.  Mobile: Primary touch press.
    /// Runtime check — no preprocessor — so Unity Touch Simulation works in Editor.
    /// </summary>
    static bool WasTapPressedThisFrame(out Vector2 screenPosition)
    {
        screenPosition = default;

        // Touch first — higher priority on mobile, also works with Editor touch simulation
        if (Touchscreen.current != null &&
            Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }

        // Mouse fallback — Editor and desktop player builds
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPosition = Mouse.current.position.ReadValue();
            return true;
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        OnProductTapped = null;
        OnCustomerTapped = null;

        if (Instance == this)
            Instance = null;
    }
}
