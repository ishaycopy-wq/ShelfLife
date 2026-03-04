using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Customer AI — NavMeshAgent state machine. Red Capsule walks to shelf via
/// NavMesh pathfinding (routes around display automatically), idles, reaches,
/// triggers chaos, then walks to checkout.
/// Update-driven, NavMeshAgent movement, no coroutines.
/// </summary>
public sealed class CustomerController : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static CustomerController Instance { get; private set; }

    // ── Events ──────────────────────────────────────────────────────
    /// <summary>Fired per-product during chaos (position of launched product). StealthBehavior/Camera listen.</summary>
    public static event Action<Vector3> OnCustomerEvent;

    /// <summary>Fired when chaos phase begins.</summary>
    public static event Action OnChaosStarted;

    /// <summary>Fired when customer reaches checkout — triggers receipt.</summary>
    public static event Action OnCustomerCheckout;

    /// <summary>Fired when first-catch slow-mo activates. CameraStateManager listens for FOV dip.</summary>
    public static event Action OnFirstCatchSlowMo;

    // ── Customer Phases ─────────────────────────────────────────────
    public enum CustomerPhase { WaitingOffscreen, WalkingToShelf, IdleAtShelf, Reaching, Chaos, WalkingToCheckout, Done }
    CustomerPhase _phase = CustomerPhase.WaitingOffscreen;
    public CustomerPhase CurrentPhase => _phase;

    // ── Config: Waypoints ───────────────────────────────────────────
    static readonly Vector3 k_Entrance = new Vector3(4f, 0.5f, 3f);
    static readonly Vector3 k_IdleSpot = new Vector3(0.5f, 0.5f, -1.2f);   // idle = front of display
    static readonly Vector3 k_Checkout = new Vector3(-3f, 0.5f, -3.5f);

    // ── Config: Timing ──────────────────────────────────────────────
    const float k_StartDelay       = 3f;      // wait before walking in
    const float k_IdleBrowseTime   = 1.5f;    // idle browsing at shelf
    const float k_ReachDuration    = 0.3f;    // stretch Y to 1.05
    const float k_ReachHoldTime    = 0.3f;    // hold at 1.05 before chaos
    const float k_ChaosStagger     = 0.5f;    // time between product launches
    const float k_ChaosMaxTime     = 30f;     // max chaos duration before checkout
    const int   k_ChaosMaxProducts = 5;       // max products before checkout

    // ── Config: Idle bob ────────────────────────────────────────────
    const float k_IdleBobSpeed     = 2f;
    const float k_IdleBobAmount    = 0.05f;

    // ── Config: Hesitation during walk ──────────────────────────────
    const float k_NormalSpeed    = 2.0f;    // standard walk speed
    const float k_HesitateSpeed  = 0.6f;    // slowed during hesitation
    const float k_HesitateDelay  = 2.5f;    // seconds into walk before hesitating
    const float k_HesitateDur    = 1.0f;    // seconds of hesitation

    // ── Config: Reaching ────────────────────────────────────────────
    const float k_ReachScaleY = 1.05f;

    // ── Config: First-catch slow-mo ─────────────────────────────────
    const float k_SlowMoScale           = 0.3f;   // Time.timeScale during slow-mo
    const float k_SlowMoDuration        = 2f;     // real-time seconds before auto-restore
    const float k_SlowMoRestoreDuration = 0.5f;   // real-time seconds to lerp timeScale back to 1

    // ── External pause ──────────────────────────────────────────────
    public bool isPaused;

    // ── NavMeshAgent ────────────────────────────────────────────────
    NavMeshAgent _agent;

    // ── State ───────────────────────────────────────────────────────
    float   _phaseTimer;
    float   _idleBaseY;
    bool    _hesitated;          // track whether hesitation happened this walk

    // ── Chaos state ─────────────────────────────────────────────────
    float   _chaosTimer;
    float   _nextLaunchTimer;
    int     _productsLaunched;
    readonly List<ProductFall> _allProducts = new List<ProductFall>();

    // ── Reaching state ──────────────────────────────────────────────
    float _reachTimer;
    float _reachBaseScaleY;

    // ── First-catch slow-mo state ───────────────────────────────────
    bool  _firstCatchUsed;         // true after slow-mo triggers, reset per customer
    bool  _slowMoActive;           // slow-mo window is open (timeScale lowered)
    float _slowMoTimer;            // real-time countdown (unscaledDeltaTime)
    bool  _slowMoRestoring;        // lerping timeScale back to 1
    float _slowMoRestoreTimer;     // real-time elapsed during restore lerp

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[CustomerController] Duplicate destroyed on '{gameObject.name}'.");
            UnityEngine.Object.Destroy(this);
            return;
        }
        Instance = this;

        // Cache NavMeshAgent — added by ShelfSceneSetup
        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
            Debug.LogWarning("[CustomerController] No NavMeshAgent found — movement will be disabled until baked.");
    }

    void Start()
    {
        CacheAllProducts();

        // Position at entrance
        if (_agent != null && _agent.isOnNavMesh)
            _agent.Warp(k_Entrance);
        else
            transform.position = k_Entrance;

        // Agent starts stopped (waiting phase)
        if (_agent != null)
            _agent.isStopped = true;

        // Start waiting
        _phase = CustomerPhase.WaitingOffscreen;
        _phaseTimer = 0f;

        // Reset first-catch flag for this customer
        _firstCatchUsed = false;

        // Subscribe to catch events for slow-mo early cancel
        GameplayLinker.OnProductCaught += HandleProductCaught;

        Debug.Log("[CustomerController] System ready — customer waiting offscreen.");
    }

    void CacheAllProducts()
    {
        _allProducts.Clear();
        GameObject[] products = GameObject.FindGameObjectsWithTag("Product");
        foreach (GameObject p in products)
        {
            ProductFall fall = p.GetComponent<ProductFall>();
            if (fall != null)
                _allProducts.Add(fall);
        }
        Debug.Log($"[CustomerController] Cached {_allProducts.Count} products for chaos launch.");
    }

    void Update()
    {
        // Slow-mo timer runs on unscaledDeltaTime — must tick regardless of isPaused or timeScale
        UpdateSlowMo();

        if (isPaused) return;

        switch (_phase)
        {
            case CustomerPhase.WaitingOffscreen:
                UpdateWaiting();
                break;
            case CustomerPhase.WalkingToShelf:
                UpdateWalkingToShelf();
                break;
            case CustomerPhase.IdleAtShelf:
                UpdateIdle();
                break;
            case CustomerPhase.Reaching:
                UpdateReaching();
                break;
            case CustomerPhase.Chaos:
                UpdateChaos();
                break;
            case CustomerPhase.WalkingToCheckout:
                UpdateWalkingToCheckout();
                break;
            case CustomerPhase.Done:
                break;
        }
    }

    // ── State: WaitingOffscreen ─────────────────────────────────────

    void UpdateWaiting()
    {
        _phaseTimer += Time.deltaTime;
        if (_phaseTimer >= k_StartDelay)
        {
            EnterWalkToShelf();
        }
    }

    // ── State: WalkingToShelf ───────────────────────────────────────

    void EnterWalkToShelf()
    {
        _phase = CustomerPhase.WalkingToShelf;
        _phaseTimer = 0f;
        _hesitated = false;

        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.speed = k_NormalSpeed;
            _agent.SetDestination(k_IdleSpot);
        }

        Debug.Log("[CustomerController] Walking to shelf...");
    }

    void UpdateWalkingToShelf()
    {
        _phaseTimer += Time.deltaTime;

        // Hesitation: slow down mid-walk, then resume
        if (!_hesitated && _phaseTimer >= k_HesitateDelay)
        {
            if (_agent != null && _agent.isOnNavMesh)
                _agent.speed = k_HesitateSpeed;
            _hesitated = true;
        }
        if (_hesitated && _phaseTimer >= k_HesitateDelay + k_HesitateDur)
        {
            if (_agent != null && _agent.isOnNavMesh)
                _agent.speed = k_NormalSpeed;
        }

        // Check arrival
        if (HasAgentArrived())
        {
            EnterIdleAtShelf();
        }
    }

    // ── State: IdleAtShelf ──────────────────────────────────────────

    void EnterIdleAtShelf()
    {
        _phase = CustomerPhase.IdleAtShelf;
        _phaseTimer = 0f;
        _idleBaseY = transform.position.y;

        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = true;

        Debug.Log("[CustomerController] Idle at shelf — browsing...");
    }

    void UpdateIdle()
    {
        _phaseTimer += Time.deltaTime;

        // Small bob animation
        float bobY = _idleBaseY + Mathf.Sin(_phaseTimer * k_IdleBobSpeed) * k_IdleBobAmount;
        Vector3 pos = transform.position;
        pos.y = bobY;
        transform.position = pos;

        // Auto-transition to Reaching after browse time
        if (_phaseTimer >= k_IdleBrowseTime)
        {
            EnterReaching();
        }
    }

    // ── State: Reaching (visual signal before chaos) ───────────────

    void EnterReaching()
    {
        _phase = CustomerPhase.Reaching;
        _reachTimer = 0f;
        _reachBaseScaleY = transform.localScale.y;
        Debug.Log("[CustomerController] Reaching toward shelf...");
    }

    void UpdateReaching()
    {
        _reachTimer += Time.deltaTime;

        if (_reachTimer <= k_ReachDuration)
        {
            // Stretch Y from base to 1.05 over ReachDuration
            float t = Mathf.Clamp01(_reachTimer / k_ReachDuration);
            float scaleY = Mathf.Lerp(_reachBaseScaleY, k_ReachScaleY, t);
            Vector3 s = transform.localScale;
            s.y = scaleY;
            transform.localScale = s;
        }
        else if (_reachTimer >= k_ReachDuration + k_ReachHoldTime)
        {
            // Hold complete — enter chaos
            // Reset scale
            Vector3 s = transform.localScale;
            s.y = 1f;
            transform.localScale = s;
            EnterChaos();
        }
        // else: holding at 1.05, do nothing
    }

    // ── State: Chaos ────────────────────────────────────────────────

    void EnterChaos()
    {
        _phase = CustomerPhase.Chaos;
        _chaosTimer = 0f;
        _nextLaunchTimer = 0f;  // launch first product immediately
        _productsLaunched = 0;

        // Re-cache products before chaos to pick up any late-spawned items
        CacheAllProducts();

        OnChaosStarted?.Invoke();
        Debug.Log("[CustomerController] CHAOS BEGINS — products launching!");
    }

    void UpdateChaos()
    {
        _chaosTimer += Time.deltaTime;
        _nextLaunchTimer += Time.deltaTime;

        // Launch next product when stagger timer expires
        if (_nextLaunchTimer >= k_ChaosStagger && _productsLaunched < _allProducts.Count)
        {
            LaunchNextProduct();
            _nextLaunchTimer = 0f;

            // LaunchNextProduct may have triggered checkout (no products left)
            if (_phase != CustomerPhase.Chaos) return;
        }

        // Check exit conditions: max products launched or max time
        if (_productsLaunched >= k_ChaosMaxProducts || _chaosTimer >= k_ChaosMaxTime)
        {
            EnterWalkToCheckout();
        }
    }

    void LaunchNextProduct()
    {
        // Find next unlaunched product
        ProductFall target = null;
        for (int i = 0; i < _allProducts.Count; i++)
        {
            ProductFall pf = _allProducts[i];
            if (pf == null) continue;
            if (pf.IsFalling || pf.HasLanded || pf.IsCaught) continue;
            if (!pf.gameObject.activeInHierarchy) continue;

            target = pf;
            break;
        }

        if (target == null)
        {
            Debug.Log("[CustomerController] No more products to launch — heading to checkout.");
            EnterWalkToCheckout();
            return;
        }

        // Compute landing position: forward of shelf, randomized
        Vector3 shelfPos = target.transform.position;
        float landX = Mathf.Clamp(shelfPos.x + UnityEngine.Random.Range(-0.5f, 0.5f), -2f, 2f);
        float landZ = UnityEngine.Random.Range(-1.5f, -3.5f);
        Vector3 landingPos = new Vector3(landX, 0.15f, landZ);

        target.LaunchForward(landingPos);
        _productsLaunched++;

        // Notify listeners (camera, stealth) with product position
        OnCustomerEvent?.Invoke(target.transform.position);

        Debug.Log($"[CustomerController] Launched '{target.name}' ({_productsLaunched}/{k_ChaosMaxProducts}) → {landingPos}");

        // ── First-catch slow-mo: activate if employee is in AmbushReady and not yet used ──
        if (!_firstCatchUsed && StealthBehavior.Instance != null &&
            StealthBehavior.Instance.CurrentPhase == StealthBehavior.Phase.AmbushReady)
        {
            ActivateSlowMo();
        }
    }

    // ── First-Catch Slow-Mo ────────────────────────────────────────

    void ActivateSlowMo()
    {
        _firstCatchUsed   = true;
        _slowMoActive     = true;
        _slowMoTimer      = 0f;
        _slowMoRestoring  = false;
        _slowMoRestoreTimer = 0f;

        Time.timeScale = k_SlowMoScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        OnFirstCatchSlowMo?.Invoke();
        Debug.Log("[CustomerController] First-catch SLOW-MO activated!");
    }

    void UpdateSlowMo()
    {
        if (_slowMoRestoring)
        {
            _slowMoRestoreTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_slowMoRestoreTimer / k_SlowMoRestoreDuration);
            Time.timeScale = Mathf.Lerp(k_SlowMoScale, 1f, t);
            Time.fixedDeltaTime = 0.02f * Time.timeScale;

            if (t >= 1f)
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
                _slowMoRestoring = false;
                Debug.Log("[CustomerController] Slow-mo restore complete — timeScale = 1.");
            }
            return;
        }

        if (!_slowMoActive) return;

        _slowMoTimer += Time.unscaledDeltaTime;
        if (_slowMoTimer >= k_SlowMoDuration)
        {
            // Timeout — begin smooth restore
            Debug.Log("[CustomerController] Slow-mo timeout (2s) — restoring timeScale.");
            BeginSlowMoRestore();
        }
    }

    void HandleProductCaught()
    {
        if (!_slowMoActive) return;

        // First catch during slow-mo window — end early
        Debug.Log("[CustomerController] Product caught during slow-mo — restoring timeScale.");
        BeginSlowMoRestore();
    }

    void BeginSlowMoRestore()
    {
        _slowMoActive       = false;
        _slowMoRestoring    = true;
        _slowMoRestoreTimer = 0f;
    }

    // ── State: WalkingToCheckout ────────────────────────────────────

    void EnterWalkToCheckout()
    {
        _phase = CustomerPhase.WalkingToCheckout;
        _phaseTimer = 0f;

        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.speed = k_NormalSpeed;
            _agent.SetDestination(k_Checkout);
        }

        Debug.Log("[CustomerController] Walking to checkout...");
    }

    void UpdateWalkingToCheckout()
    {
        if (HasAgentArrived())
        {
            EnterDone();
        }
    }

    // ── State: Done ─────────────────────────────────────────────────

    void EnterDone()
    {
        _phase = CustomerPhase.Done;

        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = true;

        OnCustomerCheckout?.Invoke();
        Debug.Log("[CustomerController] CHECKOUT — customer done!");
    }

    // ─────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the NavMeshAgent has reached its destination.
    /// Defensive: returns false if agent is null or not on NavMesh.
    /// </summary>
    bool HasAgentArrived()
    {
        if (_agent == null || !_agent.isOnNavMesh) return false;
        if (_agent.pathPending) return false;
        return _agent.remainingDistance <= _agent.stoppingDistance;
    }

    // ─────────────────────────────────────────────────────────────────
    // NEXT SHIFT RESET
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reset customer for a new encounter. Called by ReceiptScreen on "NEXT SHIFT".
    /// </summary>
    public void ResetForNewCustomer()
    {
        // Warp to entrance
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
            _agent.Warp(k_Entrance);
        }
        else
        {
            transform.position = k_Entrance;
        }

        transform.localScale = Vector3.one;

        // Reset phase
        _phase      = CustomerPhase.WaitingOffscreen;
        _phaseTimer = 0f;

        // Reset chaos state
        _chaosTimer      = 0f;
        _nextLaunchTimer = 0f;
        _productsLaunched = 0;

        // Reset slow-mo
        _firstCatchUsed  = false;
        _slowMoActive    = false;
        _slowMoRestoring = false;
        if (Time.timeScale != 1f)
        {
            Time.timeScale      = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        // Re-cache products (they've been reset)
        CacheAllProducts();

        // Unpause
        isPaused = false;

        Debug.Log("[CustomerController] Reset for new customer — waiting at entrance.");
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        // Unsubscribe from external events
        GameplayLinker.OnProductCaught -= HandleProductCaught;

        // Safety: restore timeScale if destroyed mid-slow-mo
        if (_slowMoActive || _slowMoRestoring)
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        OnCustomerEvent = null;
        OnChaosStarted = null;
        OnCustomerCheckout = null;
        OnFirstCatchSlowMo = null;

        if (Instance == this)
            Instance = null;
    }
}
