using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Configures diorama-appropriate lighting: warm directional (3500K mood),
/// 2–4 spot accent lights above the hero island, and a ReflectionProbe.
/// No PostProcessing changes — lighting only.
/// Callable via ContextMenu in the Inspector.
/// </summary>
public class DioramaLightingSetup : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Center of the hero island. Spot lights orbit above this.")]
    [SerializeField] Transform _islandCenter;

    [Header("Spot Light Config")]
    [Range(2, 4)]
    [SerializeField] int   _spotCount     = 3;
    [SerializeField] float _spotHeight    = 4f;
    [SerializeField] float _spotIntensity = 15f;
    [SerializeField] float _spotAngle     = 60f;
    [SerializeField] float _spotOrbitRadius = 1.5f;

    // ── Internal tracking ──────────────────────────────────────────
    [HideInInspector, SerializeField] List<GameObject> _createdObjects = new List<GameObject>();

    // ────────────────────────────────────────────────────────────────
    // PUBLIC API
    // ────────────────────────────────────────────────────────────────

    /// <summary>Configure all diorama lighting. Idempotent — clears previous lights first.</summary>
    [ContextMenu("Setup Diorama Lighting")]
    public void SetupLighting()
    {
        ClearCreatedLights();

        Vector3 center = _islandCenter != null ? _islandCenter.position : Vector3.zero;

        // 1. Warm directional light (3500K)
        ConfigureDirectionalLight();

        // 2. Spot accent lights above island
        CreateSpotLights(center);

        // 3. Reflection probe near island
        CreateReflectionProbe(center);

        Debug.Log($"[DioramaLightingSetup] Complete — warm directional, {_spotCount} spots, reflection probe.");
    }

    /// <summary>Remove all lights/probes created by this setup.</summary>
    [ContextMenu("Clear Diorama Lights")]
    public void ClearCreatedLights()
    {
        foreach (GameObject go in _createdObjects)
        {
            if (go == null) continue;

            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }
        _createdObjects.Clear();
    }

    // ────────────────────────────────────────────────────────────────
    // INTERNAL
    // ────────────────────────────────────────────────────────────────

    void ConfigureDirectionalLight()
    {
        Light[] allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (Light l in allLights)
        {
            if (l.type != LightType.Directional) continue;

            // 3500K warm tone — RGB approximation
            l.color       = new Color(1f, 0.85f, 0.68f, 1f);
            l.intensity   = 1.3f;
            l.shadows     = LightShadows.Soft;
            l.shadowStrength = 0.7f;

            // Slight bloom-friendly: higher intensity creates natural bloom hotspots
            // on reflective surfaces without touching post-processing
            Debug.Log("[DioramaLightingSetup] Directional light → warm 3500K, intensity 1.3.");
            return;
        }

        Debug.LogWarning("[DioramaLightingSetup] No directional light found in scene.");
    }

    void CreateSpotLights(Vector3 center)
    {
        for (int i = 0; i < _spotCount; i++)
        {
            GameObject spotGo = new GameObject($"DioramaSpot_{i}");
            spotGo.transform.SetParent(transform);

            // Distribute spots evenly in a circle above island
            float angle   = (360f / _spotCount) * i * Mathf.Deg2Rad;
            float offsetX = Mathf.Cos(angle) * _spotOrbitRadius;
            float offsetZ = Mathf.Sin(angle) * _spotOrbitRadius;

            spotGo.transform.position = center + new Vector3(offsetX, _spotHeight, offsetZ);
            spotGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // point straight down

            Light spot    = spotGo.AddComponent<Light>();
            spot.type     = LightType.Spot;
            spot.color    = new Color(1f, 0.93f, 0.82f, 1f); // warm accent
            spot.intensity = _spotIntensity;
            spot.spotAngle = _spotAngle;
            spot.range     = _spotHeight + 2f;
            spot.shadows   = LightShadows.Soft;

            _createdObjects.Add(spotGo);
        }
    }

    void CreateReflectionProbe(Vector3 center)
    {
        GameObject probeGo = new GameObject("DioramaReflectionProbe");
        probeGo.transform.SetParent(transform);
        probeGo.transform.position = center + new Vector3(0f, 1.5f, 0f);

        ReflectionProbe probe = probeGo.AddComponent<ReflectionProbe>();
        probe.size        = new Vector3(8f, 4f, 6f);

        _createdObjects.Add(probeGo);
        Debug.Log("[DioramaLightingSetup] Reflection probe created at island center.");
    }
}
