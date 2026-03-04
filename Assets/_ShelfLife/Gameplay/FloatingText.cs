using UnityEngine;

/// <summary>
/// Attach to a world-space TextMesh object.
/// Floats upward and fades out over its lifetime, then self-destructs.
/// Uses legacy TextMesh (NOT TextMeshPro) for guaranteed rendering.
/// No coroutines — pure Update-driven (per project rules).
/// </summary>
public sealed class FloatingText : MonoBehaviour
{
    // ── Config ──────────────────────────────────────────────────────
    const float k_Lifetime  = 1.5f;
    const float k_RiseSpeed = 0.6f; // world units per second

    // ── State ───────────────────────────────────────────────────────
    float _elapsed;
    TextMesh _textMesh;
    Color _startColor;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        _textMesh = GetComponent<TextMesh>();
        if (_textMesh != null)
            _startColor = _textMesh.color;
    }

    void Update()
    {
        _elapsed += Time.deltaTime;

        // ── Rise ────────────────────────────────────────────────────
        Vector3 pos = transform.position;
        pos.y += k_RiseSpeed * Time.deltaTime;
        transform.position = pos;

        // ── Fade ────────────────────────────────────────────────────
        float t = Mathf.Clamp01(_elapsed / k_Lifetime);
        if (_textMesh != null)
        {
            Color c = _startColor;
            c.a = 1f - t;        // linear fade to transparent
            _textMesh.color = c;
        }

        // ── Billboard — always face the camera ─────────────────────
        Camera cam = Camera.main;
        if (cam != null)
            transform.forward = cam.transform.forward;

        // ── Self-destruct ───────────────────────────────────────────
        if (_elapsed >= k_Lifetime)
            Destroy(gameObject);
    }
}
