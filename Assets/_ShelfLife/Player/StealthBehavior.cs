using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// "Depressed Clouseau" personality state machine for the Player capsule.
/// Slouches in existential dread, springs to hyper-eager pursuit on customer events,
/// fails at stealth (fourth-wall camera looks), then succeeds at the ambush.
/// NavMeshAgent pursuit, Update-driven state machine, no coroutines.
/// </summary>
public sealed class StealthBehavior : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static StealthBehavior Instance { get; private set; }

    // ── Events ──────────────────────────────────────────────────────
    /// <summary>Fired when a clumsy interrupt triggers.</summary>
    public static event Action OnClumsyEvent;

    /// <summary>Fired when the fourth-wall camera look starts.</summary>
    public static event Action OnFourthWallLook;

    /// <summary>Fired on every phase transition with the new Phase value.</summary>
    public static event Action<Phase> OnStateChanged;

    // ── Config ──────────────────────────────────────────────────────
    static readonly Vector3 k_HomePos = new Vector3(-3f, 0.5f, -2f);

    const float k_PursuitSpeed        = 3.0f;     // NavMeshAgent speed during pursuit
    const float k_PursuitZOffset      = -1.5f;    // stay 1.5 units behind customer on -Z
    const float k_LookRotateTime      = 0.3f;     // rotate toward camera
    const float k_LookHoldTime        = 1.0f;     // hold camera look
    const float k_ClumsyTotalTime     = k_LookRotateTime + k_LookHoldTime + k_LookRotateTime;
    const float k_AmbushHoldTime      = 3.0f;     // victory hold
    const float k_ProximityThreshold  = 1.5f;     // distance to trigger ambush
    const float k_IdleScaleY          = 0.9f;     // depressed squish
    const float k_AmbushScaleY        = 1.1f;     // victory stretch
    const float k_NormalScaleY        = 1.0f;

    // ── NavMeshAgent ────────────────────────────────────────────────
    NavMeshAgent _agent;

    // ── State ───────────────────────────────────────────────────────
    public enum Phase { DepressedIdle, EagerPursuit, ClumsyInterrupt, AmbushReady }
    Phase _phase = Phase.DepressedIdle;
    Phase _preInterruptPhase;   // state to return to after clumsy
    public Phase CurrentPhase => _phase;
    float _phaseTimer;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[StealthBehavior] Duplicate destroyed on '{gameObject.name}'.");
            UnityEngine.Object.Destroy(this);
            return;
        }
        Instance = this;

        // Cache NavMeshAgent — added by ShelfSceneSetup
        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
            Debug.LogWarning("[StealthBehavior] No NavMeshAgent found — movement will be disabled until baked.");
    }

    void Start()
    {
        // Subscribe to customer events
        InputManager.OnCustomerTapped += HandleCustomerTapped;

        // Start in depressed idle pose
        EnterDepressedIdle();

        Debug.Log("[StealthBehavior] System ready — slouching in existential dread.");
    }

    void Update()
    {
        // ── Debug keys ─────────────────────────────────────────────
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Keyboard.current != null)
        {
            if (Keyboard.current.sKey.wasPressedThisFrame)
            {
                Debug.Log("[StealthBehavior] DEBUG — simulating customer tap.");
                HandleCustomerTapped();
            }
            if (Keyboard.current.cKey.wasPressedThisFrame)
            {
                Debug.Log("[StealthBehavior] DEBUG — triggering clumsy interrupt.");
                EnterClumsyInterrupt();
            }
        }
#endif

        // ── State machine ──────────────────────────────────────────
        switch (_phase)
        {
            case Phase.DepressedIdle:
                return; // waiting for event

            case Phase.EagerPursuit:
                UpdatePursuit();
                break;

            case Phase.ClumsyInterrupt:
                UpdateClumsyInterrupt();
                break;

            case Phase.AmbushReady:
                UpdateAmbush();
                break;
        }
    }

    // ── State Transitions ──────────────────────────────────────────

    void EnterDepressedIdle()
    {
        _phase = Phase.DepressedIdle;
        _phaseTimer = 0f;
        OnStateChanged?.Invoke(Phase.DepressedIdle);

        // Slouch: squish Y scale
        SetScaleY(k_IdleScaleY);

        // Stop agent and warp to home position
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
            _agent.Warp(k_HomePos);
        }
        else
        {
            transform.position = k_HomePos;
        }

        // Reset rotation
        transform.rotation = Quaternion.identity;
    }

    void EnterEagerPursuit()
    {
        _phase = Phase.EagerPursuit;
        _phaseTimer = 0f;
        OnStateChanged?.Invoke(Phase.EagerPursuit);

        // Normal scale during pursuit
        SetScaleY(k_NormalScaleY);

        // Start moving toward customer
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.speed = k_PursuitSpeed;
            UpdatePursuitDestination();
        }

        // Trigger walkie-talkie radio chatter
        if (WalkieTalkieSystem.Instance != null)
            WalkieTalkieSystem.Instance.PlayRandom();

        Debug.Log($"[StealthBehavior] EAGER PURSUIT — chasing customer from {transform.position}");
    }

    // TODO: Sprint 3+ — add gameplay trigger for ClumsyInterrupt
    // (e.g., random chance during EagerPursuit, proximity to shelf obstacle)
    void EnterClumsyInterrupt()
    {
        // Don't re-enter if already clumsy
        if (_phase == Phase.ClumsyInterrupt) return;

        _preInterruptPhase = _phase;
        _phase = Phase.ClumsyInterrupt;
        _phaseTimer = 0f;
        OnStateChanged?.Invoke(Phase.ClumsyInterrupt);

        // Stop agent during clumsy interrupt
        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = true;

        // Camera handles fourth-wall framing via EngagementShot
        // No capsule rotation needed

        OnClumsyEvent?.Invoke();
        OnFourthWallLook?.Invoke();

        Debug.Log("[StealthBehavior] CLUMSY INTERRUPT — looking at camera!");
    }

    void EnterAmbushReady()
    {
        _phase = Phase.AmbushReady;
        _phaseTimer = 0f;
        OnStateChanged?.Invoke(Phase.AmbushReady);

        // Stop agent at ambush position
        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = true;

        // Victory stretch
        SetScaleY(k_AmbushScaleY);

        Debug.Log("[StealthBehavior] AMBUSH READY — victory pose!");
    }

    // ── State Updates ──────────────────────────────────────────────

    void UpdatePursuit()
    {
        _phaseTimer += Time.deltaTime;

        // Update destination every frame to track moving customer
        UpdatePursuitDestination();

        // Check proximity to customer (actual customer, not offset target)
        Vector3 customerPos = GetCustomerPosition();
        float dist = Vector3.Distance(
            new Vector3(transform.position.x, 0f, transform.position.z),
            new Vector3(customerPos.x, 0f, customerPos.z));

        if (dist <= k_ProximityThreshold)
        {
            if (CustomerController.Instance == null)
            {
                Debug.LogError("[StealthBehavior] CustomerController.Instance is NULL at proximity trigger!");
                EnterAmbushReady();
                return;
            }

            var custPhase = CustomerController.Instance.CurrentPhase;
            // Employee arrives — enter ambush if customer is at shelf area
            // Chaos is automatic now, employee presence enables CATCHING
            if (custPhase == CustomerController.CustomerPhase.IdleAtShelf ||
                custPhase == CustomerController.CustomerPhase.Reaching ||
                custPhase == CustomerController.CustomerPhase.Chaos)
            {
                EnterAmbushReady();
            }
            // else: customer still walking — keep lurking
        }
    }

    void UpdatePursuitDestination()
    {
        if (_agent == null || !_agent.isOnNavMesh) return;

        Vector3 customerPos = GetCustomerPosition();
        // Target: behind customer on -Z (floor side)
        Vector3 behindCustomer = new Vector3(
            customerPos.x,
            transform.position.y,
            customerPos.z + k_PursuitZOffset);

        _agent.SetDestination(behindCustomer);
    }

    Vector3 GetCustomerPosition()
    {
        if (CustomerController.Instance != null)
            return CustomerController.Instance.transform.position;

        Debug.LogWarning("[StealthBehavior] CustomerController.Instance is NULL — returning origin as fallback.");
        return Vector3.zero;
    }

    void UpdateClumsyInterrupt()
    {
        _phaseTimer += Time.deltaTime;

        // Timing preserved — camera does the visual framing now
        if (_phaseTimer >= k_ClumsyTotalTime)
        {
            // Done — return to previous state
            if (_preInterruptPhase == Phase.EagerPursuit)
            {
                // Resume pursuit
                _phase = Phase.EagerPursuit;
                _phaseTimer = 0f;
                OnStateChanged?.Invoke(Phase.EagerPursuit);

                if (_agent != null && _agent.isOnNavMesh)
                {
                    _agent.isStopped = false;
                    _agent.speed = k_PursuitSpeed;
                    UpdatePursuitDestination();
                }
            }
            else
            {
                EnterDepressedIdle();
            }

            Debug.Log($"[StealthBehavior] Clumsy interrupt over — returning to {_phase}.");
        }
    }

    void UpdateAmbush()
    {
        _phaseTimer += Time.deltaTime;

        if (_phaseTimer >= k_AmbushHoldTime)
        {
            Debug.Log("[StealthBehavior] Ambush hold complete — returning to idle.");
            EnterDepressedIdle();
        }
    }

    // ── Event Handlers ─────────────────────────────────────────────

    void HandleCustomerTapped()
    {
        if (_phase == Phase.ClumsyInterrupt) return; // don't interrupt clumsy
        if (_phase == Phase.EagerPursuit) return;     // already pursuing
        if (_phase == Phase.AmbushReady) return;       // already there

        EnterEagerPursuit();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    void SetScaleY(float y)
    {
        Vector3 s = transform.localScale;
        s.y = y;
        transform.localScale = s;
    }

    // ─────────────────────────────────────────────────────────────────
    // NEXT SHIFT RESET
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reset employee for a new shift. Called by ReceiptScreen on "NEXT SHIFT".
    /// </summary>
    public void ResetForNewShift()
    {
        EnterDepressedIdle();
        Debug.Log("[StealthBehavior] Reset for new shift — slouching again.");
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        InputManager.OnCustomerTapped -= HandleCustomerTapped;
        OnClumsyEvent = null;
        OnFourthWallLook = null;
        OnStateChanged = null;

        if (Instance == this)
            Instance = null;
    }
}
