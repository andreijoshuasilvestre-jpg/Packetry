using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Generic "Are you sure?" confirmation modal — shared by any action that
/// should gate behind a confirm/cancel step before it runs (see
/// NetworkUIManager's top-bar Return/Home/Exit buttons). Same self-contained
/// screen-space-overlay singleton pattern as LevelCompletePanel/
/// DeviceAddressPopup, but generalized to any message/action instead of
/// being tied to level completion.
///
/// The panel itself matches Assets/Textures/Panel_Template.png — the
/// chamfered-plate look PlateSpriteBuilder already generates elsewhere (see
/// LevelSelectManager's carousel backdrop) — rather than a flat rectangle,
/// and every label uses the same PixeloidMono SDF font as the main menu
/// (MainMenuBootstrap/LoadingScreenManager) instead of TMP's default font.
/// </summary>
public class ConfirmDialogManager : MonoBehaviour
{
    public static ConfirmDialogManager Instance { get; private set; }

    public bool IsOpen => dimRoot != null && dimRoot.activeSelf;

    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    // Matches Assets/Textures/Panel_Template.png (dark navy fill, steel-blue
    // chamfered border, near-black trim line) — see PlateSpriteBuilder.
    static readonly Color32 PanelFill   = new Color32(14, 18, 28, 250);
    static readonly Color32 PanelBorder = new Color32(36, 82, 130, 255);
    static readonly Color32 PanelTrim   = new Color32(5, 8, 13, 255);

    static readonly Vector2 PanelSize = new Vector2(520f, 250f);

    // Button row geometry (also used by MakeButton below) — pulled out as
    // constants so the body label can center itself in whatever vertical
    // space is actually left above the buttons, instead of a hand-picked
    // offset that only happened to look centered at one PanelSize.
    const float ButtonWidth        = 220f;
    const float ButtonHeight       = 52f;
    const float ButtonBottomMargin = 32f;
    const float ButtonGapFromCenter = 130f; // each button's x-offset from panel center

    TMP_FontAsset font;
    GameObject dimRoot;
    RectTransform panelRt;
    ZoomPanel panelZoom;
    PanelArrivalGlow panelGlow;
    TextMeshProUGUI bodyLabel;
    TextMeshProUGUI confirmLabel;
    TextMeshProUGUI cancelLabel;
    Image confirmImage;
    Action onConfirm;
    Action onCancel;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        font = Resources.Load<TMP_FontAsset>(FontResourcePath);
        BuildPanel();
        dimRoot.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Shows the modal with message/button text and the action to run if confirmed.
    /// Cancel just closes the modal, running onCancelClicked if given — for a
    /// question with two real answers (see NarrationManager.ConfirmForLevel)
    /// rather than an "are you sure?". confirmColor defaults to Danger, the
    /// destructive-action red.</summary>
    public void Show(string message, string confirmText, Action onConfirmClicked, string cancelText = "Cancel",
        Action onCancelClicked = null, Color? confirmColor = null)
    {
        if (bodyLabel == null) return; // BuildPanel hasn't run yet (called before Start)

        bodyLabel.text     = message;
        confirmLabel.text  = confirmText;
        cancelLabel.text   = cancelText;
        confirmImage.color = confirmColor ?? GameTheme.Danger;
        onConfirm          = onConfirmClicked;
        onCancel           = onCancelClicked;

        // dimRoot must already be active before ZoomIn() — it starts a
        // coroutine on panelZoom, which is a child of dimRoot and can't run
        // one while disabled.
        dimRoot.SetActive(true);
        panelZoom.ZoomIn(panelGlow.Play);
    }

    /// <summary>"Quit the game?" prompt — shared by the gameplay top bar's Exit
    /// button (NetworkUIManager) and the main menu's Quit button.</summary>
    public void ShowQuitPrompt()
    {
        Show("Quit the game?", "Quit", () =>
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });
    }

    /// <summary>Closes the modal instantly, no zoom — used when something else
    /// (a scene load, Application.Quit) is about to cover the screen anyway, so
    /// an animated close would just get cut off. See OnCancelClicked for the
    /// animated dismiss used when the player actually backs out.</summary>
    public void Hide()
    {
        if (dimRoot != null) dimRoot.SetActive(false);
        onConfirm = null;
        onCancel  = null;
    }

    void OnConfirmClicked()
    {
        var callback = onConfirm;
        Hide();
        callback?.Invoke();
    }

    void OnCancelClicked()
    {
        var callback = onCancel;
        onConfirm = null;
        onCancel  = null;
        panelZoom.ZoomOut(() => dimRoot.SetActive(false));
        callback?.Invoke();
    }

    // ── UI construction ──────────────────────────────────────────────────────

    void BuildPanel()
    {
        var canvasGo = new GameObject("ConfirmDialog_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60; // above LevelCompletePanel (50) — a confirm gate always wins

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Full-screen dim backdrop — blocks input to the scene behind it (see
        // LevelCompletePanel.dimRoot for why Show()/Hide() toggle this, not
        // just the inner panel).
        var dimGo = new GameObject("Dim");
        dimRoot = dimGo;
        dimGo.transform.SetParent(canvasGo.transform, false);
        var dimImg = dimGo.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.6f);
        var dimRt = dimGo.GetComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;

        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(dimGo.transform, false);
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.sprite = PlateSpriteBuilder.GetSprite(PanelFill, PanelBorder, PanelTrim,
            borderThickness: 4, chamfer: 22, pixelScale: 4);
        panelImg.type = Image.Type.Sliced;

        panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot     = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = PanelSize;

        // Center the message in whatever vertical space is actually left
        // above the button row (not the panel's own center) — otherwise the
        // button row's height eats into the bottom of the panel and the text
        // reads as sitting too high/off-center relative to what's visible
        // above it.
        float panelHalfHeight = PanelSize.y * 0.5f;
        float buttonRowTop    = -panelHalfHeight + ButtonBottomMargin + ButtonHeight;
        float bodyAreaHeight  = panelHalfHeight - buttonRowTop;
        float bodyCenterY     = (panelHalfHeight + buttonRowTop) * 0.5f;

        bodyLabel = MakeLabel(panelGo.transform, "Body", 22,
            new Vector2(0f, bodyCenterY), new Vector2(440f, bodyAreaHeight - 16f));
        bodyLabel.color = new Color(0.88f, 0.92f, 0.97f);

        MakeButton(panelGo.transform, "CancelButton", new Color(0.30f, 0.32f, 0.38f),
            new Vector2(-ButtonGapFromCenter, ButtonBottomMargin), OnCancelClicked, out cancelLabel);

        confirmImage = MakeButton(panelGo.transform, "ConfirmButton", GameTheme.Danger,
            new Vector2(ButtonGapFromCenter, ButtonBottomMargin), OnConfirmClicked, out confirmLabel);

        // Zooms the panel itself (not the dim backdrop, which just snaps
        // visible/invisible) up from nothing on open — same entrance as the
        // level-select panel (see ZoomPanel) — then plays the button-style
        // arrival glow once it's full size; zooms back down when the player
        // cancels out.
        panelZoom = panelGo.AddComponent<ZoomPanel>();
        panelGlow = PanelArrivalGlow.Attach(panelGo, GameTheme.ElectricBlue);
    }

    TextMeshProUGUI MakeLabel(Transform parent, string name, int fontSize, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font        = font;
        tmp.fontSize    = fontSize;
        tmp.alignment   = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;
        return tmp;
    }

    Image MakeButton(Transform parent, string name, Color color, Vector2 anchoredPos,
                     UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI label)
    {
        var btnGo = new GameObject(name);
        btnGo.transform.SetParent(parent, false);
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = color;
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin        = new Vector2(0.5f, 0f);
        btnRt.anchorMax        = new Vector2(0.5f, 0f);
        btnRt.pivot            = new Vector2(0.5f, 0f);
        btnRt.anchoredPosition = anchoredPos;
        btnRt.sizeDelta        = new Vector2(ButtonWidth, ButtonHeight);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(btnGo.transform, false);
        label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font      = font;
        label.fontSize  = 18;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color     = Color.white;
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(onClick);
        return btnImg;
    }
}
