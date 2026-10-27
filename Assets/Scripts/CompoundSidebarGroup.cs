using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generic "one visible sidebar button that flips through several tools"
/// widget — used by NetworkUIManager for both the devices/cable button
/// (PC/Switch/Router/Cable) and the remove button (Remove Device/Remove
/// Cable/Remove Table). One tool at a time sits in the sidebar's own layout
/// list as "the face" (left-click fires that tool's own action, wired by the
/// caller before Register()); right-click, or clicking the pixel arrow tab
/// just outside the face button (see FlyoutArrowTab), reveals the other tools
/// as a horizontal flyout that flares past the sidebar's right edge into the play
/// area, each one scaling in with a short staggered ease-out-back "trail".
/// Clicking a flyout tool swaps it in as the new face and closes the flyout.
///
/// TKind is the caller's own enum identifying each tool. Register() must be
/// called once per tool, in the order the caller wants it offered in the
/// flyout, with the button's own domain action already wired to its
/// onClick — this class only adds the face-swap/right-click glue on top.
/// </summary>
public class CompoundSidebarGroup<TKind> where TKind : struct, Enum
{
    public readonly struct Entry
    {
        public readonly Button Btn;
        public readonly SidebarIconButtonFX Fx;
        public readonly RectTransform Rt;
        public Entry(Button btn, SidebarIconButtonFX fx, RectTransform rt) { Btn = btn; Fx = fx; Rt = rt; }
    }

    const float FlyoutGap = 16f;   // gap between the face button's own edge and the first flyout button (matches inter-item spacing)
    const float FlyoutItemDuration = 0.22f;
    const float FlyoutStagger = 0.07f;

    readonly MonoBehaviour host;
    readonly RectTransform hudCanvasRect;
    readonly List<TKind> order = new();
    readonly Dictionary<TKind, Entry> tools = new();
    static readonly EqualityComparer<TKind> Cmp = EqualityComparer<TKind>.Default;

    // "More tools this way" pixel arrow just outside the face button's plate,
    // level with its vertical middle (see FlyoutArrowTab) — a discoverable,
    // left-clickable alternative to the right-click flyout gesture (which a
    // player who's never used a right-click context menu before has no
    // reason to try). Not part of any single tool's own button, so it
    // survives face swaps: SelectFace reparents it onto whichever tool is
    // newly visible.
    const float ArrowGap = 4f; // between the plate's right edge and the arrow's framed edge
    FlyoutArrowTab arrow;
    // The tab rides its face button's icon alpha (see LateTick), so it
    // arrives with the button's HUD reveal flash and leaves with its exit
    // fade instead of sitting there through the whole level entrance/exit.
    Image faceIcon;

    Transform listParent;
    Transform poolParent;
    TKind face;
    bool guided; // true once an external caller (tutorial/guide) is managing this group — disables the flyout
    RectTransform flyoutRowRt;
    GameObject flyoutBackdropGo;
    Coroutine flyoutAnimRoutine;
    bool closing; // true while AnimateFlyoutOut is retracting items back into the face button

    public bool FlyoutOpen => flyoutRowRt != null;

    /// <summary>True once the flyout is open AND its pop-in animation has
    /// finished settling every item at its resting scale=1 (i.e. neither
    /// AnimateFlyoutIn nor AnimateFlyoutOut is currently running). An
    /// external caller that wants to attach a CONTINUOUS scale-driving
    /// effect to a flyout item (e.g. TutorialManager's PulsingHighlight —
    /// see FlyoutRevealGate) must wait for this rather than FlyoutOpen: that
    /// effect captures the item's scale at the moment it attaches, and
    /// attaching mid-animation would capture a near-zero in-progress scale,
    /// permanently freezing the item shrunk instead of at its real size.</summary>
    public bool FlyoutSettled => FlyoutOpen && flyoutAnimRoutine == null && !closing;
    public TKind Face => face;
    public Button FaceButton => tools[face].Btn;
    public RectTransform FaceRect => tools[face].Rt;
    public SidebarIconButtonFX FaceFx => tools[face].Fx;

    /// <summary>The RectTransform of one specific registered tool, stable
    /// regardless of whether it's currently the face (in listParent) or
    /// pooled/parked in the flyout row — same object either way, just
    /// reparented (see SelectFace/OpenFlyout). Lets an external caller (e.g.
    /// a tutorial step) point a highlight at a tool that ISN'T the current
    /// face, unlike FaceRect.</summary>
    public RectTransform GetToolRect(TKind kind) => tools.TryGetValue(kind, out var e) ? e.Rt : null;

    /// <summary>The flyout's arrow tab beside the face button (null for a
    /// single-tool group, which has no flyout). Same object across face
    /// swaps, so a tutorial step can keep pointing at it.</summary>
    public RectTransform ArrowRect => arrow != null ? arrow.Rect : null;

    public CompoundSidebarGroup(MonoBehaviour host, RectTransform hudCanvasRect)
    {
        this.host = host;
        this.hudCanvasRect = hudCanvasRect;
    }

    /// <summary>Registers one tool — call once per tool, before Build(). btn should
    /// already have its own domain action wired (StartDragging/BeginRemoving/...);
    /// this adds the face-swap-on-click and right-click-opens-flyout behavior on top.</summary>
    public void Register(TKind kind, Button btn, SidebarIconButtonFX fx)
    {
        order.Add(kind);
        tools[kind] = new Entry(btn, fx, btn.transform as RectTransform);

        btn.onClick.AddListener(() => SelectFace(kind));

        // Right-click is this button's second way of being pressed, so it
        // clicks like a left-click does (UIButtonSounds only hears onClick,
        // which is left-button only).
        var relay = btn.gameObject.AddComponent<RightClickHandler>();
        relay.onRightClick = () =>
        {
            AudioManager.Instance?.PlaySfx(SfxId.UIClick);
            ToggleFlyout();
        };
    }

    // Sibling index (within listParent) this group's face slot occupies —
    // captured at Build() time from wherever the initial face naturally sits
    // (its construction order), NOT hardcoded to 0: a group built first in
    // the sidebar (devices/cable) naturally anchors at 0, but one built after
    // other rows already exist (remove) needs to anchor further down and stay
    // there — SelectFace restores exactly this index whenever a different
    // tool swaps in as the new face, so the group's row never jumps position.
    int anchorSiblingIndex;

    /// <summary>Call once after every tool is Register()ed: initialFace becomes the
    /// visible sidebar entry (parented into listParent), everything else parks in
    /// an inactive pool until the flyout borrows it.</summary>
    public void Build(Transform listParent, TKind initialFace)
    {
        this.listParent = listParent;

        var poolGo = new GameObject(typeof(TKind).Name + "_Pool", typeof(RectTransform));
        poolGo.transform.SetParent(host.transform, false);
        poolGo.SetActive(false);
        poolParent = poolGo.transform;

        face = initialFace;
        anchorSiblingIndex = tools[face].Rt.GetSiblingIndex();

        foreach (var kind in order)
        {
            if (Cmp.Equals(kind, face)) continue;
            var e = tools[kind];
            e.Rt.SetParent(poolParent, false);
            e.Btn.gameObject.SetActive(false);

            // CreateSidebarButton starts every icon at alpha 0, expecting
            // NetworkUIManager's sidebar-wide reveal-flash sweep
            // (QueueRevealFlash) to bring it back to 1 — but that sweep only
            // walks descendants of the visible sidebar list, and pooling a
            // tool here reparents it out of that list. A pooled tool never
            // plays the flash itself (the flyout has its own pop-in
            // animation when it's picked), so without this its icon would
            // stay invisible forever — the flyout would show only the
            // glow/spark flash on click, never the actual artwork.
            var icon = e.Rt.Find("Icon")?.GetComponent<Image>();
            if (icon != null)
            {
                var c = icon.color;
                icon.color = new Color(c.r, c.g, c.b, 1f);
            }
        }

        if (order.Count > 1) BuildArrow();
    }

    void BuildArrow()
    {
        arrow = FlyoutArrowTab.Create(ToggleFlyout);
        AttachArrowToFace();
    }

    // A child of the face button, not a sibling in the sidebar list, so it
    // follows the button's own hover scale and layout. It's the button's
    // last child, so a click on the tab hits the tab's own Button first and
    // never falls through to the face's own click behind it (the face's
    // raycastable Icon spans the transparent margin the tab sits in).
    void AttachArrowToFace()
    {
        if (arrow == null) return;
        var arrowRt = arrow.Rect;
        arrowRt.SetParent(tools[face].Rt, false);
        arrowRt.SetAsLastSibling();

        // Sits just past the plate's own right edge, level with its vertical
        // middle, rather than the button rect's: the rect also spans the
        // transparent margin and the label stem baked beneath the plate, so
        // its edge is too far out and its center too low. Re-measured per
        // face since each tool's icon sprite differs.
        var icon = tools[face].Rt.Find("Icon")?.GetComponent<Image>();
        faceIcon = icon;
        Rect plate = icon != null ? SpriteContentBounds.GetPlate(icon.sprite) : new Rect(0f, 0f, 1f, 1f);
        var anchor = new Vector2(plate.xMax, plate.center.y);
        arrowRt.anchorMin = anchor;
        arrowRt.anchorMax = anchor;
        arrowRt.anchoredPosition = new Vector2(ArrowGap + FlyoutArrowTab.VisualSize.x * 0.5f, 0f);
    }

    /// <summary>Swaps the sidebar's visible entry to kind (no-op if it already is
    /// one, aside from closing a stray flyout) — called both by a tool button's own
    /// click (via Register's listener) and externally by SetLock for a forced tool.</summary>
    public void SelectFace(TKind kind)
    {
        if (Cmp.Equals(kind, face)) { CloseFlyout(); return; }

        var old = tools[face];
        old.Rt.SetParent(poolParent, false);
        old.Btn.gameObject.SetActive(false);

        face = kind;
        var next = tools[kind];
        next.Rt.SetParent(listParent, false);
        next.Rt.SetSiblingIndex(anchorSiblingIndex);
        next.Rt.localScale = Vector3.one; // guards against SelectFace landing mid-flyout-close-animation (see AnimateFlyoutOut), which would otherwise leave the new face stuck at a shrunk scale
        next.Btn.gameObject.SetActive(true);
        AttachArrowToFace();

        CloseFlyout();
    }

    void ToggleFlyout()
    {
        if (guided || closing) return;
        if (FlyoutOpen) CloseFlyout();
        else OpenFlyout();
    }

    void OpenFlyout()
    {
        // Full-screen invisible catch-all — a click anywhere outside the
        // flyout's own buttons closes it (this also naturally covers "click
        // the face button again" and "click elsewhere in the game").
        flyoutBackdropGo = new GameObject("FlyoutBackdrop", typeof(RectTransform));
        flyoutBackdropGo.transform.SetParent(hudCanvasRect, false);
        var backdropImg = flyoutBackdropGo.AddComponent<Image>();
        backdropImg.color = new Color(0f, 0f, 0f, 0f);
        var bdRt = flyoutBackdropGo.GetComponent<RectTransform>();
        bdRt.anchorMin = Vector2.zero;
        bdRt.anchorMax = Vector2.one;
        bdRt.offsetMin = Vector2.zero;
        bdRt.offsetMax = Vector2.zero;
        var backdropBtn = flyoutBackdropGo.AddComponent<Button>();
        backdropBtn.transition    = Selectable.Transition.None;
        backdropBtn.targetGraphic = backdropImg;
        backdropBtn.onClick.AddListener(CloseFlyout);
        flyoutBackdropGo.AddComponent<SilentUIButton>(); // not a button to the player — no hover/click sound

        // The backdrop is full-screen and sits on top of everything else under
        // hudCanvasRect while open — including the face button itself — so a
        // second right-click on the face button would otherwise be swallowed by
        // the backdrop's own raycast (Button.onClick only ever fires for the
        // left mouse button) and never reach the face button's RightClickHandler
        // underneath. Giving the backdrop its own right-click handler makes a
        // right-click ANYWHERE while open close the flyout, face button included.
        var backdropRelay = flyoutBackdropGo.AddComponent<RightClickHandler>();
        backdropRelay.onRightClick = CloseFlyout;

        var rowGo = new GameObject("Flyout", typeof(RectTransform));
        rowGo.transform.SetParent(hudCanvasRect, false);
        flyoutRowRt = rowGo.GetComponent<RectTransform>();
        flyoutRowRt.anchorMin = new Vector2(0f, 0.5f);
        flyoutRowRt.anchorMax = new Vector2(0f, 0.5f);
        flyoutRowRt.pivot     = new Vector2(0f, 0.5f);

        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment         = TextAnchor.MiddleLeft;
        layout.childControlWidth      = false;
        layout.childControlHeight     = false;
        layout.childForceExpandWidth  = false;
        layout.childForceExpandHeight = false;
        layout.spacing                = 16f; // matches the sidebar's own row spacing (see NetworkUIManager.BuildSidebar) so hover scale-up doesn't touch neighbors here either

        var fitter = rowGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var items = new List<Entry>();
        foreach (var kind in order)
        {
            if (Cmp.Equals(kind, face)) continue;
            var e = tools[kind];
            e.Btn.interactable = true;
            e.Btn.gameObject.SetActive(true);
            e.Rt.SetParent(rowGo.transform, false);
            items.Add(e);
        }

        UpdatePosition();

        if (flyoutAnimRoutine != null) host.StopCoroutine(flyoutAnimRoutine);
        flyoutAnimRoutine = host.StartCoroutine(AnimateFlyoutIn(items));
    }

    /// <summary>Retracts the still-open flyout items back into the face button
    /// (mirror of the opening trail, in reverse order — farthest item first) before
    /// actually tearing the flyout down. Safe to call again while already closing
    /// (no-ops via the `closing` guard) or right after a SelectFace, which has
    /// already reparented the newly-chosen face out of the row by this point.</summary>
    public void CloseFlyout()
    {
        if (!FlyoutOpen || closing) return;
        if (flyoutAnimRoutine != null) { host.StopCoroutine(flyoutAnimRoutine); flyoutAnimRoutine = null; }

        // Closes to input immediately — only the visual retraction lingers.
        if (flyoutBackdropGo != null) { UnityEngine.Object.Destroy(flyoutBackdropGo); flyoutBackdropGo = null; }

        var items = new List<Entry>();
        foreach (var kind in order)
        {
            if (Cmp.Equals(kind, face)) continue;
            var e = tools[kind];
            e.Btn.interactable = false;
            items.Add(e);
        }

        closing = true;
        var rowGo = flyoutRowRt.gameObject;
        flyoutAnimRoutine = host.StartCoroutine(AnimateFlyoutOut(items, rowGo));
    }

    /// <summary>Call from the host's LateUpdate (not Update) — the sidebar's
    /// SmoothScrollRect also moves its content in LateUpdate, so recomputing here
    /// keeps the flyout glued to the face button instead of lagging a frame behind
    /// while the list glides from a mouse-wheel scroll.</summary>
    public void LateTick()
    {
        if (FlyoutOpen) UpdatePosition();

        if (arrow != null)
        {
            arrow.ExternalAlpha = faceIcon != null ? faceIcon.color.a : 1f;
            arrow.SetAvailable(!guided); // a guided step's lock disables the flyout, so the tab goes too
            arrow.SetOpen(FlyoutOpen && !closing);
        }
    }

    void UpdatePosition()
    {
        RectTransform faceRt = tools[face].Rt;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, faceRt.position);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(hudCanvasRect, screenPoint, null, out Vector2 local))
            return;

        // faceRt is top-pivoted (pivot = (0.5, 1), see NetworkUIManager.
        // CreateSidebarButton) so `local` lands on its TOP edge, not its
        // center — correct for both axes so the flyout starts flush with the
        // face button's own right edge and lines up on its vertical center,
        // instead of floating above/right of it.
        float centerY    = local.y - faceRt.rect.height * 0.5f;
        float rightEdgeX = local.x + faceRt.rect.width * 0.5f;

        // `local` is measured from hudCanvasRect's own pivot (its rect center),
        // but flyoutRowRt is anchored to hudCanvasRect's LEFT edge (anchorMin.x
        // = 0) — anchoredPosition.x has to be expressed relative to that anchor
        // point (hudCanvasRect.rect.xMin), not the pivot, otherwise the flyout
        // lands a full half-canvas-width too far left (off-screen instead of
        // flush against the face button's right edge). Y needs no such fix:
        // its anchor is 0.5 too, which already coincides with the pivot-centered
        // frame `local.y` is in.
        float anchoredX = rightEdgeX + FlyoutGap - hudCanvasRect.rect.xMin;
        flyoutRowRt.anchoredPosition = new Vector2(anchoredX, centerY);
    }

    /// <summary>Staggered ease-out-back scale-up for each flyout button in turn —
    /// each one starts FlyoutStagger seconds after the previous, so they pop in
    /// left-to-right like a short trail rather than all at once. Each item also
    /// flashes its own click-punch glow (sparks included) right as it FINISHES
    /// popping in — not the instant it starts, when its rect is still scaled
    /// down near zero: UISparks.EmitBurst spreads spark origins across the
    /// origin rect's current (world) size, so firing it mid-scale-up collapsed
    /// every spark to one condensed point instead of spreading across the
    /// button's face like a normal click's burst does.</summary>
    IEnumerator AnimateFlyoutIn(List<Entry> items)
    {
        foreach (var it in items) it.Rt.localScale = Vector3.zero;
        var glowed = new bool[items.Count];

        float maxTime = items.Count > 0 ? (items.Count - 1) * FlyoutStagger + FlyoutItemDuration : 0f;
        float t = 0f;
        while (t < maxTime)
        {
            t += Time.unscaledDeltaTime;
            for (int i = 0; i < items.Count; i++)
            {
                float delay = i * FlyoutStagger;
                float local = Mathf.Clamp01((t - delay) / FlyoutItemDuration);
                items[i].Rt.localScale = Vector3.one * EaseOutBack(local);
                if (!glowed[i] && local >= 1f)
                {
                    glowed[i] = true;
                    items[i].Fx?.PlayClickPunch();
                }
            }
            yield return null;
        }
        foreach (var it in items) it.Rt.localScale = Vector3.one;
        flyoutAnimRoutine = null;
    }

    /// <summary>Mirror of AnimateFlyoutIn, run on CloseFlyout: shrinks items back
    /// to zero in reverse order (the one farthest from the face button leads, so
    /// the trail reads as retracting back toward it) before the row/backdrop are
    /// actually torn down.</summary>
    IEnumerator AnimateFlyoutOut(List<Entry> items, GameObject rowGo)
    {
        int n = items.Count;
        float maxTime = n > 0 ? (n - 1) * FlyoutStagger + FlyoutItemDuration : 0f;
        float t = 0f;
        while (t < maxTime)
        {
            t += Time.unscaledDeltaTime;
            for (int i = 0; i < n; i++)
            {
                // Skip anything no longer actually in the row — e.g. SetLock
                // forced a new face mid-retraction (SelectFace reparents it
                // into listParent right away); leave that one alone instead of
                // continuing to shrink or later pool a button that's now the
                // visible face.
                if (items[i].Rt.parent != rowGo.transform) continue;
                float delay = (n - 1 - i) * FlyoutStagger;
                float local = Mathf.Clamp01((t - delay) / FlyoutItemDuration);
                items[i].Rt.localScale = Vector3.one * (1f - EaseInBack(local));
            }
            yield return null;
        }

        foreach (var e in items)
        {
            if (e.Rt.parent != rowGo.transform) continue;
            e.Btn.gameObject.SetActive(false);
            e.Rt.SetParent(poolParent, false);
            e.Rt.localScale = Vector3.one;
        }
        UnityEngine.Object.Destroy(rowGo);
        flyoutRowRt = null;
        closing = false;
        flyoutAnimRoutine = null;
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    /// <summary>Mirror of EaseOutBack for the reverse (closing) direction — a
    /// slight overshoot past 0 right as each item starts retracting, then a
    /// smooth accelerating pull the rest of the way to fully shrunk.</summary>
    static float EaseInBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return c3 * t * t * t - c1 * t * t;
    }

    /// <summary>External lock, e.g. from a tutorial/guide step: forcedTool set forces
    /// the face to that tool and disables the flyout; null just disables the flyout.
    /// Doesn't touch the button's own interactable/dim state — the caller is
    /// expected to layer SidebarButtonLock on FaceButton for that, same as before.</summary>
    public void SetLock(TKind? forcedTool)
    {
        guided = true;
        CloseFlyout();
        if (forcedTool.HasValue && !Cmp.Equals(forcedTool.Value, face))
            SelectFace(forcedTool.Value);
    }

    /// <summary>Shows the face button's lit artwork while one of this group's
    /// tools is in use (see SidebarIconButtonFX.SetForcedActive), and clears it
    /// on every other tool. Lighting only the face used to leave a tool lit
    /// forever once it was swapped out mid-mode (Remove Cable active, then
    /// Remove Device picked from the flyout) — it came back looking active the
    /// next time the flyout opened.</summary>
    public void SetFaceLit(bool lit)
    {
        foreach (var kind in order)
            tools[kind].Fx?.SetForcedActive(lit && Cmp.Equals(kind, face));
    }

    /// <summary>Leaves the group fully free — any tool reachable, flyout included.</summary>
    public void SetUnrestricted()
    {
        guided = false;
        CloseFlyout();
    }
}
