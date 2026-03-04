using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cascade chain reactions — when a product hits the floor, products on
/// the shelf directly above (within 0.5 units on X) have a chance to fall.
/// First level: 60% chance. Chain levels: 40% chance. 0.3s delay each.
/// Pauses spawner during cascade. Triggers VisualSilence ONCE after
/// the entire cascade completes, not per product.
/// No coroutines — pure Update-driven timers.
/// </summary>
public sealed class CascadeSystem : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static CascadeSystem Instance { get; private set; }

    // ── Config ──────────────────────────────────────────────────────
    const float k_CascadeDelay = 0.3f;
    const float k_FirstChance  = 0.60f;   // 60% for first cascade level
    const float k_ChainChance  = 0.40f;   // 40% for subsequent levels
    const float k_XProximity   = 0.5f;    // max X distance to "directly above"

    // ── Public state ────────────────────────────────────────────────
    /// <summary>True while a cascade chain is in progress.</summary>
    public bool IsCascading { get; private set; }

    // ── Cached product data ─────────────────────────────────────────
    struct ProductInfo
    {
        public ProductFall Fall;
        public Vector3     OriginalPos;
        public int         Row; // 0=bottom, 1=middle, 2=top
    }
    readonly List<ProductInfo> _allProducts = new List<ProductInfo>();

    // ── Cascade state ───────────────────────────────────────────────
    struct PendingCascade
    {
        public ProductFall Target;
        public float       Timer;
    }
    readonly List<PendingCascade> _pending = new List<PendingCascade>();
    readonly List<ProductFall>    _activeCascadeFalls = new List<ProductFall>();

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[CascadeSystem] Duplicate destroyed on '{gameObject.name}'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        ProductFall.OnProductHitFloor += HandleFloorHit;
        CacheProducts();
    }

    void OnDisable()
    {
        ProductFall.OnProductHitFloor -= HandleFloorHit;
    }

    // ─────────────────────────────────────────────────────────────────
    // PRODUCT CACHE (built once at Start — no FindObjectOfType in Update)
    // ─────────────────────────────────────────────────────────────────

    void CacheProducts()
    {
        GameObject[] products = GameObject.FindGameObjectsWithTag("Product");
        foreach (GameObject p in products)
        {
            ProductFall fall = p.GetComponent<ProductFall>();
            if (fall == null) continue;

            Vector3 pos = p.transform.position;
            _allProducts.Add(new ProductInfo
            {
                Fall        = fall,
                OriginalPos = pos,
                Row         = GetRowFromY(pos.y)
            });
        }

        Debug.Log($"[CascadeSystem] Cached {_allProducts.Count} products for cascade lookups.");
    }

    /// <summary>
    /// Determine shelf row from Y position.
    /// ShelfSceneSetup heights: { 0.1, 0.6, 1.1 }
    /// Products sit on top, so centers are roughly 0.24–0.29, 0.74–0.79, 1.24–1.29.
    /// </summary>
    static int GetRowFromY(float y)
    {
        if (y < 0.5f) return 0;  // bottom shelf
        if (y < 1.0f) return 1;  // middle shelf
        return 2;                  // top shelf
    }

    // ─────────────────────────────────────────────────────────────────
    // FLOOR HIT HANDLER
    // ─────────────────────────────────────────────────────────────────

    void HandleFloorHit(GameObject product)
    {
        // Skip caught products — they don't crash into the floor
        if (product.TryGetComponent(out ProductFall fall) && fall.IsCaught)
            return;

        // If this was a cascade-triggered product, remove it from active tracking
        _activeCascadeFalls.Remove(fall);

        // Determine cascade chance: first level = 60%, chain = 40%
        float chance = IsCascading ? k_ChainChance : k_FirstChance;

        // Find the original position of this product for row/X lookup
        Vector3 origPos = Vector3.zero;
        bool found = false;
        for (int i = 0; i < _allProducts.Count; i++)
        {
            if (_allProducts[i].Fall == fall)
            {
                origPos = _allProducts[i].OriginalPos;
                found = true;
                break;
            }
        }

        if (!found)
        {
            Debug.LogWarning($"[CascadeSystem] Product '{product.name}' not in cache — skipping cascade.");
            return;
        }

        // Queue cascades from products above
        QueueCascadesAbove(origPos, chance);

        // Start cascade mode if new pending falls were queued
        if (!IsCascading && _pending.Count > 0)
        {
            IsCascading = true;

            if (ProductSpawner.Instance != null)
                ProductSpawner.Instance.isPaused = true;

            Debug.Log("[CascadeSystem] CASCADE STARTED — spawner paused.");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // CASCADE QUEUEING
    // ─────────────────────────────────────────────────────────────────

    void QueueCascadesAbove(Vector3 fallenOrigPos, float chance)
    {
        int fallenRow = GetRowFromY(fallenOrigPos.y);

        // Top shelf: nothing above
        if (fallenRow >= 2) return;

        int targetRow = fallenRow + 1;

        for (int i = 0; i < _allProducts.Count; i++)
        {
            ProductInfo info = _allProducts[i];

            // Must be on the row directly above
            if (info.Row != targetRow) continue;

            // Must be within X proximity
            float xDist = Mathf.Abs(info.OriginalPos.x - fallenOrigPos.x);
            if (xDist > k_XProximity) continue;

            // Must not already be falling, caught, or landed
            if (info.Fall.IsFalling || info.Fall.HasLanded || info.Fall.IsCaught)
                continue;

            // Must not already be in pending queue
            bool alreadyPending = false;
            for (int j = 0; j < _pending.Count; j++)
            {
                if (_pending[j].Target == info.Fall)
                {
                    alreadyPending = true;
                    break;
                }
            }
            if (alreadyPending) continue;

            // Roll the dice
            if (Random.value > chance) continue;

            // Queue with delay
            _pending.Add(new PendingCascade
            {
                Target = info.Fall,
                Timer  = k_CascadeDelay
            });

            Debug.Log($"[CascadeSystem] Queued cascade: '{info.Fall.name}' " +
                      $"(row {info.Row}, chance {chance:P0}, delay {k_CascadeDelay}s)");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // UPDATE — process delays, track completion
    // ─────────────────────────────────────────────────────────────────

    void Update()
    {
        if (!IsCascading) return;

        // ── Tick pending cascade delays ──────────────────────────────
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            PendingCascade p = _pending[i];
            p.Timer -= Time.deltaTime;

            if (p.Timer <= 0f)
            {
                // Start the fall
                p.Target.StartFall();
                _activeCascadeFalls.Add(p.Target);
                _pending.RemoveAt(i);

                Debug.Log($"[CascadeSystem] Cascade fall STARTED: '{p.Target.name}'");
            }
            else
            {
                _pending[i] = p; // write back updated timer
            }
        }

        // ── Check for caught cascade products (won't fire OnProductHitFloor) ──
        for (int i = _activeCascadeFalls.Count - 1; i >= 0; i--)
        {
            if (_activeCascadeFalls[i].IsCaught)
            {
                Debug.Log($"[CascadeSystem] Cascade product '{_activeCascadeFalls[i].name}' was CAUGHT.");
                _activeCascadeFalls.RemoveAt(i);
            }
        }

        // ── Check if cascade is complete ─────────────────────────────
        if (_pending.Count == 0 && _activeCascadeFalls.Count == 0)
        {
            EndCascade();
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // CASCADE END
    // ─────────────────────────────────────────────────────────────────

    void EndCascade()
    {
        IsCascading = false;
        Debug.Log("[CascadeSystem] CASCADE ENDED.");

        // Trigger VisualSilence ONCE for the entire cascade
        // (only if shift hasn't already ended)
        if (ScoreManager.Instance != null && ScoreManager.Instance.FloorMeter < 3)
        {
            if (VisualSilenceController.Instance != null)
                VisualSilenceController.Instance.TriggerSilence();
        }

        // Don't unpause spawner here — VisualSilence will handle it.
        // If VisualSilence doesn't trigger (shift ended), spawner stays paused
        // which is correct since the game is over.
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
