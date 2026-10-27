using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// Bottom-right cluster of view controls: zoom in/out, turn the view
/// left/right, reset it, and open the 2D network diagram (NetworkDiagramPanel).
/// The zoom buttons are for trackpads, which have no wheel to scroll: a press
/// zooms one step, holding keeps zooming. Turning the camera
/// used to exist only as the Q/E keys, which a player had to be told about —
/// now it's a pair of buttons on screen whose arrows show which way the scene
/// will spin, with the keys printed on them as keycaps so keyboard players
/// still pick up the shortcut, and every button explains itself on hover.
/// Buttons are HudIconButton's, shared with DeviceRotateHud.
/// </summary>
public class ViewControlsHud : MonoBehaviour
{
    public static ViewControlsHud Instance { get; private set; }

    const float Spacing      = 10f;
    const float GroupSpacing = 18f;
    const float RevealFade   = 0.35f;
    /// <summary>Gap from the screen edge — DeviceRotateHud sits on the same baseline.</summary>
    public const float Margin = 20f;

    /// <summary>Height (canvas units, up from the bottom edge) this cluster
    /// keeps clear in the bottom-right corner — notification toasts and the
    /// diagram window stack above it instead of on top of it.</summary>
    public static float BottomRightReservedHeight => Margin + HudIconButton.Size + HudIconButton.KeycapOverhang + 8f;

    // Zoom buttons: one step per press, then a steady zoom while held.
    const float ZoomStep      = 4f;    // world units of orbit distance per press
    const float ZoomHoldRate  = 20f;   // world units per second once held
    const float ZoomHoldDelay = 0.3f;  // how long a press must last before it counts as a hold

    TMP_FontAsset font;
    CanvasGroup group;
    RectTransform turnGroupRt, zoomGroupRt;
    Button turnLeft, turnRight, reset, diagram, zoomIn, zoomOut;
    int zoomHeldDirection; // +1 in, -1 out, 0 none
    float zoomHeldSince;
    Image diagramImg;
    bool diagramHovered;

    /// <summary>The two turn buttons together — what Level 1's tutorial points
    /// at when it teaches turning the view.</summary>
    public RectTransform TurnButtonsRect => turnGroupRt;

    /// <summary>The two zoom buttons together — what Level 1's tutorial points
    /// at when it teaches zooming.</summary>
    public RectTransform ZoomButtonsRect => zoomGroupRt;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        font = Resources.Load<TMP_FontAsset>(HudIconButton.FontResourcePath);
        Build();
        StartCoroutine(RevealWithHud());
    }

    // Arrives with the rest of the HUD (see GameBootstrap.EntrySequence) and
    // leaves with it.
    IEnumerator RevealWithHud()
    {
        group.alpha = 0f;
        group.blocksRaycasts = false;
        while (!GameBootstrap.HudRevealStarted) yield return null;
        yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, GameBootstrap.HudButtonsDelay));

        group.blocksRaycasts = true;
        for (float t = 0f; t < RevealFade; t += AnimationSpeed.UnscaledDelta(AnimationKind.Level))
        {
            group.alpha = t / RevealFade;
            yield return null;
        }
        group.alpha = 1f;

        while (!GameBootstrap.ExitStarted) yield return null;
        group.blocksRaycasts = false;
        for (float t = 0f; t < 0.15f; t += AnimationSpeed.UnscaledDelta(AnimationKind.Level))
        {
            group.alpha = 1f - t / 0.15f;
            yield return null;
        }
        group.alpha = 0f;
    }

    void Update()
    {
        var cam = IsometricCamera.Instance;
        bool canTurn  = cam != null && cam.CanRotate;
        bool canReset = cam != null && cam.CanResetView;
        HudIconButton.SetUsable(turnLeft, canTurn);
        HudIconButton.SetUsable(turnRight, canTurn);
        HudIconButton.SetUsable(reset, canReset);
        HudIconButton.SetUsable(zoomIn, cam != null && cam.CanZoomFurther(true));
        HudIconButton.SetUsable(zoomOut, cam != null && cam.CanZoomFurther(false));
        UpdateZoomHold(cam);

        bool open = NetworkDiagramPanel.Instance != null && NetworkDiagramPanel.Instance.IsOpen;
        diagramImg.sprite = open
            ? HudIconButton.Plate(HudIconButton.OnFill, HudIconButton.OnBorder)
            : HudIconButton.Plate(diagramHovered ? HudIconButton.HoverFill : HudIconButton.Fill, HudIconButton.Border);
    }

    // ── Zoom (press = one step, hold = keep going) ───────────────────────────

    void BeginZoomHold(Button button, int direction)
    {
        if (!button.interactable) return;
        IsometricCamera.Instance?.ZoomBy(direction * ZoomStep);
        zoomHeldDirection = direction;
        zoomHeldSince = Time.unscaledTime;
    }

    void EndZoomHold() => zoomHeldDirection = 0;

    void UpdateZoomHold(IsometricCamera cam)
    {
        if (zoomHeldDirection == 0) return;
        // The release can land off the button, or after the cluster faded out —
        // the mouse itself says whether the hold is still on.
        if (cam == null || Mouse.current == null || !Mouse.current.leftButton.isPressed) { EndZoomHold(); return; }
        if (Time.unscaledTime - zoomHeldSince < ZoomHoldDelay) return;
        cam.ZoomBy(zoomHeldDirection * ZoomHoldRate * Time.unscaledDeltaTime);
    }

    // ── Tooltips ─────────────────────────────────────────────────────────────

    const string LockedNote = "Can't turn while a device is selected, being moved, or a panel is open.";

    static TooltipContent TurnTooltip(bool right) => new TooltipContent
    {
        Title  = right ? "Turn view right" : "Turn view left",
        Tag    = right ? "E" : "Q",
        Body   = "Spins the scene around its center so you can see devices, cables and ports from another side. " +
                 (right ? "The near side swings right." : "The near side swings left."),
        Footer = IsometricCamera.Instance != null && !IsometricCamera.Instance.CanRotate
            ? LockedNote
            : $"Shortcut: {(right ? "E" : "Q")} on the keyboard",
    };

    static TooltipContent ResetTooltip() => new TooltipContent
    {
        Title  = "Reset view",
        Body   = "Brings the camera back to the starting angle, zoom and position.",
        Footer = "Move: W A S D  ·  Zoom: mouse wheel or the zoom buttons  ·  Drag the view: hold the middle mouse button",
    };

    static TooltipContent ZoomTooltip(bool zoomIn) => new TooltipContent
    {
        Title  = zoomIn ? "Zoom in" : "Zoom out",
        Body   = zoomIn
            ? "Moves the camera closer, tilting down toward the tables so ports and labels are easier to read."
            : "Pulls the camera back and up for a wider view of the whole network.",
        Footer = "Click for one step, or hold to keep zooming. The mouse wheel (or a two-finger swipe) zooms too.",
    };

    static TooltipContent DiagramTooltip() => new TooltipContent
    {
        Title  = "Network diagram (2D)",
        Body   = "A flat map of your network, like Cisco Packet Tracer's logical view: every device, " +
                 "cable, port and IP address at a glance. It stays open beside the 3D view and updates live.",
        Footer = NetworkDiagramPanel.Instance != null && NetworkDiagramPanel.Instance.IsOpen
            ? "Click again (or press Esc) to close it."
            : "Drag its title bar to move it.",
    };

    // ── Construction ─────────────────────────────────────────────────────────

    void Build()
    {
        var canvas = HudIconButton.CreateCanvas("ViewControls_Canvas", transform);

        var root = HudIconButton.NewRect("ViewControls", canvas.transform);
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = rootRt.anchorMax = new Vector2(1f, 0f);
        rootRt.pivot = new Vector2(1f, 0f);
        rootRt.anchoredPosition = new Vector2(-Margin, Margin);
        group = root.AddComponent<CanvasGroup>();
        var row = root.AddComponent<HorizontalLayoutGroup>();
        row.spacing = GroupSpacing;
        row.childAlignment = TextAnchor.LowerRight;
        row.childControlWidth = row.childControlHeight = false;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        var fit = root.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Zoom first (leftmost) — the pair shares a group, like the turn buttons.
        var zoomGroup = HudIconButton.NewRect("Zoom", root.transform);
        zoomGroupRt = (RectTransform)zoomGroup.transform;
        zoomGroupRt.sizeDelta = new Vector2(HudIconButton.Size * 2f + Spacing, HudIconButton.Size);
        var zoomRow = zoomGroup.AddComponent<HorizontalLayoutGroup>();
        zoomRow.spacing = Spacing;
        zoomRow.childControlWidth = zoomRow.childControlHeight = false;
        zoomRow.childForceExpandWidth = zoomRow.childForceExpandHeight = false;

        zoomOut = BuildZoomButton(zoomGroup.transform, zoomIn: false);
        zoomIn  = BuildZoomButton(zoomGroup.transform, zoomIn: true);

        // The two turn buttons share a group so the tutorial can point at both.
        var turnGroup = HudIconButton.NewRect("Turn", root.transform);
        turnGroupRt = (RectTransform)turnGroup.transform;
        turnGroupRt.sizeDelta = new Vector2(HudIconButton.Size * 2f + Spacing, HudIconButton.Size);
        var turnRow = turnGroup.AddComponent<HorizontalLayoutGroup>();
        turnRow.spacing = Spacing;
        turnRow.childControlWidth = turnRow.childControlHeight = false;
        turnRow.childForceExpandWidth = turnRow.childForceExpandHeight = false;

        turnLeft = HudIconButton.Create(turnGroup.transform, "TurnLeft", font, ViewControlIconBuilder.GetTurnSprite(false), null, "Q",
            () => IsometricCamera.Instance?.RotateStep(-1), () => TurnTooltip(false), out _);
        turnRight = HudIconButton.Create(turnGroup.transform, "TurnRight", font, ViewControlIconBuilder.GetTurnSprite(true), null, "E",
            () => IsometricCamera.Instance?.RotateStep(1), () => TurnTooltip(true), out _);
        reset = HudIconButton.Create(root.transform, "ResetView", font, ViewControlIconBuilder.GetResetSprite(), null, null,
            () => IsometricCamera.Instance?.ResetView(), ResetTooltip, out _);
        diagram = HudIconButton.Create(root.transform, "Diagram", font, null, "2D", null,
            () => NetworkDiagramPanel.Instance?.Toggle(), DiagramTooltip, out diagramImg);

        // The diagram button's plate is driven by Update (open/hover state),
        // so it tracks hover itself rather than through Create's own swap.
        var trigger = diagram.gameObject.GetComponent<EventTrigger>();
        trigger.triggers.Clear();
        HudIconButton.AddTrigger(trigger, EventTriggerType.PointerEnter, () => diagramHovered = true);
        HudIconButton.AddTrigger(trigger, EventTriggerType.PointerExit,  () => diagramHovered = false);

        OccupiesScreen.Add(root, OccupantLayer.Hud);
    }

    // Zooms on press (and keeps going while held) rather than on click, so
    // its onClick does nothing — the click glow still plays on release.
    Button BuildZoomButton(Transform parent, bool zoomIn)
    {
        int direction = zoomIn ? 1 : -1;
        var button = HudIconButton.Create(parent, zoomIn ? "ZoomIn" : "ZoomOut", font,
            ViewControlIconBuilder.GetZoomSprite(zoomIn), null, null,
            () => { }, () => ZoomTooltip(zoomIn), out _);

        var trigger = button.gameObject.GetComponent<EventTrigger>();
        HudIconButton.AddTrigger(trigger, EventTriggerType.PointerDown, () => BeginZoomHold(button, direction));
        HudIconButton.AddTrigger(trigger, EventTriggerType.PointerUp,   EndZoomHold);
        return button;
    }
}
