using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that automatically drops products off the shelf at timed intervals.
/// Finds all "Product"-tagged GameObjects on Start, then picks random ones to fall.
/// Difficulty ramps: spawnInterval decreases every 10 seconds.
/// No coroutines — pure Update-driven timer (per project rules).
/// </summary>
public sealed class ProductSpawner : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static ProductSpawner Instance { get; private set; }

    // ── Config ──────────────────────────────────────────────────────
    [Tooltip("Seconds between automatic product drops.")]
    public float spawnInterval = 2.0f;

    const float k_RampEvery      = 10f;   // reduce interval every N seconds
    const float k_RampAmount     = 0.15f; // seconds removed per ramp step
    const float k_MinInterval    = 0.5f;  // fastest possible spawn rate

    // ── Public state ────────────────────────────────────────────────
    [Tooltip("Set true to pause automatic spawning (e.g., during menus).")]
    public bool isPaused;

    // ── Internal ────────────────────────────────────────────────────
    readonly List<ProductFall> _pendingProducts = new List<ProductFall>();
    float _spawnTimer;
    float _rampTimer;
    float _defaultInterval;  // saved from inspector at Start

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[ProductSpawner] Duplicate destroyed on '{gameObject.name}'.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        _defaultInterval = spawnInterval;

        // Gather every product on the shelf that hasn't fallen yet
        GameObject[] tagged = GameObject.FindGameObjectsWithTag("Product");
        foreach (GameObject go in tagged)
        {
            if (go.TryGetComponent(out ProductFall fall) && !fall.IsFalling && !fall.HasLanded)
                _pendingProducts.Add(fall);
        }

        Debug.Log($"[ProductSpawner] Initialized — {_pendingProducts.Count} products queued, interval {spawnInterval:F2}s.");
    }

    void Update()
    {
        if (isPaused)
            return;

        // ── No products left to drop ────────────────────────────────
        if (_pendingProducts.Count == 0)
            return;

        // ── Difficulty ramp ─────────────────────────────────────────
        _rampTimer += Time.deltaTime;
        if (_rampTimer >= k_RampEvery)
        {
            _rampTimer -= k_RampEvery;
            float oldInterval = spawnInterval;
            spawnInterval = Mathf.Max(k_MinInterval, spawnInterval - k_RampAmount);
            Debug.Log($"[ProductSpawner] Ramp! interval {oldInterval:F2}s → {spawnInterval:F2}s");
        }

        // ── Spawn timer ─────────────────────────────────────────────
        _spawnTimer += Time.deltaTime;
        if (_spawnTimer < spawnInterval)
            return;

        _spawnTimer -= spawnInterval;

        // ── Pick a random pending product and drop it ───────────────
        int index = Random.Range(0, _pendingProducts.Count);
        ProductFall chosen = _pendingProducts[index];
        _pendingProducts.RemoveAt(index);

        chosen.StartFall();
        Debug.Log($"[ProductSpawner] Dropped '{chosen.name}' — {_pendingProducts.Count} remaining, interval {spawnInterval:F2}s");
    }

    // ─────────────────────────────────────────────────────────────────
    // NEXT SHIFT RESET
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-queue all products and unpause. Called by ReceiptScreen on "NEXT SHIFT".
    /// </summary>
    public void ResetForNewShift()
    {
        isPaused      = false;
        _spawnTimer   = 0f;
        _rampTimer    = 0f;
        spawnInterval = _defaultInterval;  // restore inspector value

        // Re-queue all products that are on the shelf (active, not falling, not landed)
        _pendingProducts.Clear();
        GameObject[] tagged = GameObject.FindGameObjectsWithTag("Product");
        foreach (GameObject go in tagged)
        {
            if (go.TryGetComponent(out ProductFall fall) && !fall.IsFalling && !fall.HasLanded)
                _pendingProducts.Add(fall);
        }

        Debug.Log($"[ProductSpawner] Reset for new shift — {_pendingProducts.Count} products re-queued.");
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
