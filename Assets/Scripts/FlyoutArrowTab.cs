using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The pixel-art arrow tab just outside a CompoundSidebarGroup face button's
/// right edge: a left-clickable way to open the flyout for players who'd
/// never think to right-click. Right-clicking it works too, same as the face
/// button. CompoundSidebarGroup owns placement and state and drives this
/// every frame through SetAvailable/SetOpen/ExternalAlpha. This component only
/// animates.
///
/// Every animation acts on the Glyph child, never on this root, so something
/// else can scale the root without a fight. TutorialManager points
/// PulsingHighlight at the root when it teaches the arrow.
/// </summary>
public class FlyoutArrowTab : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // One logical cell of the arrow = this many canvas units, the HUD's usual
    // pixel density (see MarkerGlyphs, StarIconBuilder.DisplaySize).
    public const float CellSize = 3f;

    /// <summary>Canvas-unit size of the framed arrow itself, not the clear
    /// columns either side of it in its square sprite.</summary>
    public static Vector2 VisualSize =>
        new Vector2(FlyoutArrowIconBuilder.FramedWidth, FlyoutArrowIconBuilder.GridSize) * CellSize;

    const float HoverNudge      = CellSize; // slides one cell toward the flyout on hover
    const float HoverSpeed      = 14f;      // 1/seconds, exponential ease
    const float RevealDuration  = 0.28f;
    const float HideDuration    = 0.14f;
    const float FlipDuration    = 0.16f;

    RectTransform glyphRt;
    Image glyph;
    CanvasGroup group;

    bool available = true, open, hovered;
    float reveal;         // 0 = hidden, 1 = shown
    float hoverT;         // eased 0..1
    float flipT = 1f;     // 0..1 through a facing swap; 1 = at rest
    bool facingRight = true;

    /// <summary>Multiplied into the tab's alpha. CompoundSidebarGroup feeds
    /// the face button's icon alpha in here, so the tab arrives with the
    /// button's HUD reveal flash, leaves with its exit fade and dims with it
    /// when locked.</summary>
    public float ExternalAlpha { get; set; } = 1f;

    public RectTransform Rect => (RectTransform)transform;

    public static FlyoutArrowTab Create(UnityAction onClick)
    {
        var go = new GameObject("FlyoutArrow", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = VisualSize;

        // Invisible hit area over the whole framed arrow. The glyph's own
        // square rect is wider than the arrow (clear columns), so it doesn't
        // take clicks itself.
        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        var glyphGo = new GameObject("Glyph", typeof(RectTransform));
        glyphGo.transform.SetParent(go.transform, false);
        var glyph = glyphGo.AddComponent<Image>();
        glyph.sprite = FlyoutArrowIconBuilder.GetSprite(pointRight: true, lit: false);
        glyph.raycastTarget = false;
        var grt = (RectTransform)glyphGo.transform;
        grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
        grt.pivot     = new Vector2(0.5f, 0.5f);
        grt.sizeDelta = Vector2.one * FlyoutArrowIconBuilder.GridSize * CellSize;

        var btn = go.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None;
        btn.targetGraphic = hit;
        btn.onClick.AddListener(onClick);

        // Button.onClick is left-button only. UIButtonSounds clicks for that
        // one; this plays the same click for a right-click.
        go.AddComponent<RightClickHandler>().onRightClick = () =>
        {
            AudioManager.Instance?.PlaySfx(SfxId.UIClick);
            onClick?.Invoke();
        };

        var tab = go.AddComponent<FlyoutArrowTab>();
        tab.glyphRt = grt;
        tab.glyph   = glyph;
        tab.group   = go.AddComponent<CanvasGroup>();
        tab.group.alpha = 0f;
        return tab;
    }

    /// <summary>False while the flyout can't open (a guided step has locked
    /// the group to one tool): the tab tucks away and stops taking clicks
    /// rather than offering something that does nothing. Coming back pops it
    /// in again.</summary>
    public void SetAvailable(bool value)
    {
        if (available == value) return;
        available = value;
        if (!value) hovered = false;
    }

    /// <summary>Flips the arrow to point back toward the button while the
    /// flyout is out, and back again once it closes.</summary>
    public void SetOpen(bool value)
    {
        if (open == value) return;
        open = value;
        flipT = 0f;
    }

    public void OnPointerEnter(PointerEventData eventData) { if (available) hovered = true; }
    public void OnPointerExit(PointerEventData eventData) => hovered = false;

    void OnDisable()
    {
        hovered = false;
        hoverT = 0f;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Waits for its button to be fully there: the HUD reveal flash snaps
        // the icon to alpha 1, so the tab pops in right after its button
        // arrives, and tucks away again as the button fades out on exit.
        bool show = available && ExternalAlpha >= 0.99f;
        reveal = Mathf.MoveTowards(reveal, show ? 1f : 0f, dt / (show ? RevealDuration : HideDuration));
        hoverT = Mathf.Lerp(hoverT, hovered ? 1f : 0f, 1f - Mathf.Exp(-HoverSpeed * dt));

        if (flipT < 1f)
        {
            flipT = Mathf.Min(1f, flipT + dt / FlipDuration);
            if (flipT >= 0.5f) facingRight = !open; // swap at the squash's thinnest point
        }

        group.alpha = ExternalAlpha * Mathf.Clamp01(reveal * 1.6f);
        group.blocksRaycasts = show;
        group.interactable   = show;

        // Pops in with a small overshoot, like the flyout's own items, and
        // simply shrinks back out.
        float scale = show ? EaseOutBack(reveal) : reveal;
        float squash = flipT < 1f ? Mathf.Abs(Mathf.Cos(flipT * Mathf.PI)) : 1f;
        glyphRt.localScale = new Vector3(scale * squash, scale, 1f);

        float nudge = hoverT * HoverNudge * (facingRight ? 1f : -1f);
        glyphRt.anchoredPosition = new Vector2(nudge, 0f);

        glyph.sprite = FlyoutArrowIconBuilder.GetSprite(facingRight, lit: hovered);
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }
}
