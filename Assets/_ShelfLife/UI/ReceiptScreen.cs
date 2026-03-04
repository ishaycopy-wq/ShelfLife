using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// End-of-shift receipt screen. Subscribes to ScoreManager.OnShiftEnd.
/// After a 1-second delay, builds a full-screen receipt that prints
/// line by line (0.3s per line) with a typewriter effect.
/// Includes Next Shift and Share Receipt buttons.
/// No coroutines — pure Update-driven timer (per project rules).
/// </summary>
public sealed class ReceiptScreen : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static ReceiptScreen Instance { get; private set; }

    // ── Config ──────────────────────────────────────────────────────
    const float k_InitialDelay     = 1.0f;   // seconds after shift end before receipt appears
    const float k_LineDelay        = 0.3f;   // seconds between each line
    const float k_TotalLineDelay   = 0.5f;   // extra pause before TOTAL line
    const int   k_SortingOrder     = 100;
    const float k_FontSize         = 16f;
    const float k_TotalFontSize    = 24f;
    const float k_VatRate          = 0.17f;

    // ── Product descriptions ────────────────────────────────────────
    static readonly Dictionary<string, string> s_Descriptions = new Dictionary<string, string>
    {
        { "Red",        "bruise-free, betrayed" },
        { "Purple",     "2019, witnessed chaos" },
        { "White",      "1 of 12, condolences" },
        { "Green",      "ripe briefly" },
        { "DarkGreen",  "ripe briefly" },
        { "Orange",     "unstable" },
        { "Yellow",     "dramatic, claims to be vegetable" },
        { "Brown",      "survivor, again" },
        { "Pink",       "increasingly philosophical" },
        { "DarkBlue",   "seen things" },
    };

    // ── Product display names ───────────────────────────────────────
    static readonly Dictionary<string, string> s_DisplayNames = new Dictionary<string, string>
    {
        { "Red",        "Tomato" },
        { "Purple",     "Wine Bottle" },
        { "White",      "Egg Carton" },
        { "Green",      "Avocado" },
        { "DarkGreen",  "Cucumber" },
        { "Orange",     "Orange" },
        { "Yellow",     "Banana" },
        { "Brown",      "Cereal Box" },
        { "Pink",       "Yogurt" },
        { "DarkBlue",   "Detergent" },
    };

    // ── State machine ───────────────────────────────────────────────
    enum Phase { Idle, WaitingDelay, Printing, Done }
    Phase _phase = Phase.Idle;
    float _timer;
    int   _currentLine;
    readonly List<ReceiptLine> _lines = new List<ReceiptLine>();

    // ── UI refs ─────────────────────────────────────────────────────
    GameObject    _canvasGo;
    RectTransform _contentParent;
    Button        _nextShiftBtn;
    Button        _shareBtn;

    // ── Line data ───────────────────────────────────────────────────
    struct ReceiptLine
    {
        public string Text;
        public float  FontSize;
        public FontStyles Style;
        public float  ExtraDelay; // added before this line prints
    }

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[ReceiptScreen] Duplicate destroyed on '{gameObject.name}'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnShiftEnd += HandleShiftEnd;
        else
            Debug.LogError("[ReceiptScreen] ScoreManager.Instance is NULL in Start!");
    }

    // NOTE: OnShiftEnd subscription is in Start(), so unsubscription is in OnDestroy().
    // Using Start/OnDestroy pair (not OnEnable/OnDisable) to match subscription lifecycle.

    void Update()
    {
        switch (_phase)
        {
            case Phase.WaitingDelay:
                _timer += Time.deltaTime;
                if (_timer >= k_InitialDelay)
                {
                    CreateReceiptCanvas();
                    BuildLines();
                    _currentLine = 0;
                    _timer = 0f;
                    _phase = Phase.Printing;
                }
                break;

            case Phase.Printing:
                if (_currentLine >= _lines.Count)
                {
                    // All lines printed — show buttons
                    ShowButtons();
                    _phase = Phase.Done;
                    break;
                }

                float delay = k_LineDelay + _lines[_currentLine].ExtraDelay;
                _timer += Time.deltaTime;
                if (_timer >= delay)
                {
                    _timer -= delay;
                    PrintNextLine();
                }
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // EVENT HANDLER
    // ─────────────────────────────────────────────────────────────────

    void HandleShiftEnd()
    {
        if (_phase != Phase.Idle) return; // don't re-trigger

        _timer = 0f;
        _phase = Phase.WaitingDelay;

        int caughtCount = ScoreManager.Instance != null ? ScoreManager.Instance.CaughtProducts.Count : 0;
        float total = ScoreManager.Instance != null ? ScoreManager.Instance.ReceiptTotal : 0f;
        Debug.Log($"[ReceiptScreen] Shift ended — {caughtCount} products caught, total \u20aa{total:F2}. " +
                  $"Receipt will appear in {k_InitialDelay}s.");
    }

    // ─────────────────────────────────────────────────────────────────
    // BUILD RECEIPT LINES
    // ─────────────────────────────────────────────────────────────────

    void BuildLines()
    {
        _lines.Clear();

        ScoreManager sm = ScoreManager.Instance;
        if (sm == null) return;

        int receiptNum = Random.Range(10000, 99999);

        // Header
        _lines.Add(Line("LaLo Market - Aisle 3"));
        _lines.Add(Line($"Receipt #{receiptNum}"));
        _lines.Add(Line(""));  // blank

        // Caught products
        foreach (ScoreManager.CaughtProduct cp in sm.CaughtProducts)
        {
            string displayName = s_DisplayNames.TryGetValue(cp.ColorKey, out string dn) ? dn : cp.ColorKey;
            string desc = s_Descriptions.TryGetValue(cp.ColorKey, out string d) ? d : "mysterious";
            string priceStr = $"{cp.EarnedPrice:F2}";

            // Pad dots between description and price
            string leftPart = $"{displayName} ({desc})";
            int dotsNeeded = Mathf.Max(3, 44 - leftPart.Length - priceStr.Length);
            string dots = new string('.', dotsNeeded);

            _lines.Add(Line($"{leftPart} {dots} \u20aa{priceStr}"));
        }

        if (sm.CaughtProducts.Count == 0)
        {
            _lines.Add(Line("(nothing caught. impressive.)"));
        }

        _lines.Add(Line(""));  // blank

        // Totals
        float subtotal = sm.ReceiptTotal;
        float vat = subtotal * k_VatRate;
        float total = subtotal + vat;

        _lines.Add(Line($"SUBTOTAL ...................... \u20aa{subtotal:F2}"));
        _lines.Add(Line($"VAT (17%) .................... \u20aa{vat:F2}"));
        _lines.Add(Line(""));  // blank

        // TOTAL — bigger, bold, with extra delay
        _lines.Add(new ReceiptLine
        {
            Text       = $"TOTAL ........................ \u20aa{total:F2}",
            FontSize   = k_TotalFontSize,
            Style      = FontStyles.Bold,
            ExtraDelay = k_TotalLineDelay
        });

        _lines.Add(Line(""));  // blank
        _lines.Add(new ReceiptLine
        {
            Text       = "Thank you for shopping responsibly.",
            FontSize   = k_FontSize,
            Style      = FontStyles.Italic,
            ExtraDelay = 0f
        });

        Debug.Log($"[ReceiptScreen] Built {_lines.Count} receipt lines " +
                  $"({sm.CaughtProducts.Count} products, total \u20aa{sm.ReceiptTotal:F2}).");
    }

    static ReceiptLine Line(string text)
    {
        return new ReceiptLine
        {
            Text       = text,
            FontSize   = k_FontSize,
            Style      = FontStyles.Normal,
            ExtraDelay = 0f
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // LINE PRINTING
    // ─────────────────────────────────────────────────────────────────

    void PrintNextLine()
    {
        if (_currentLine >= _lines.Count) return;

        ReceiptLine rl = _lines[_currentLine];

        // Create text element
        GameObject go = new GameObject($"Line_{_currentLine}");
        go.transform.SetParent(_contentParent, false);

        // Ensure the GameObject is active
        go.SetActive(true);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text               = rl.Text;
        tmp.fontSize           = rl.FontSize;
        tmp.fontStyle          = rl.Style;
        tmp.color              = Color.white;
        tmp.alignment          = TextAlignmentOptions.Left;
        tmp.raycastTarget      = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode       = TextOverflowModes.Overflow;

        // Layout element — tells VLG how tall each line should be
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = rl.Text == "" ? 12f : rl.FontSize + 8f;
        le.flexibleWidth   = 1f;

        // Force layout rebuild so ContentSizeFitter recalculates
        LayoutRebuilder.ForceRebuildLayoutImmediate(_contentParent);

        // Diagnostic logging
        Debug.Log($"[ReceiptScreen] Line {_currentLine}/{_lines.Count}: " +
                  $"\"{rl.Text}\" | fontSize={rl.FontSize} color={tmp.color} " +
                  $"active={go.activeInHierarchy} font={tmp.font?.name ?? "NULL"} " +
                  $"contentH={_contentParent.sizeDelta.y:F0}");

        _currentLine++;
    }

    // ─────────────────────────────────────────────────────────────────
    // UI CREATION
    // ─────────────────────────────────────────────────────────────────

    void CreateReceiptCanvas()
    {
        // ── Canvas ──────────────────────────────────────────────────
        _canvasGo = new GameObject("ReceiptCanvas");
        Canvas canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = k_SortingOrder;

        CanvasScaler scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight  = 0.5f;

        _canvasGo.AddComponent<GraphicRaycaster>();

        Debug.Log("[ReceiptScreen] Canvas created (sortingOrder=100).");

        // ── Black background ────────────────────────────────────────
        // MUST be first child so receipt text renders ON TOP of it
        GameObject bgGo = new GameObject("Background");
        bgGo.transform.SetParent(_canvasGo.transform, false);

        RectTransform bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        Image bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color(0.03f, 0.03f, 0.03f, 0.95f); // near-black
        bgImg.raycastTarget = true; // blocks clicks on game behind

        Debug.Log("[ReceiptScreen] Background panel created (sibling 0).");

        // ── Scroll area — receipt content may overflow ───────────────
        // SECOND child — renders on top of background
        GameObject scrollGo = new GameObject("ScrollView");
        scrollGo.transform.SetParent(_canvasGo.transform, false);

        RectTransform scrollRt = scrollGo.AddComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0.05f, 0.15f); // room for buttons at bottom
        scrollRt.anchorMax = new Vector2(0.95f, 0.92f);
        scrollRt.offsetMin = Vector2.zero;
        scrollRt.offsetMax = Vector2.zero;

        ScrollRect scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal   = false;
        scrollRect.vertical     = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // FIX: Use RectMask2D instead of Mask + Image(Color.clear).
        // The old Mask required the Image to render pixels for the stencil
        // buffer — with Color.clear (alpha=0), no pixels rendered, so the
        // stencil was never written and ALL children were masked out.
        // RectMask2D clips by rect bounds without needing a stencil/Image.
        scrollGo.AddComponent<RectMask2D>();

        Debug.Log("[ReceiptScreen] ScrollView created with RectMask2D (sibling 1).");

        // ── Content container ───────────────────────────────────────
        GameObject contentGo = new GameObject("Content");
        contentGo.transform.SetParent(scrollGo.transform, false);

        _contentParent = contentGo.AddComponent<RectTransform>();
        _contentParent.anchorMin = new Vector2(0f, 1f);
        _contentParent.anchorMax = new Vector2(1f, 1f);
        _contentParent.pivot     = new Vector2(0.5f, 1f);
        _contentParent.sizeDelta = new Vector2(0f, 0f); // grows via ContentSizeFitter

        VerticalLayoutGroup vlg = contentGo.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment         = TextAnchor.UpperLeft;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;  // FIX: was false — VLG must control heights
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(20, 20, 20, 20);

        ContentSizeFitter csf = contentGo.AddComponent<ContentSizeFitter>();
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        scrollRect.content  = _contentParent;
        scrollRect.viewport = scrollRt; // explicit viewport

        Debug.Log("[ReceiptScreen] Content container created (VLG + ContentSizeFitter).");

        // ── Buttons (hidden until receipt is complete) ───────────────
        CreateButtons();

        Debug.Log("[ReceiptScreen] Canvas fully built — printing receipt...");
    }

    void CreateButtons()
    {
        // ── Next Shift — bottom center ──────────────────────────────
        _nextShiftBtn = CreateButton("NextShiftBtn", "NEXT SHIFT",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 100f), new Vector2(300f, 60f),
            new Color32(40, 180, 80, 255));
        _nextShiftBtn.onClick.AddListener(OnNextShift);
        _nextShiftBtn.gameObject.SetActive(false);

        // ── Share Receipt — above Next Shift ─────────────────────────
        _shareBtn = CreateButton("ShareBtn", "SHARE RECEIPT",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 170f), new Vector2(300f, 60f),
            new Color32(80, 80, 100, 255));
        _shareBtn.onClick.AddListener(OnShareReceipt);
        _shareBtn.gameObject.SetActive(false);
    }

    Button CreateButton(string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 size, Color32 bgColor)
    {
        GameObject btnGo = new GameObject(name);
        btnGo.transform.SetParent(_canvasGo.transform, false);

        RectTransform rt = btnGo.AddComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;

        Image img = btnGo.AddComponent<Image>();
        img.color = bgColor;

        Button btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = img;

        // Label text
        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(btnGo.transform, false);

        RectTransform labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text          = label;
        tmp.fontSize      = 20f;
        tmp.fontStyle     = FontStyles.Bold;
        tmp.color         = Color.white;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return btn;
    }

    void ShowButtons()
    {
        if (_nextShiftBtn != null) _nextShiftBtn.gameObject.SetActive(true);
        if (_shareBtn != null)     _shareBtn.gameObject.SetActive(true);
        Debug.Log("[ReceiptScreen] Receipt complete — buttons visible.");
    }

    // ─────────────────────────────────────────────────────────────────
    // BUTTON HANDLERS
    // ─────────────────────────────────────────────────────────────────

    void OnNextShift()
    {
        Debug.Log("[ReceiptScreen] NEXT SHIFT — resetting encounter.");

        // ── ORDERING IS CRITICAL ──────────────────────────────────────
        // Steps 1-4 prepare the world state.
        // Step 4 (ResetAllProducts) must run BEFORE step 5 (CustomerController re-caches
        // products via CacheAllProducts) and step 8 (ProductSpawner re-queues products).
        // Step 5 restores Time.timeScale if slow-mo was active.
        // ──────────────────────────────────────────────────────────────

        // 1. Hide receipt and reset own state
        ResetSelf();

        // 2. Reset score
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.ResetForNewShift();

        // 3. Clean floor stains
        CleanFloorStains();

        // 4. Reset all products to shelf positions (BEFORE customer/spawner re-cache)
        ResetAllProducts();

        // 5. Reset customer (re-caches products, restores timeScale)
        if (CustomerController.Instance != null)
            CustomerController.Instance.ResetForNewCustomer();

        // 6. Reset employee
        if (StealthBehavior.Instance != null)
            StealthBehavior.Instance.ResetForNewShift();

        // 7. Reset camera
        if (CameraStateManager.Instance != null)
            CameraStateManager.Instance.TransitionToWideShot();

        // 8. Unpause and re-queue spawner (products must be reset by step 4)
        if (ProductSpawner.Instance != null)
            ProductSpawner.Instance.ResetForNewShift();
    }

    /// <summary>
    /// Destroy the receipt canvas and return ReceiptScreen to Idle phase.
    /// </summary>
    void ResetSelf()
    {
        if (_canvasGo != null)
            Destroy(_canvasGo);
        _canvasGo      = null;
        _contentParent = null;
        _nextShiftBtn  = null;
        _shareBtn      = null;
        _lines.Clear();
        _currentLine = 0;
        _phase       = Phase.Idle;
        _timer       = 0f;
    }

    /// <summary>
    /// Destroy all floor stain quads spawned by ScoreManager.SpawnStain.
    /// Only called once per reset — FindObjectsByType is acceptable here.
    /// </summary>
    static void CleanFloorStains()
    {
        var allTransforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var t in allTransforms)
        {
            if (t.gameObject.name == "FloorStain")
            {
                Object.Destroy(t.gameObject);
                count++;
            }
        }
        Debug.Log($"[ReceiptScreen] Cleaned {count} floor stains.");
    }

    /// <summary>
    /// Re-enable all products and reset them to their original shelf positions.
    /// Uses FindObjectsByType with FindObjectsInactive.Include so caught
    /// products (which were SetActive(false)) are also found and restored.
    /// </summary>
    static void ResetAllProducts()
    {
        ProductFall[] allFalls = Object.FindObjectsByType<ProductFall>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (ProductFall fall in allFalls)
        {
            // Re-enable if disabled (caught products get SetActive(false))
            fall.gameObject.SetActive(true);

            // Reset ProductFall state and position
            fall.ResetToShelf();
        }
        Debug.Log($"[ReceiptScreen] Reset {allFalls.Length} products to shelf.");
    }

    static void OnShareReceipt()
    {
        Debug.Log("[ReceiptScreen] SHARE RECEIPT pressed — placeholder, share functionality coming soon.");
        // TODO: implement native share sheet or screenshot capture
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnShiftEnd -= HandleShiftEnd;

        if (Instance == this)
            Instance = null;
    }
}
