using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared show/hide and lock/dim helpers for NetworkUIManager's sidebar
/// buttons — pulled out of TutorialManager so it, Level2Guide, and
/// Level3Guide can all drive the same "only the button relevant to the
/// current step is clickable, everything else is dimmed or hidden" behavior
/// without duplicating the visual logic.
/// </summary>
public static class SidebarButtonLock
{
    public static void SetActive(Button btn, bool active)
    {
        if (btn != null) btn.gameObject.SetActive(active);
    }

    // locked = true dims the button and blocks clicks; false restores full
    // opacity/interactivity. These buttons use Selectable.Transition.None
    // (SidebarIconButtonFX owns hover/click visuals), so interactable alone
    // gives no visual feedback — dim the baked artwork directly instead.
    public static void SetLocked(Button btn, bool locked)
    {
        if (btn == null) return;
        btn.interactable = !locked;
        if (btn.targetGraphic != null)
        {
            Color c = btn.targetGraphic.color;
            c.a = locked ? 0.35f : 1f;
            btn.targetGraphic.color = c;
        }
    }
}
