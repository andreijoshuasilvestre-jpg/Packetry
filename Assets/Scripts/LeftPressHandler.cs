using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Sits alongside a Button on the same GameObject and fires onLeftPress the
/// moment the left mouse button goes DOWN on it — Button.onClick only fires on
/// release (and only if the release lands back on the button), which is too
/// late for a press-and-hold gesture like dragging a device out of the
/// sidebar (see NetworkUIManager's devices compound button and
/// DragDropManager.StartDragging). Respects the Button's own interactable
/// state so a locked/dimmed button can't be pressed through.
/// </summary>
public class LeftPressHandler : MonoBehaviour, IPointerDownHandler
{
    public UnityAction onLeftPress;

    Selectable selectable;

    void Awake() => selectable = GetComponent<Selectable>();

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (selectable != null && !selectable.IsInteractable()) return;
        onLeftPress?.Invoke();
    }
}
