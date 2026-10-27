using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Owns a single persistent, player-dismissed warning "window" for the kind
/// of human-error feedback NotificationManager's auto-fading corner toast is
/// too brief for (e.g. "that table placement would be nearly a single tile").
/// Distinct from NotificationManager (quick, non-interactive, auto-dismissing
/// event feedback) and from TutorialUIManager/ConceptCardManager (scripted
/// guide content) — this is reserved for "you're about to make/just made a
/// mistake" call-outs that need to stay on screen until the player actually
/// reads and closes them.
///
/// Wears the same Windows-style draggable title bar (WindowChromeBuilder) as
/// every other floating annotation in the game, so the player can drag it off
/// whatever it's covering. Everything else is locked until it's closed (see
/// AnnotationLock). Only ever shows one warning at a time
/// (a second Show() while one is open just replaces its text) and keeps
/// wherever the player last dragged it to across calls, same as a real OS
/// window would. The message is read aloud while narration is on (see
/// NarrationManager).
/// </summary>
public class AnnotationWarningManager : MonoBehaviour
{
    public static AnnotationWarningManager Instance { get; private set; }

    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    static readonly Vector2 PanelSize = new Vector2(380f, 130f);
    const float TitleBarHeight  = 26f; // must match WindowChromeBuilder.BarHeight
    const float TextPaddingX    = 18f;
    const float TextPaddingTop  = 14f;
    const float TextPaddingBottom = 14f;
    const float MinBodyHeight   = 60f;
    const float MaxPanelHeight  = 300f;
    const float WaveformHeight  = 28f; // the speaking indicator's row under the text, while narration is on

    static readonly Color PanelFill   = new Color(0.08f, 0.10f, 0.15f, 0.97f);
    static readonly Color PanelTrim   = new Color(0.02f, 0.03f, 0.05f, 1f);

    TMP_FontAsset font;
    Canvas canvas;
    RectTransform panelRect;
    Image panelBg;
    TextMeshProUGUI titleLabel;
    TextMeshProUGUI bodyLabel;
    NarrationWaveform waveform;
    GameObject inputBlocker;
    bool built;
    bool everShown;

    /// <summary>True while the warning is up — the rest of the game is locked
    /// until the player closes it (see AnnotationLock).</summary>
    public bool IsOpen => panelRect != null && panelRect.gameObject.activeSelf;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        font = Resources.Load<TMP_FontAsset>(FontResourcePath);
        BuildPanel();
        panelRect.gameObject.SetActive(false);
    }

    void BuildPanel()
    {
        var canvasGo = new GameObject("AnnotationWarning_Canvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 25; // above CLI_Canvas (20) / Guide_Canvas (21) / Tutorial_Canvas (22), below ConceptCard_Canvas (30) and LevelComplete (50)

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        inputBlocker = AnnotationLock.CreateBlocker(canvasGo.transform);

        var panelGo = new GameObject("WarningPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        panelBg = panelGo.AddComponent<Image>();
        panelBg.type = Image.Type.Sliced;

        panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRect.pivot            = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta        = PanelSize;

        titleLabel = MakeLabel(panelGo.transform, "Title", 18, FontStyles.Bold, TextAlignmentOptions.TopLeft,
            new Vector2(0f, -(TitleBarHeight + TextPaddingTop)), new Vector2(PanelSize.x - TextPaddingX * 2f, 26f));
        titleLabel.color = Color.white;

        bodyLabel = MakeLabel(panelGo.transform, "Body", 15, FontStyles.Normal, TextAlignmentOptions.TopLeft,
            new Vector2(0f, -(TitleBarHeight + TextPaddingTop + 30f)), new Vector2(PanelSize.x - TextPaddingX * 2f, MinBodyHeight));
        bodyLabel.color = new Color(0.85f, 0.90f, 0.97f);
        bodyLabel.lineSpacing = 3f;

        // Speaking indicator along the bottom edge — AutoSizePanel only makes
        // room for it while narration is on.
        waveform = NarrationWaveform.Create(panelGo.transform, this, GameTheme.Warning);
        waveform.Rect.anchorMin = Vector2.zero;
        waveform.Rect.anchorMax = new Vector2(1f, 0f);
        waveform.Rect.offsetMin = new Vector2(TextPaddingX, TextPaddingBottom);
        waveform.Rect.offsetMax = new Vector2(-TextPaddingX, TextPaddingBottom + WaveformHeight);

        var closeBtn = WindowChromeBuilder.Build(panelGo.transform, panelRect, canvas, font,
            "Warning", PanelSize.x, GameTheme.Warning, showCloseButton: true);
        closeBtn.onClick.AddListener(Hide);
    }

    TextMeshProUGUI MakeLabel(Transform parent, string name, int fontSize, FontStyles style, TextAlignmentOptions align,
                               Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font      = font;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.raycastTarget = false;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 1f);
        rt.anchorMax        = new Vector2(0.5f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;
        return tmp;
    }

    /// <summary>Shows (or updates, if already open) the warning window. Centers it
    /// only the first time it's ever shown — later calls leave it wherever the
    /// player last dragged it, same as a real window.</summary>
    public void Show(string title, string message, NotifyKind kind = NotifyKind.Warning)
    {
        EnsureBuilt();

        Color accent = kind switch
        {
            NotifyKind.Danger  => GameTheme.Danger,
            NotifyKind.Success => GameTheme.Success,
            NotifyKind.Info    => GameTheme.Info,
            _                  => GameTheme.Warning,
        };
        panelBg.sprite = PlateSpriteBuilder.GetSprite(
            Color.Lerp(PanelFill, accent, 0.12f), accent, PanelTrim, borderThickness: 3, chamfer: 14, pixelScale: 4);
        waveform.SetColor(accent);

        titleLabel.text = title;
        bodyLabel.text  = message;
        AutoSizePanel();
        NarrationManager.Instance?.Speak(this, message);

        if (!everShown)
        {
            everShown = true;
            panelRect.anchoredPosition = Vector2.zero; // centered on first-ever appearance
        }

        panelRect.gameObject.SetActive(true);
        AnnotationLock.SetBlocker(inputBlocker, true);
        // A warning here is the game refusing something the player just did
        // (e.g. "Table Too Small"); an Info one is just a heads-up. Played on
        // every Show — a replaced warning is still a new one.
        bool refusal = kind == NotifyKind.Warning || kind == NotifyKind.Danger;
        AudioManager.Instance?.PlaySfx(refusal ? SfxId.Refusal : SfxId.PanelPrompt);
    }

    public void Hide()
    {
        if (panelRect != null) panelRect.gameObject.SetActive(false);
        AnnotationLock.SetBlocker(inputBlocker, false);
        NarrationManager.Instance?.Stop(this);
    }

    void AutoSizePanel()
    {
        float textWidth = PanelSize.x - TextPaddingX * 2f;
        float bodyHeight = bodyLabel.GetPreferredValues(bodyLabel.text, textWidth, 0f).y;
        bodyHeight = Mathf.Max(bodyHeight, MinBodyHeight);

        float waveformRow = NarrationManager.Enabled ? WaveformHeight + TextPaddingBottom : 0f;
        float neededHeight = TitleBarHeight + TextPaddingTop + 30f + bodyHeight + waveformRow + TextPaddingBottom;
        float height = Mathf.Clamp(neededHeight, PanelSize.y, MaxPanelHeight);
        panelRect.sizeDelta = new Vector2(PanelSize.x, height);
        bodyLabel.rectTransform.sizeDelta = new Vector2(textWidth, bodyHeight);
    }
}
