using System;
using UnityEngine;

/// <summary>
/// Singleton grading system. Evaluates how early the player caught a falling
/// product and returns a letter grade (S → Miss) based on fall progress.
/// </summary>
public sealed class GradeManager : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static GradeManager Instance { get; private set; }

    // ── Grade Enum ──────────────────────────────────────────────────
    public enum CatchGrade { S, A, B, C, Miss }

    // ── Events ──────────────────────────────────────────────────────
    /// <summary>Fired after a catch is graded. Carries the grade and world position.</summary>
    public event Action<CatchGrade, Vector3> OnGradeAwarded;

    // ── Grade Thresholds ────────────────────────────────────────────
    const float k_ThresholdS = 0.30f;
    const float k_ThresholdA = 0.60f;
    const float k_ThresholdB = 0.85f;
    const float k_ThresholdC = 1.00f;

    // ── Rich-text colors for console logging ────────────────────────
    const string k_ColorS    = "#FFD700"; // gold
    const string k_ColorA    = "#32CD32"; // green
    const string k_ColorB    = "#4A90D9"; // blue
    const string k_ColorC    = "#FF8C00"; // orange
    const string k_ColorMiss = "#FF3333"; // red

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[GradeManager] Duplicate destroyed on '{gameObject.name}'. Singleton lives on '{Instance.gameObject.name}'.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[GradeManager] Singleton initialized.");
    }

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Evaluates a catch based on how far the product has fallen.
    /// 0 = caught instantly on the shelf, 1 = caught at floor level.
    /// </summary>
    /// <param name="fallProgress">Normalized fall progress (0–1+).</param>
    /// <param name="catchPosition">World position where the product was caught.</param>
    /// <returns>The letter grade for the catch timing.</returns>
    public CatchGrade EvaluateCatch(float fallProgress, Vector3 catchPosition)
    {
        CatchGrade grade;
        string color;

        if (fallProgress < k_ThresholdS)
        {
            grade = CatchGrade.S;
            color = k_ColorS;
        }
        else if (fallProgress < k_ThresholdA)
        {
            grade = CatchGrade.A;
            color = k_ColorA;
        }
        else if (fallProgress < k_ThresholdB)
        {
            grade = CatchGrade.B;
            color = k_ColorB;
        }
        else if (fallProgress <= k_ThresholdC)
        {
            grade = CatchGrade.C;
            color = k_ColorC;
        }
        else
        {
            grade = CatchGrade.Miss;
            color = k_ColorMiss;
        }

        Debug.Log($"[GradeManager] <color={color}>Grade: {grade}</color> (fallProgress: {fallProgress:F2})");

        OnGradeAwarded?.Invoke(grade, catchPosition);

        return grade;
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
