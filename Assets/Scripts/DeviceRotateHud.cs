using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Bottom-center rotate buttons for the selected device: turn it 45°
/// counter-clockwise or clockwise per click, with a caption between them
/// naming the device and its current angle. Only on screen while a device is
/// selected — the same state hold-R-and-scroll rotation works in (see
/// DragDropManager.CanRotateHighlighted) — so it fades in exactly when it can
/// be used and the R+scroll gesture no longer has to be discovered or
/// remembered. Same buttons as ViewControlsHud (HudIconButton), sitting on the
/// same baseline.
/// </summary>
public class DeviceRotateHud : MonoBehaviour
{
    public static DeviceRotateHud Instance { get; private set; }

    const float Spacing      = 10f;
    const float CaptionWidth = 132f;
    const float FadeDuration = 0.12f;
    const float RiseDistance = 10f; // slides up this far as it fades in

    TMP_FontAsset font;
    GameObject root;
    RectTransform rootRt;
    CanvasGroup group;
    TextMeshProUGUI captionDetail;
    float alpha;

    /// <summary>The whole cluster — what Level 1's tutorial points at when it
    /// teaches rotating a device.</summary>
    public RectTransform ButtonsRect => rootRt;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        font = Resources.Load<TMP_FontAsset>(HudIconButton.FontResourcePath);
        Build();
        root.SetActive(false);
    }

    void Update()
    {
        var dd = DragDropManager.Instance;
        bool wanted = dd != null && dd.CanRotateHighlighted;

        if (wanted)
        {
            NetworkDevice device = dd.HighlightedDevice;
            int angle = Mathf.RoundToInt(device.transform.eulerAngles.y) % 360;
            captionDetail.text = $"{device.deviceName}  {angle}°";
            if (!root.activeSelf) root.SetActive(true);
        }

        alpha = Mathf.MoveTowards(alpha, wanted ? 1f : 0f, Time.unscaledDeltaTime / FadeDuration);
        group.alpha = alpha;
        group.blocksRaycasts = wanted;
        rootRt.anchoredPosition = new Vector2(0f, ViewControlsHud.Margin - (1f - alpha) * RiseDistance);

        // Fully off once faded, so it stops counting as covering the screen
        // (OccupiesScreen follows the active state).
        if (!wanted && alpha <= 0f && root.activeSelf) root.SetActive(false);
    }

    // ── Tooltips ─────────────────────────────────────────────────────────────

    // Named for what the player sees: clockwise (seen from above) swings the
    // device's camera-facing side to the left, the same way ViewControlsHud's
    // left turn button spins the scene.
    static TooltipContent RotateTooltip(bool clockwise) => new TooltipContent
    {
        Title  = clockwise ? "Rotate left" : "Rotate right",
        Tag    = $"{DragDropManager.RotateStepDegrees:0}°",
        Body   = $"Turns the selected device {DragDropManager.RotateStepDegrees:0}° " +
                 (clockwise ? "clockwise" : "counter-clockwise") +
                 $" per click (its front swings to the {(clockwise ? "left" : "right")}), so its ports can face the device you're cabling it to.",
        Footer = "For any angle in between: hold R and scroll the mouse wheel.",
    };

    // ── Construction ─────────────────────────────────────────────────────────

    void Build()
    {
        var canvas = HudIconButton.CreateCanvas("DeviceRotate_Canvas", transform);

        root = HudIconButton.NewRect("DeviceRotate", canvas.transform);
        rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0f);
        rootRt.pivot = new Vector2(0.5f, 0f);
        rootRt.anchoredPosition = new Vector2(0f, ViewControlsHud.Margin);
        group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        var row = root.AddComponent<HorizontalLayoutGroup>();
        row.spacing = Spacing;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = row.childControlHeight = false;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        var fit = root.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ↻ on the left, ↺ on the right — the same order and glyphs as
        // ViewControlsHud's turn buttons. Seen from the game's overhead camera,
        // Unity's +Y turn is clockwise, which swings the device's near side
        // left: so the left button turns +Y, the right one −Y.
        HudIconButton.Create(root.transform, "RotateLeft", font, ViewControlIconBuilder.GetTurnSprite(false), null, null,
            () => DragDropManager.Instance?.RotateHighlighted(1), () => RotateTooltip(true), out _);
        BuildCaption(root.transform);
        HudIconButton.Create(root.transform, "RotateRight", font, ViewControlIconBuilder.GetTurnSprite(true), null, null,
            () => DragDropManager.Instance?.RotateHighlighted(-1), () => RotateTooltip(false), out _);

        OccupiesScreen.Add(root, OccupantLayer.Hud);
    }

    // What the buttons act on, between them: "ROTATE" over "PC  90°".
    void BuildCaption(Transform parent)
    {
        var cap = HudIconButton.NewRect("Caption", parent);
        ((RectTransform)cap.transform).sizeDelta = new Vector2(CaptionWidth, HudIconButton.Size);
        var img = cap.AddComponent<Image>();
        img.sprite = HudIconButton.LabelPlate();
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;

        var title = HudIconButton.NewText("Title", cap.transform, font, 10f, HudIconButton.LabelBorder);
        title.text = "ROTATE";
        title.alignment = TextAlignmentOptions.Center;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0f, 0.5f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = new Vector2(6f, 0f);
        trt.offsetMax = new Vector2(-6f, -6f);

        captionDetail = HudIconButton.NewText("Detail", cap.transform, font, 13f, Color.white);
        captionDetail.fontStyle = FontStyles.Bold;
        captionDetail.alignment = TextAlignmentOptions.Center;
        captionDetail.overflowMode = TextOverflowModes.Ellipsis;
        var drt = captionDetail.rectTransform;
        drt.anchorMin = new Vector2(0f, 0f);
        drt.anchorMax = new Vector2(1f, 0.5f);
        drt.offsetMin = new Vector2(6f, 6f);
        drt.offsetMax = new Vector2(-6f, 0f);
    }
}
