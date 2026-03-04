using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Receipt Economy — the core scoring system. Every caught product adds its
/// PRICE to the customer's bill. Combo streaks multiply the price.
/// Three floor hits = shift over.
/// Creates its own UI Canvas with receipt total, combo text, and floor meter.
/// </summary>
public sealed class ScoreManager : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static ScoreManager Instance { get; private set; }

    // ── Events ──────────────────────────────────────────────────────
    /// <summary>Fired when a catch adds to the receipt. (earnedPrice, worldPos)</summary>
    public event Action<float, Vector3> OnScoreAdded;
    /// <summary>Fired when 3 products hit the floor — game over.</summary>
    public event Action OnShiftEnd;

    // ── Caught product record ─────────────────────────────────────
    public struct CaughtProduct
    {
        public string ColorKey;      // "Red", "Purple", etc.
        public float  EarnedPrice;   // after combo multiplier
    }

    // ── Public state ────────────────────────────────────────────────
    public float ReceiptTotal { get; private set; }
    public int   ComboCount   { get; private set; }
    public int   FloorMeter   { get; private set; }

    /// <summary>Every product the player caught this shift, in order.</summary>
    public readonly List<CaughtProduct> CaughtProducts = new List<CaughtProduct>();

    // ── Price lookup by product color name ──────────────────────────
    static readonly Dictionary<string, float> s_Prices = new Dictionary<string, float>
    {
        { "Red",        4.90f },
        { "Purple",    89.90f },
        { "White",      2.65f },
        { "Green",     12.50f },
        { "DarkGreen", 12.50f },
        { "Orange",     8.90f },
        { "Yellow",     6.90f },
        { "Brown",      9.90f },
        { "Pink",       3.90f },
        { "DarkBlue",  14.90f },
    };

    // ── Config ──────────────────────────────────────────────────────
    const int   k_MaxFloorHits    = 3;
    const float k_ShakeDuration   = 0.2f;
    const float k_ShakeIntensity  = 0.03f;

    // ── UI refs (created at runtime) ────────────────────────────────
    TextMeshProUGUI _receiptText;
    TextMeshProUGUI _comboText;
    readonly Image[] _floorDots = new Image[k_MaxFloorHits];

    // ── Cached refs ──────────────────────────────────────────────────
    Camera _cam;

    // ── Camera shake state ──────────────────────────────────────────
    Vector3 _shakeOffset;
    float   _shakeTimer;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[ScoreManager] Duplicate destroyed on '{gameObject.name}'.");
            Destroy(this);
            return;
        }

        Instance = this;
        Debug.Log("[ScoreManager] Singleton initialized.");
    }

    void Start()
    {
        _cam = Camera.main;
        CreateUI();
        RefreshUI();
    }

    void OnEnable()
    {
        ProductFall.OnProductHitFloor += HandleMiss;
        CustomerController.OnCustomerCheckout += HandleCheckout;
    }

    void OnDisable()
    {
        ProductFall.OnProductHitFloor -= HandleMiss;
        CustomerController.OnCustomerCheckout -= HandleCheckout;
    }

    // ── Camera shake in LateUpdate (after any camera movement) ──────
    void LateUpdate()
    {
        if (_cam == null) return;

        // Remove previous frame's offset
        _cam.transform.position -= _shakeOffset;
        _shakeOffset = Vector3.zero;

        if (_shakeTimer > 0f)
        {
            _shakeTimer -= Time.deltaTime;
            _shakeOffset = UnityEngine.Random.insideUnitSphere * k_ShakeIntensity;
            _shakeOffset.z = 0f; // keep shake in screen plane
            _cam.transform.position += _shakeOffset;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GameplayLinker when a product is caught mid-fall.
    /// Looks up the product's price, applies combo multiplier, updates receipt.
    /// </summary>
    public void ProcessCatch(GameObject product, Vector3 catchPosition)
    {
        float basePrice = GetPrice(product.name);
        ComboCount++;
        float multiplier = GetComboMultiplier();
        float earned = basePrice * multiplier;
        ReceiptTotal += earned;

        // Track for end-of-shift receipt
        string colorKey = product.name.Replace("Product_", "");
        CaughtProducts.Add(new CaughtProduct { ColorKey = colorKey, EarnedPrice = earned });

        RefreshUI();
        OnScoreAdded?.Invoke(earned, catchPosition);

        Debug.Log($"[ScoreManager] CATCH '{product.name}': " +
                  $"₪{basePrice:F2} x{multiplier:F1} = ₪{earned:F2} | " +
                  $"Total: ₪{ReceiptTotal:F2} | Combo: {ComboCount}");
    }

    /// <summary>
    /// Reset scoring for a new shift. Called by ReceiptScreen on "NEXT SHIFT".
    /// </summary>
    public void ResetForNewShift()
    {
        ReceiptTotal = 0f;
        ComboCount   = 0;
        FloorMeter   = 0;
        CaughtProducts.Clear();
        _shakeTimer  = 0f;
        _shakeOffset = Vector3.zero;
        RefreshUI();
        Debug.Log("[ScoreManager] Reset for new shift — receipt cleared.");
    }

    // ─────────────────────────────────────────────────────────────────
    // MISS HANDLING
    // ─────────────────────────────────────────────────────────────────

    void HandleMiss(GameObject product)
    {
        // Safety: don't count caught products
        if (product.TryGetComponent(out ProductFall fall) && fall.IsCaught)
            return;

        ComboCount = 0;
        FloorMeter++;

        // Screen shake
        _shakeTimer = k_ShakeDuration;

        // Floor stain
        SpawnStain(product);

        RefreshUI();

        Debug.Log($"[ScoreManager] MISS '{product.name}' — " +
                  $"floorMeter: {FloorMeter}/{k_MaxFloorHits}");

        if (FloorMeter >= k_MaxFloorHits)
        {
            Debug.Log("[ScoreManager] SHIFT END — 3 products hit the floor!");

            // Pause spawner
            if (ProductSpawner.Instance != null)
                ProductSpawner.Instance.isPaused = true;

            OnShiftEnd?.Invoke();
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // CHECKOUT HANDLING
    // ─────────────────────────────────────────────────────────────────

    void HandleCheckout()
    {
        Debug.Log("[ScoreManager] Customer checkout — triggering receipt!");

        // Pause spawner
        if (ProductSpawner.Instance != null)
            ProductSpawner.Instance.isPaused = true;

        OnShiftEnd?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────
    // COMBO MULTIPLIER
    // ─────────────────────────────────────────────────────────────────

    float GetComboMultiplier()
    {
        if (ComboCount <= 2) return 1f;
        if (ComboCount <= 4) return 1.5f;
        if (ComboCount <= 7) return 2f;
        if (ComboCount <= 9) return 3f;
        return 5f;
    }

    // ─────────────────────────────────────────────────────────────────
    // PRICE LOOKUP
    // ─────────────────────────────────────────────────────────────────

    static float GetPrice(string productName)
    {
        // "Product_DarkBlue" → "DarkBlue"
        string key = productName.Replace("Product_", "");
        return s_Prices.TryGetValue(key, out float price) ? price : 1.00f;
    }

    // ─────────────────────────────────────────────────────────────────
    // FLOOR STAIN
    // ─────────────────────────────────────────────────────────────────

    void SpawnStain(GameObject product)
    {
        // Darken the product's color for the splat
        Color stainColor = new Color(0.3f, 0.1f, 0.1f);
        MeshRenderer mr = product.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial != null)
        {
            stainColor = mr.sharedMaterial.GetColor("_BaseColor") * 0.35f;
            stainColor.a = 1f;
        }

        GameObject stain = GameObject.CreatePrimitive(PrimitiveType.Quad);
        stain.name = "FloorStain";
        stain.transform.position = new Vector3(
            product.transform.position.x,
            0.02f, // just above floor
            product.transform.position.z);
        stain.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // flat on floor
        stain.transform.localScale = new Vector3(0.4f, 0.4f, 1f);

        // Remove collider so raycasts ignore it
        Collider col = stain.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Apply darkened material
        MeshRenderer stainMr = stain.GetComponent<MeshRenderer>();
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");
        Material mat = new Material(litShader);
        mat.SetColor("_BaseColor", stainColor);
        mat.SetFloat("_Smoothness", 0.8f);
        stainMr.material = mat;
    }

    // ─────────────────────────────────────────────────────────────────
    // UI CREATION
    // ─────────────────────────────────────────────────────────────────

    void CreateUI()
    {
        // ── Canvas ──────────────────────────────────────────────────
        GameObject canvasGo = new GameObject("ScoreCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // ── Receipt Total — top center ──────────────────────────────
        _receiptText = CreateTMPText(canvasGo.transform, "ReceiptText",
            "₪0.00", 48, FontStyles.Bold, Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -40f), new Vector2(400f, 70f));

        // Drop shadow for readability
        _receiptText.enableVertexGradient = false;
        _receiptText.outlineWidth = 0.15f;
        _receiptText.outlineColor = new Color32(0, 0, 0, 180);

        // ── Combo Text — below receipt ──────────────────────────────
        _comboText = CreateTMPText(canvasGo.transform, "ComboText",
            "", 28, FontStyles.Bold, new Color32(255, 210, 63, 255),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -100f), new Vector2(200f, 50f));
        _comboText.gameObject.SetActive(false);

        // ── Floor Meter — top right, 3 circles ──────────────────────
        Sprite circleSprite = CreateCircleSprite(64);

        for (int i = 0; i < k_MaxFloorHits; i++)
        {
            GameObject dotGo = new GameObject($"FloorDot_{i}");
            dotGo.transform.SetParent(canvasGo.transform, false);

            RectTransform rt = dotGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(60f, 60f);
            // Stack right-to-left from top-right corner
            rt.anchoredPosition = new Vector2(-20f - (i * 70f), -30f);

            Image img = dotGo.AddComponent<Image>();
            img.sprite = circleSprite;
            img.color  = new Color32(80, 80, 80, 120); // dim gray = empty
            _floorDots[i] = img;
        }

        Debug.Log("[ScoreManager] UI created — receipt, combo, floor meter.");
    }

    // ─────────────────────────────────────────────────────────────────
    // UI UPDATE
    // ─────────────────────────────────────────────────────────────────

    void RefreshUI()
    {
        // Receipt total
        if (_receiptText != null)
            _receiptText.text = $"₪{ReceiptTotal:F2}";

        // Combo indicator (only show at 3+ combo = x1.5 multiplier)
        if (_comboText != null)
        {
            bool showCombo = ComboCount >= 3;
            _comboText.gameObject.SetActive(showCombo);
            if (showCombo)
            {
                float mult = GetComboMultiplier();
                _comboText.text = $"x{mult:F1}";
            }
        }

        // Floor meter dots
        for (int i = 0; i < k_MaxFloorHits; i++)
        {
            if (_floorDots[i] != null)
            {
                _floorDots[i].color = i < FloorMeter
                    ? new Color32(220, 50, 50, 255)   // filled red
                    : new Color32(80, 80, 80, 120);    // empty gray
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────

    static TextMeshProUGUI CreateTMPText(
        Transform parent, string name, string text,
        float fontSize, FontStyles style, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = style;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return tmp;
    }

    /// <summary>Generates a white circle sprite at runtime (no asset dependency).</summary>
    static Sprite CreateCircleSprite(int diameter)
    {
        Texture2D tex = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false);
        float radius   = diameter * 0.5f;
        Vector2 center = new Vector2(radius, radius);

        for (int y = 0; y < diameter; y++)
        for (int x = 0; x < diameter; x++)
        {
            float dist = Vector2.Distance(new Vector2(x, y), center);
            tex.SetPixel(x, y, dist <= radius - 1f ? Color.white : Color.clear);
        }

        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return Sprite.Create(tex, new Rect(0, 0, diameter, diameter),
            new Vector2(0.5f, 0.5f), 100f);
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        OnScoreAdded = null;
        OnShiftEnd   = null;

        if (Instance == this)
            Instance = null;
    }
}
