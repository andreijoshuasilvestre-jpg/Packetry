using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum NotifyKind { Info, Success, Warning, Danger }

/// <summary>
/// Owns a single transient bottom-right toast for one-off event feedback
/// (ping results, objective complete, send failures, etc.) — the kind of
/// message NetworkUIManager's status bar can't show because it's overwritten
/// every frame by mode-checking logic in NetworkUIManager.Update().
///
/// Styled as the same chamfered PlateSpriteBuilder plate + PixeloidMono SDF
/// pixel font every other panel in the game uses (CLIPanelManager,
/// DeviceAddressPopup) — a dark fill tinted toward the NotifyKind's color
/// with a bright border in that same color, rather than the old flat solid-
/// color banner. Reuses SlidePanel (the same slide-in/out utility the
/// sidebar/status bar/task panel already use) for the enter-from/exit-toward
/// the right edge motion instead of hand-rolling its own tween.
/// </summary>
public class NotificationManager : MonoBehaviour
{
    public static NotificationManager Instance { get; private set; }

    const float HoldTime      = 2.5f;
    const float SlideDuration = 0.35f;
    const float SlideDistance = 460f; // clears the toast's own width plus margin, so it's fully off-screen when hidden
    const float BottomMargin  = 20f;
    // Toasts stack above ViewControlsHud's bottom-right buttons, not over them.
    static float ToastBottom => BottomMargin + ViewControlsHud.BottomRightReservedHeight;
    const float RightMargin   = 20f;

    static readonly Vector2 ToastSize = new Vector2(400, 48);

    // Height is no longer fixed: MinToastHeight is just the floor for short
    // messages, and AutoSizeToast() grows the banner to fit whatever message
    // actually needs (same fix as TutorialUIManager.AutoSizeCallout) — a long
    // Danger notification (e.g. "That address is already in use by ...")
    // used to overflow past the toast's bottom edge instead of growing the
    // banner to fit it. Capped so a runaway string can't dominate the screen.
    const float MinToastHeight = 48f;
    const float MaxToastHeight = 160f;
    const float TextPaddingX   = 16f; // matches lrt offsetMin/offsetMax.x magnitude
    const float TextPaddingY   = 4f;  // matches lrt offsetMin/offsetMax.y magnitude

    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    // Same dark navy base every panel in the game shares (GameTheme.PanelBg),
    // tinted toward the notification's own color rather than the old flat
    // solid-color banner — reads as "one of this game's panels that happens
    // to be color-coded" instead of a generic UI toast.
    static readonly Color PanelBase = new Color(0.08f, 0.10f, 0.15f, 0.97f);
    static readonly Color TrimColor = new Color(0.02f, 0.03f, 0.05f, 1f);

    RectTransform bannerRect;
    CanvasGroup   bannerGroup;
    Image         bannerBg;
    TextMeshProUGUI bannerLabel;
    SlidePanel    slidePanel;
    Coroutine     activeRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        BuildCanvas();
    }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("Notification_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 7; // between TaskUIManager (5) and NetworkUIManager's HUD (10)

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();

        var banner = new GameObject("Toast");
        banner.transform.SetParent(canvasGo.transform, false);

        bannerGroup       = banner.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 1f; // the slide itself is the transition — no fade layered on top
        bannerGroup.blocksRaycasts = false;
        bannerGroup.interactable   = false;

        bannerBg = banner.AddComponent<Image>();
        bannerBg.type = Image.Type.Sliced;

        bannerRect = banner.GetComponent<RectTransform>();
        bannerRect.anchorMin        = new Vector2(1f, 0f);
        bannerRect.anchorMax        = new Vector2(1f, 0f);
        bannerRect.pivot            = new Vector2(1f, 0f);
        bannerRect.anchoredPosition = new Vector2(-RightMargin, ToastBottom);
        bannerRect.sizeDelta        = ToastSize;

        // Position must be set (above) BEFORE adding SlidePanel — its Awake()
        // captures whatever anchoredPosition is already there as "rest".
        slidePanel = banner.AddComponent<SlidePanel>();
        slidePanel.Configure(SlideEdge.Right, SlideDistance, SlideDuration);

        var font = Resources.Load<TMP_FontAsset>(FontResourcePath);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(banner.transform, false);
        bannerLabel               = labelGo.AddComponent<TextMeshProUGUI>();
        bannerLabel.font           = font;
        bannerLabel.fontSize       = 15;
        bannerLabel.alignment      = TextAlignmentOptions.Center;
        bannerLabel.color          = Color.white;
        bannerLabel.fontStyle      = FontStyles.Bold;
        bannerLabel.enableWordWrapping = true;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(16, 4);
        lrt.offsetMax = new Vector2(-16, -4);

        bannerRect.anchoredPosition = new Vector2(-RightMargin, ToastBottom) + Vector2.right * SlideDistance;
    }

    public void Notify(string message, NotifyKind kind = NotifyKind.Info)
    {
        if (bannerLabel == null) return; // BuildCanvas hasn't run yet (called before Start)

        bannerLabel.text = message;
        AutoSizeToast(message);
        Color kindColor = ColorFor(kind);
        bannerBg.sprite = PlateSpriteBuilder.GetSprite(
            Color.Lerp(PanelBase, kindColor, 0.25f), kindColor, TrimColor,
            borderThickness: 2, chamfer: 4, pixelScale: 3);

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(ShowRoutine());
    }

    /// <summary>Grows (or shrinks back down) the toast to fit message at the fixed
    /// ToastSize.x width, so a long notification pushes the banner taller instead
    /// of overflowing past its bottom edge — see the class comment on
    /// MinToastHeight/MaxToastHeight for the bug this replaced. bannerRect is
    /// bottom-pivoted (pivot.y = 0, see BuildCanvas), so growing sizeDelta.y
    /// extends the banner upward from its fixed bottom-right rest position
    /// without needing any repositioning math.</summary>
    void AutoSizeToast(string message)
    {
        float textWidth   = ToastSize.x - TextPaddingX * 2f;
        float textHeight  = bannerLabel.GetPreferredValues(message, textWidth, 0f).y;
        float neededHeight = textHeight + TextPaddingY * 2f;
        float height = Mathf.Clamp(neededHeight, MinToastHeight, MaxToastHeight);
        bannerRect.sizeDelta = new Vector2(ToastSize.x, height);
    }

    static Color ColorFor(NotifyKind kind) => kind switch
    {
        NotifyKind.Success => GameTheme.Success,
        NotifyKind.Warning => GameTheme.Warning,
        NotifyKind.Danger  => GameTheme.Danger,
        _                  => GameTheme.Info,
    };

    IEnumerator ShowRoutine()
    {
        slidePanel.SlideIn();
        yield return new WaitForSeconds(HoldTime);
        slidePanel.SlideOut();
        activeRoutine = null;
    }
}
