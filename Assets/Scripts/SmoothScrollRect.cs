using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drop-in ScrollRect replacement that eases mouse-wheel scrolling. Stock
/// ScrollRect.OnScroll snaps content straight to each wheel step, which reads
/// as jumpy — especially at a punchy scrollSensitivity like the sidebar's.
/// This computes the exact same target position but glides to it with
/// SmoothDamp instead of teleporting. Drag-and-release is untouched — that
/// already has its own built-in inertia via the base class.
/// </summary>
public class SmoothScrollRect : ScrollRect
{
    [SerializeField] private float smoothTime = 0.12f; // seconds to glide to the new target after a wheel step

    Vector2 targetPosition;
    Vector2 dampVelocity;
    bool    gliding;

    // Read by LevelCarousel so its own "snap nearest card to center" pull
    // stays out of the way while a wheel-scroll glide is in progress —
    // without this it has no way to tell a wheel glide apart from the list
    // just sitting still, and re-centers on the still-nearest card mid-glide,
    // fighting (and effectively cancelling) every wheel step.
    public bool IsGliding => gliding;

    public override void OnScroll(PointerEventData data)
    {
        if (!IsActive() || content == null || viewport == null) return;

        // Same delta handling as the stock implementation (wheel "down" is
        // positive, UI "up" is positive, hence the flip) — only the "snap
        // straight to position" part below is replaced with a glide target.
        Vector2 delta = data.scrollDelta;
        delta.y *= -1f;
        if (vertical && !horizontal)
        {
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                delta.y = delta.x;
            delta.x = 0;
        }
        if (horizontal && !vertical)
        {
            if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
                delta.x = delta.y;
            delta.y = 0;
        }

        if (!gliding) targetPosition = content.anchoredPosition;
        targetPosition += delta * scrollSensitivity;

        // EnsureLayoutHasRebuilt/CalculateOffset are private on the base class,
        // so bounds are derived here instead — content is top-pivoted and
        // stretched to the viewport's width, so this is just how far its top
        // edge can travel before either end of the list hits the viewport edge.
        if (movementType == MovementType.Clamped)
        {
            if (vertical)
            {
                float rangeY = Mathf.Max(0f, content.rect.height - viewport.rect.height);
                targetPosition.y = Mathf.Clamp(targetPosition.y, 0f, rangeY);
            }
            if (horizontal)
            {
                float rangeX = Mathf.Max(0f, content.rect.width - viewport.rect.width);
                targetPosition.x = Mathf.Clamp(targetPosition.x, -rangeX, 0f);
            }
        }

        gliding = true;
        StopMovement(); // cancel any residual drag inertia so it doesn't fight the glide
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        gliding = false; // hand control back to the base class's own drag/inertia handling
        base.OnBeginDrag(eventData);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (!gliding) return;

        content.anchoredPosition = Vector2.SmoothDamp(content.anchoredPosition, targetPosition, ref dampVelocity, smoothTime);
        if ((content.anchoredPosition - targetPosition).sqrMagnitude < 0.01f)
        {
            content.anchoredPosition = targetPosition;
            gliding      = false;
            dampVelocity = Vector2.zero;
        }
    }
}
