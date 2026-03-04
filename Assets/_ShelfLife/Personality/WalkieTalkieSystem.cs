using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Singleton walkie-talkie commentary system — radio-style typewriter text
/// with "KSHHH - " prefix. Top-center tactical HUD bar, green monospace text.
/// Instant radio cut-off on dismiss. Update-driven, no coroutines.
/// </summary>
public sealed class WalkieTalkieSystem : MonoBehaviour
{
    // ── Singleton ───────────────────────────────────────────────────
    public static WalkieTalkieSystem Instance { get; private set; }

    // ── Config ──────────────────────────────────────────────────────
    const string k_RadioPrefix   = "KSHHH - ";
    const float  k_CharDelay     = 0.05f;   // seconds per character
    const float  k_HoldTime      = 2.0f;    // seconds after typing completes
    const int    k_FontSize      = 20;
    const int    k_PanelWidth    = 900;
    const int    k_PanelHeight   = 60;
    const int    k_TopOffset     = 40;
    const int    k_PaddingH      = 10;

    // ── Clumsy stealth commentary lines (body only — prefix auto-added) ──
    static readonly string[] s_Lines =
    {
        "...moving to intercept. Ignore the falling tuna display. Over.",
        "Target is near the dairy aisle. Requesting backup. And a mop. Over.",
        "Lost visual. Suspect may be disguised as a shopping cart. Over.",
        "Command, the bananas are hostile. Repeat, hostile. Over.",
        "Stealth approach compromised by... squeaky shoe. Over.",
        "Perimeter breached in Aisle 3. It's the yogurt. Again. Over.",
        "HQ, customer is making eye contact. Abort? ...Abort?! Over.",
        "Initiating Operation Breadbasket. This is not a drill. Over.",
        "Target acquired. Wait — that's a mannequin. Disregard. Over.",
        "Requesting air support near frozen foods. It's getting dicey. Over.",
    };

    // TODO: Wire to gameplay events in Sprint 2+ (e.g., cascade triggers,
    //       customer sightings, VisualSilence moments). Currently debug-only.

    // ── State ───────────────────────────────────────────────────────
    enum Phase { Idle, Typing, Holding, Done }
    Phase _phase = Phase.Idle;
    float _phaseTimer;
    int   _revealedChars;
    int   _totalChars;

    // ── UI refs ─────────────────────────────────────────────────────
    TextMeshProUGUI _text;
    CanvasGroup     _canvasGroup;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[WalkieTalkie] Duplicate destroyed on '{gameObject.name}'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        CreateUI();
        Debug.Log("[WalkieTalkie] System ready.");
    }

    void Update()
    {
        // ── Debug trigger: 'T' key plays random line ────────────
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            PlayRandom();
#endif

        // ── Typewriter state machine ────────────────────────────
        switch (_phase)
        {
            case Phase.Idle:
                return;

            case Phase.Typing:
            {
                _phaseTimer += Time.deltaTime;
                int charsToShow = Mathf.Min(
                    Mathf.FloorToInt(_phaseTimer / k_CharDelay) + 1,
                    _totalChars);

                if (charsToShow != _revealedChars)
                {
                    _revealedChars = charsToShow;
                    _text.maxVisibleCharacters = _revealedChars;
                }

                if (_revealedChars >= _totalChars)
                {
                    _phase = Phase.Holding;
                    _phaseTimer = 0f;
                }
                break;
            }

            case Phase.Holding:
            {
                _phaseTimer += Time.deltaTime;
                if (_phaseTimer >= k_HoldTime)
                {
                    _phase = Phase.Done;
                }
                break;
            }

            case Phase.Done:
            {
                // Instant radio cut-off
                _canvasGroup.alpha = 0f;
                _phase = Phase.Idle;
                break;
            }
        }
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>Play a specific line with radio prefix.</summary>
    public void PlayLine(string messageBody)
    {
        if (_text == null || _canvasGroup == null) return;

        string full = k_RadioPrefix + messageBody;
        _text.text = full;
        _totalChars = full.Length;
        _revealedChars = 0;
        _text.maxVisibleCharacters = 0;

        _canvasGroup.alpha = 1f;
        _phase = Phase.Typing;
        _phaseTimer = 0f;
    }

    /// <summary>Play a random clumsy stealth line.</summary>
    public void PlayRandom()
    {
        PlayLine(s_Lines[Random.Range(0, s_Lines.Length)]);
    }

    // ── UI Creation ─────────────────────────────────────────────────
    void CreateUI()
    {
        // Canvas — above SubtitleCanvas (15), below CatchFlash (999)
        GameObject canvasGo = new GameObject("WalkieTalkieCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight  = 0.5f;

        // No GraphicRaycaster — clicks pass through

        _canvasGroup = canvasGo.AddComponent<CanvasGroup>();
        _canvasGroup.alpha          = 0f;
        _canvasGroup.interactable   = false;
        _canvasGroup.blocksRaycasts = false;

        // Dark background panel — top-center
        GameObject panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);

        RectTransform panelRt = panelGo.AddComponent<RectTransform>();
        panelRt.anchorMin        = new Vector2(0.5f, 1f);
        panelRt.anchorMax        = new Vector2(0.5f, 1f);
        panelRt.pivot            = new Vector2(0.5f, 1f);
        panelRt.anchoredPosition = new Vector2(0f, -k_TopOffset);
        panelRt.sizeDelta        = new Vector2(k_PanelWidth, k_PanelHeight);

        Image panelImg = panelGo.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.75f);
        panelImg.raycastTarget = false;

        // Text — inside panel, with horizontal padding
        GameObject textGo = new GameObject("RadioText");
        textGo.transform.SetParent(panelGo.transform, false);

        RectTransform textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(k_PaddingH, 0f);
        textRt.offsetMax = new Vector2(-k_PaddingH, 0f);

        _text = textGo.AddComponent<TextMeshProUGUI>();
        _text.text                = "";
        _text.fontSize            = k_FontSize;
        _text.fontStyle           = FontStyles.Normal;
        _text.color               = new Color(0f, 1f, 0.3f, 1f); // green terminal
        _text.alignment           = TextAlignmentOptions.MidlineLeft;
        _text.raycastTarget       = false;
        _text.maxVisibleCharacters = 0;

        // Attempt monospace font — LiberationSans SDF is TMP's default (proportional).
        // TODO: Import a true monospace TMP font (e.g., Liberation Mono, Courier)
        //       and update this path for the full tactical radio aesthetic.
        TMP_FontAsset monoFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (monoFont != null)
            _text.font = monoFont;
        else
            Debug.LogWarning("[WalkieTalkie] Monospace font not found — using TMP default.");

        Debug.Log("[WalkieTalkie] UI created — top-center tactical HUD bar.");
    }

    // ─────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
