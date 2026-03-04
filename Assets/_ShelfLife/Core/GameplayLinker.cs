using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Glue script — wires InputManager tap events to the catch mechanic.
/// Falls are automatic (ProductSpawner). Tapping a falling product = CATCH.
/// On catch: feeds ScoreManager (receipt economy), GradeManager (timing grade),
/// triggers FaceOverlay personality, and marks other falling products as "rejected".
/// Rejected products that hit the floor get 80% FaceOverlay + rejection subtitles.
/// Lives on the same GameObject as InputManager.
/// </summary>
public sealed class GameplayLinker : MonoBehaviour
{
    // ── Events ──────────────────────────────────────────────────────
    /// <summary>Fired when any product is successfully caught mid-fall. CustomerController listens for slow-mo cancel.</summary>
    public static event Action OnProductCaught;

    // ── Rejection config ──────────────────────────────────────────
    const float k_RejectionFaceChance = 0.80f;   // 80% face on rejected miss

    // ── Cached product refs (built once at Start, no FindObjectOfType in Update) ──
    static readonly List<ProductFall> s_AllProducts = new List<ProductFall>();

    void OnEnable()
    {
        InputManager.OnProductTapped += HandleProductTapped;
        ProductFall.OnProductHitFloor += HandleMiss;
    }

    void OnDisable()
    {
        InputManager.OnProductTapped -= HandleProductTapped;
        ProductFall.OnProductHitFloor -= HandleMiss;
    }

    void Start()
    {
        CacheAllProducts();
    }

    /// <summary>Cache all ProductFall refs once. No FindObjectOfType in Update per CLAUDE.md.</summary>
    static void CacheAllProducts()
    {
        s_AllProducts.Clear();
        GameObject[] products = GameObject.FindGameObjectsWithTag("Product");
        foreach (GameObject p in products)
        {
            ProductFall pf = p.GetComponent<ProductFall>();
            if (pf != null)
                s_AllProducts.Add(pf);
        }
        Debug.Log($"[GameplayLinker] Cached {s_AllProducts.Count} products for rejection tracking.");
    }

    static void HandleProductTapped(GameObject product)
    {
        if (!product.TryGetComponent(out ProductFall fall))
            return;

        // ── Already dealt with (caught or fully landed) — ignore ──
        if (fall.IsCaught)
            return;
        if (fall.HasLanded && !fall.IsPostLanding)
            return;

        // ── Not falling or post-landing — ignore, spawner handles drops ──
        if (!fall.IsFalling && !fall.IsPostLanding)
            return;

        // ── Product is mid-fall → CATCH ──
        Vector3 catchPos = fall.transform.position;
        fall.CatchProduct();

        // ── Mark all OTHER currently-falling products as "rejected" ──
        MarkOtherFallingAsRejected(fall);

        // ── Receipt Economy — add price to the bill ──
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.ProcessCatch(product, catchPos);
        }
        else
        {
            Debug.LogError("[GameplayLinker] ScoreManager.Instance is NULL!");
        }

        // ── Grade (console log only, kept for debugging) ──
        if (GradeManager.Instance != null)
        {
            GradeManager.CatchGrade grade = GradeManager.Instance.EvaluateCatch(
                fall.FallProgress, catchPos);
            Debug.Log($"[GameplayLinker] '{product.name}' caught — grade: {grade}");
        }

        // ── Notify listeners (CustomerController slow-mo cancel) ──
        OnProductCaught?.Invoke();

        // ── Personality — maybe show a face on the caught product ──
        if (product.TryGetComponent(out FaceOverlay face))
            face.TryTrigger();
    }

    /// <summary>
    /// Find all products currently falling (excluding the one just caught)
    /// and mark them as "rejected" — they were passed over in favor of the catch.
    /// </summary>
    static void MarkOtherFallingAsRejected(ProductFall caughtProduct)
    {
        int rejectedCount = 0;

        for (int i = 0; i < s_AllProducts.Count; i++)
        {
            ProductFall pf = s_AllProducts[i];

            // Skip the product that was just caught
            if (pf == caughtProduct) continue;

            // Only mark products that are currently mid-fall
            if (!pf.IsFalling) continue;

            // Skip already caught or landed
            if (pf.IsCaught || pf.HasLanded) continue;

            pf.IsRejected = true;
            rejectedCount++;

            Debug.Log($"[GameplayLinker] '{pf.name}' marked as REJECTED.");
        }

        if (rejectedCount > 0)
            Debug.Log($"[GameplayLinker] {rejectedCount} product(s) rejected after catching '{caughtProduct.name}'.");
    }

    static void HandleMiss(GameObject product)
    {
        // Safety: don't trigger face on caught products
        if (!product.TryGetComponent(out ProductFall fall))
            return;
        if (fall.IsCaught)
            return;

        // ── Rejected product hit the floor — special behavior ──
        if (fall.IsRejected)
        {
            // 80% chance FaceOverlay (bypasses normal 40% chance)
            if (product.TryGetComponent(out FaceOverlay rejectedFace))
                rejectedFace.TryTriggerWithChance(k_RejectionFaceChance);

            // Rejection subtitle is handled by SubtitleSystem.HandleMiss
            // which checks IsRejected and calls ShowRejectionLine()
            Debug.Log($"[GameplayLinker] Rejected product '{product.name}' hit the floor.");
            return;
        }

        // ── Normal miss — maybe show a face on the missed product ──
        if (product.TryGetComponent(out FaceOverlay face))
            face.TryTrigger();
    }
}
