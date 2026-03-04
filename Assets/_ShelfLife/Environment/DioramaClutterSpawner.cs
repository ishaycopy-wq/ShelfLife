using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns environmental clutter props in a ring around a center anchor.
/// Pure transform placement — no Rigidbody, no physics.
/// Safe to call from both editor (ContextMenu) and runtime.
/// Avoids spawning inside NavMeshObstacle bounds.
/// </summary>
public class DioramaClutterSpawner : MonoBehaviour
{
    [Header("Anchor")]
    [Tooltip("Center point for the clutter ring (e.g. island center).")]
    [SerializeField] Transform _anchor;

    [Header("Prefabs")]
    [Tooltip("List of prefabs to scatter. Populate with prop prefabs.")]
    [SerializeField] List<GameObject> _prefabs = new List<GameObject>();

    [Header("Spawn Config")]
    [SerializeField] int   _spawnCount = 12;
    [SerializeField] float _radiusMin  = 3f;
    [SerializeField] float _radiusMax  = 5f;

    [Header("Variation")]
    [Tooltip("Random scale multiplier range (0.95–1.05 for subtle variety).")]
    [SerializeField] Vector2 _scaleRange = new Vector2(0.95f, 1.05f);

    [Header("Avoidance")]
    [Tooltip("NavMeshObstacles to avoid spawning inside.")]
    [SerializeField] List<NavMeshObstacle> _avoidObstacles = new List<NavMeshObstacle>();

    // ── Internal tracking ──────────────────────────────────────────
    [HideInInspector, SerializeField] List<GameObject> _spawned = new List<GameObject>();

    // ────────────────────────────────────────────────────────────────
    // PUBLIC API
    // ────────────────────────────────────────────────────────────────

    /// <summary>Spawn clutter ring. Safe to call from editor or runtime.</summary>
    [ContextMenu("Spawn Clutter")]
    public void SpawnClutter()
    {
        ClearClutter();

        if (_anchor == null)
        {
            Debug.LogWarning("[DioramaClutterSpawner] No anchor assigned.");
            return;
        }
        if (_prefabs.Count == 0)
        {
            Debug.LogWarning("[DioramaClutterSpawner] No prefabs assigned — nothing to spawn.");
            return;
        }

        int placed   = 0;
        int attempts = 0;
        int maxAttempts = _spawnCount * 10; // prevent infinite loop

        while (placed < _spawnCount && attempts < maxAttempts)
        {
            attempts++;

            // Random point in ring
            float angle  = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float radius = Random.Range(_radiusMin, _radiusMax);
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Vector3 worldPos = _anchor.position + offset;
            worldPos.y = 0f; // ground level

            // Avoid NavMeshObstacle bounds
            if (IsInsideAnyObstacle(worldPos)) continue;

            // Pick random prefab
            GameObject prefab = _prefabs[Random.Range(0, _prefabs.Count)];
            if (prefab == null) continue;

            // Instantiate (editor-safe)
            GameObject instance;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            else
                instance = Instantiate(prefab);
#else
            instance = Instantiate(prefab);
#endif

            instance.transform.position = worldPos;
            instance.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            float scale = Random.Range(_scaleRange.x, _scaleRange.y);
            instance.transform.localScale = Vector3.one * scale;

            instance.transform.SetParent(transform);
            _spawned.Add(instance);
            placed++;
        }

        Debug.Log($"[DioramaClutterSpawner] Placed {placed}/{_spawnCount} clutter objects ({attempts} attempts).");
    }

    /// <summary>Destroy all previously spawned clutter.</summary>
    [ContextMenu("Clear Clutter")]
    public void ClearClutter()
    {
        foreach (GameObject go in _spawned)
        {
            if (go == null) continue;

            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }
        _spawned.Clear();
    }

    // ────────────────────────────────────────────────────────────────
    // INTERNAL
    // ────────────────────────────────────────────────────────────────

    bool IsInsideAnyObstacle(Vector3 worldPos)
    {
        foreach (NavMeshObstacle obs in _avoidObstacles)
        {
            if (obs == null) continue;

            // Compute world-space AABB from obstacle center + size + transform
            Vector3 center   = obs.transform.TransformPoint(obs.center);
            Vector3 halfSize = Vector3.Scale(obs.size * 0.5f, obs.transform.lossyScale);
            Bounds bounds    = new Bounds(center, halfSize * 2f);

            // Expand slightly for agent radius safety margin
            bounds.Expand(0.5f);

            if (bounds.Contains(worldPos))
                return true;
        }
        return false;
    }
}
