using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Hover and click sounds for every UI Button in the scene, without each UI
/// builder (NetworkUIManager, the popups, the menus, ...) wiring its own —
/// a button built anywhere, now or later, gets them for free. Added
/// alongside AudioManager, so every scene with one has these.
///
/// Hover: the topmost UI element under the mouse, raycast the same way the
/// EventSystem does, is checked each frame for an interactable Button (a
/// label or icon inside one counts). Click: the first time a Button is
/// hovered, UIClick is added to its own onClick — so the sound follows
/// Unity's actual click rules (released over the same button, not dragged
/// off it first) instead of guessing from raw mouse presses.
/// </summary>
public class UIButtonSounds : MonoBehaviour
{
    readonly List<RaycastResult> hits = new();
    readonly HashSet<Button> hooked = new();
    PointerEventData pointer;
    Button hovered;

    void Update()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null || Mouse.current == null) { hovered = null; return; }

        if (pointer == null) pointer = new PointerEventData(eventSystem);
        pointer.position = Mouse.current.position.ReadValue();
        hits.Clear();
        eventSystem.RaycastAll(pointer, hits);

        Button button = hits.Count > 0 ? hits[0].gameObject.GetComponentInParent<Button>() : null;
        if (button != null && (!button.IsInteractable() || button.TryGetComponent<SilentUIButton>(out _))) button = null;

        if (button != null && button != hovered)
        {
            AudioManager.Instance?.PlaySfx(SfxId.UIHover);
            if (hooked.Add(button)) button.onClick.AddListener(PlayClick);
        }
        hovered = button;
    }

    static void PlayClick() => AudioManager.Instance?.PlaySfx(SfxId.UIClick);
}
