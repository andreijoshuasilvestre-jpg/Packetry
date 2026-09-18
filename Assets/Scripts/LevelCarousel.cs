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

    public void OnBeginDrag(PointerEventData eventData) => dragging = true;
    public void OnEndDrag(PointerEventData eventData)   => dragging = false;

    void LateUpdate()
    {
        if (scrollRect == null || scrollRect.viewport == null || scrollRect.content == null || cards.Count == 0)
            return;

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
        if (dragging || nearest == null) return;
        if (scrollRect.velocity.sqrMagnitude >= snapVelocityThreshold * snapVelocityThreshold) return;
        if (scrollRect is SmoothScrollRect smooth && smooth.IsGliding) return;

        float nearestCenterY = nearest.TransformPoint(nearest.rect.center).y;
        float worldDelta = viewportCenterY - nearestCenterY;
        if (Mathf.Abs(worldDelta) <= 0.5f) return;

        float contentScaleY = scrollRect.content.lossyScale.y;
        float localDelta = Mathf.Approximately(contentScaleY, 0f) ? worldDelta : worldDelta / contentScaleY;

        Vector2 pos = scrollRect.content.anchoredPosition;
        pos.y += localDelta * Time.unscaledDeltaTime * snapSpeed;
        scrollRect.content.anchoredPosition = pos;
        scrollRect.velocity = Vector2.zero;
    }
}
