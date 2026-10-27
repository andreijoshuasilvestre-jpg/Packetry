using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drives the level-select carousel's visual language: cards nearer the
/// viewport's vertical center are scaled up and brightened, cards further
/// away shrink and fade, and the list glides the nearest card back to dead
/// center once the user stops dragging or wheel-scrolling. Works with any
/// ScrollRect (including SmoothScrollRect) whose content holds one child
/// RectTransform per card — cards register themselves via Register() as
/// they're built, so LevelSelectManager can add more levels later without
/// this component changing at all.
/// </summary>
public class LevelCarousel : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    [SerializeField] ScrollRect scrollRect;

    [Header("Focus falloff")]
    [SerializeField] float focusScale = 1f;
    [SerializeField] float edgeScale  = 0.74f;
    [SerializeField] float focusAlpha = 1f;
    [SerializeField] float edgeAlpha  = 0.35f;
    [Tooltip("Distance (px) from the viewport center at which a card reaches edgeScale/edgeAlpha.")]
    [SerializeField] float falloff = 220f;

    [Header("Snap")]
    [SerializeField] float snapSpeed = 12f;
    [SerializeField] float snapVelocityThreshold = 40f;

    readonly List<RectTransform> cards  = new List<RectTransform>();
    readonly List<CanvasGroup>   groups = new List<CanvasGroup>();

    bool dragging;
    Coroutine glideRoutine; // GlideTo in progress — owns the scroll position, so the snap below stands down

    public void Configure(ScrollRect rect) => scrollRect = rect;

    /// <summary>Call once per card as it's built.</summary>
    public void Register(RectTransform card, CanvasGroup group)
    {
        cards.Add(card);
        groups.Add(group);
    }

    /// <summary>Drops all registered cards — call before rebuilding the card
    /// list (see LevelSelectManager.RefreshCards) so a rebuild doesn't pile up
    /// stale/destroyed entries alongside the freshly-registered ones.</summary>
    public void Clear()
    {
        cards.Clear();
        groups.Clear();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = true;
        StopGlide(); // the player's own drag always wins over a scripted glide
    }

    public void OnEndDrag(PointerEventData eventData) => dragging = false;

    void LateUpdate()
    {
        if (scrollRect == null || scrollRect.viewport == null || scrollRect.content == null || cards.Count == 0)
            return;
        // Mid zoom-in/out, near scale 0 — the world-space distances below are
        // noise then, and would snap the list toward whichever card they
        // happen to pick (see SmoothScrollRect.IsCollapsed).
        if (scrollRect is SmoothScrollRect collapsing && collapsing.IsCollapsed) return;

        float viewportCenterY = scrollRect.viewport.TransformPoint(scrollRect.viewport.rect.center).y;

        RectTransform nearest = null;
        float nearestDist = float.MaxValue;

        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (card == null) continue;

            float cardCenterY = card.TransformPoint(card.rect.center).y;
            float dist = Mathf.Abs(viewportCenterY - cardCenterY);
            float t = falloff > 0f ? Mathf.Clamp01(dist / falloff) : 0f;

            float scale = Mathf.Lerp(focusScale, edgeScale, t);
            card.localScale = new Vector3(scale, scale, 1f);
            if (groups[i] != null) groups[i].alpha = Mathf.Lerp(focusAlpha, edgeAlpha, t);

            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = card;
            }
        }

        // Glide the closest card back to dead center once motion has settled.
        // Skipped while the user is actively dragging, drag-inertia is still
        // fast, or a SmoothScrollRect wheel-glide is in progress (that glide
        // doesn't drive ScrollRect.velocity at all, so the threshold check
        // below can't see it on its own) — SmoothScrollRect owns that phase,
        // so the two motions never fight each other.
        if (dragging || glideRoutine != null || nearest == null) return;
        if (scrollRect.velocity.sqrMagnitude >= snapVelocityThreshold * snapVelocityThreshold) return;
        if (scrollRect is SmoothScrollRect smooth && smooth.IsGliding) return;

        float delta = OffsetToCenter(nearest);
        if (Mathf.Abs(delta) <= 0.5f) return;

        // Exponential ease rather than a raw delta * dt * snapSpeed step,
        // which overshoots on any frame long enough that dt * snapSpeed > 1.
        Vector2 pos = scrollRect.content.anchoredPosition;
        pos.y += delta * (1f - Mathf.Exp(-snapSpeed * Time.unscaledDeltaTime));
        scrollRect.content.anchoredPosition = pos;
        scrollRect.velocity = Vector2.zero;
    }

    /// <summary>
    /// Jumps (no glide) so `card` sits on the viewport's center, or as close
    /// as the scroll range allows. Forces a layout pass first, so it's safe
    /// to call in the same frame the cards were (re)built — the carousel must
    /// already be active in the hierarchy, or that pass won't see them.
    /// </summary>
    public void CenterOn(RectTransform card)
    {
        if (scrollRect == null || scrollRect.viewport == null || scrollRect.content == null || card == null)
            return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
        scrollRect.StopMovement();
        if (scrollRect is SmoothScrollRect smooth) smooth.CancelGlide();

        Vector2 pos = scrollRect.content.anchoredPosition;
        pos.y += OffsetToCenter(card);
        scrollRect.content.anchoredPosition = pos;
    }

    /// <summary>
    /// Eases (ease-in-out) from wherever the list is now to `card` sitting
    /// on the viewport's center, or as close as the scroll range allows —
    /// CenterOn's animated counterpart, for a scripted scroll the player
    /// watches rather than drives (see LevelSelectManager.Celebrate). Poll
    /// IsGliding to wait for it — it also goes false if a drag or StopGlide()
    /// cuts the glide short.
    /// </summary>
    public void GlideTo(RectTransform card, float duration)
    {
        StopGlide();
        if (scrollRect == null || scrollRect.viewport == null || scrollRect.content == null || card == null)
            return;
        glideRoutine = StartCoroutine(GlideRoutine(card, duration));
    }

    public bool IsGliding => glideRoutine != null;

    public void StopGlide()
    {
        if (glideRoutine == null) return;
        StopCoroutine(glideRoutine);
        glideRoutine = null;
    }

    IEnumerator GlideRoutine(RectTransform card, float duration)
    {
        scrollRect.StopMovement();
        if (scrollRect is SmoothScrollRect smooth) smooth.CancelGlide();

        // Card positions within content never change during the glide
        // (focus scaling is localScale only, which layout ignores), so the
        // destination only needs working out once.
        float startY = scrollRect.content.anchoredPosition.y;
        float endY   = startY + OffsetToCenter(card);

        for (float t = 0f; t < duration; )
        {
            t += Time.unscaledDeltaTime;
            SetContentY(Mathf.Lerp(startY, endY, EaseInOutCubic(Mathf.Clamp01(t / duration))));
            yield return null;
        }
        SetContentY(endY);
        glideRoutine = null;
    }

    void SetContentY(float y)
    {
        Vector2 pos = scrollRect.content.anchoredPosition;
        pos.y = y;
        scrollRect.content.anchoredPosition = pos;
        scrollRect.velocity = Vector2.zero;
    }

    static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

    // How far (viewport-local units, + moves content up) the content has to
    // shift to put `card`'s center on the viewport's center. Worked out in
    // local space — content is the viewport's direct child, cards are
    // content's — rather than world space, since the whole panel scales up
    // from exactly 0 during ZoomPanel's pop-in.
    float OffsetToCenter(RectTransform card)
    {
        RectTransform content = scrollRect.content;
        Rect viewRect  = scrollRect.viewport.rect;
        float contentY = content.localPosition.y;
        float scaleY   = content.localScale.y;

        float cardCenterY = contentY + scaleY * (card.localPosition.y + card.rect.center.y * card.localScale.y);
        float delta = viewRect.center.y - cardCenterY;

        // Clamped to the ScrollRect's own range. Without this, a card that
        // can't reach dead center (content too short past it) had the snap
        // pushing content past the end every frame while ScrollRect's Clamped
        // correction pulled it straight back — the stutter at either limit.
        float minDelta = viewRect.yMax - (contentY + scaleY * content.rect.yMax); // content's top edge stays at/above the viewport's top
        float maxDelta = viewRect.yMin - (contentY + scaleY * content.rect.yMin); // content's bottom edge stays at/below the viewport's bottom
        return Mathf.Clamp(delta, minDelta, Mathf.Max(minDelta, maxDelta));
    }
}
