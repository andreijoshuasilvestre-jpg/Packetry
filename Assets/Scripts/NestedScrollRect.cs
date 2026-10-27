using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ScrollRect for a list that sits inside another scrollable area (e.g.
/// SettingsPanelManager's Subnet Colors list inside the settings body).
/// Stock ScrollRect swallows every wheel event over it, so once the pointer
/// lands on the inner list the outer area can't be wheel-scrolled any further.
/// This hands the wheel to the nearest parent ScrollRect whenever the inner
/// list has nothing left to scroll in that direction.
/// </summary>
public class NestedScrollRect : ScrollRect
{
    ScrollRect parentScroll;

    public override void OnScroll(PointerEventData data)
    {
        if (parentScroll == null && transform.parent != null)
            parentScroll = transform.parent.GetComponentInParent<ScrollRect>();

        if (parentScroll != null && vertical && content != null && viewport != null)
        {
            float range = content.rect.height - viewport.rect.height;
            float wheel = data.scrollDelta.y; // > 0 = wheel up
            bool atTop    = content.anchoredPosition.y <= 0.5f;
            bool atBottom = content.anchoredPosition.y >= range - 0.5f;
            if (range <= 0.5f || (wheel > 0f && atTop) || (wheel < 0f && atBottom))
            {
                parentScroll.OnScroll(data);
                return;
            }
        }
        base.OnScroll(data);
    }
}
