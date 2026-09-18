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
/// caller before Register()); right-click reveals the other tools as a
/// horizontal flyout that flares past the sidebar's right edge into the play
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

    Transform listParent;
    Transform poolParent;
    TKind face;
    bool guided; // true once an external caller (tutorial/guide) is managing this group — disables the flyout
    RectTransform flyoutRowRt;
    GameObject flyoutBackdropGo;
    Coroutine flyoutAnimRoutine;

    public bool FlyoutOpen => flyoutRowRt != null;
    public TKind Face => face;
    public Button FaceButton => tools[face].Btn;
    public RectTransform FaceRect => tools[face].Rt;
    public SidebarIconButtonFX FaceFx => tools[face].Fx;

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

        var relay = btn.gameObject.AddComponent<RightClickHandler>();
        relay.onRightClick = ToggleFlyout;
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
        }
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
        next.Btn.gameObject.SetActive(true);

        CloseFlyout();
    }

    void ToggleFlyout()
    {
        if (guided) return;
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

        var items = new List<RectTransform>();
        foreach (var kind in order)
        {
            if (Cmp.Equals(kind, face)) continue;
            var e = tools[kind];
            e.Btn.gameObject.SetActive(true);
            e.Rt.SetParent(rowGo.transform, false);
            items.Add(e.Rt);
        }

        UpdatePosition();

        if (flyoutAnimRoutine != null) host.StopCoroutine(flyoutAnimRoutine);
        flyoutAnimRoutine = host.StartCoroutine(AnimateFlyoutIn(items));
    }

    public void CloseFlyout()
    {
        if (!FlyoutOpen) return;
        if (flyoutAnimRoutine != null) { host.StopCoroutine(flyoutAnimRoutine); flyoutAnimRoutine = null; }

        foreach (var kind in order)
        {
            if (Cmp.Equals(kind, face)) continue;
            var e = tools[kind];
            e.Btn.gameObject.SetActive(false);
            e.Rt.SetParent(poolParent, false);
        }

        UnityEngine.Object.Destroy(flyoutRowRt.gameObject);
        flyoutRowRt = null;
        if (flyoutBackdropGo != null) { UnityEngine.Object.Destroy(flyoutBackdropGo); flyoutBackdropGo = null; }
    }

    /// <summary>Call from the host's LateUpdate (not Update) — the sidebar's
    /// SmoothScrollRect also moves its content in LateUpdate, so recomputing here
    /// keeps the flyout glued to the face button instead of lagging a frame behind
    /// while the list glides from a mouse-wheel scroll.</summary>
    public void LateTick()
    {
        if (FlyoutOpen) UpdatePosition();
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
        flyoutRowRt.anchoredPosition = new Vector2(rightEdgeX + FlyoutGap, centerY);
    }

    /// <summary>Staggered ease-out-back scale-up for each flyout button in turn —
    /// each one starts FlyoutStagger seconds after the previous, so they pop in
    /// left-to-right like a short trail rather than all at once.</summary>
    IEnumerator AnimateFlyoutIn(List<RectTransform> items)
    {
        foreach (var it in items) it.localScale = Vector3.zero;

        float maxTime = items.Count > 0 ? (items.Count - 1) * FlyoutStagger + FlyoutItemDuration : 0f;
        float t = 0f;
        while (t < maxTime)
        {
            t += Time.unscaledDeltaTime;
            for (int i = 0; i < items.Count; i++)
            {
                float local = Mathf.Clamp01((t - i * FlyoutStagger) / FlyoutItemDuration);
                items[i].localScale = Vector3.one * EaseOutBack(local);
            }
            yield return null;
        }
        foreach (var it in items) it.localScale = Vector3.one;
        flyoutAnimRoutine = null;
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
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

    /// <summary>Leaves the group fully free — any tool reachable, flyout included.</summary>
    public void SetUnrestricted()
    {
        guided = false;
        CloseFlyout();
    }
}
