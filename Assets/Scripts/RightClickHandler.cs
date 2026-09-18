using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

/// <summary>
/// Sits alongside a Button on the same GameObject and fires onRightClick for
/// right-button clicks — Button itself only reacts to the left mouse button
/// (see Selectable/Button.OnPointerClick), so this is the minimal add-on
/// needed for a button that wants a distinct right-click action (see
/// NetworkUIManager's devices/cable compound button and its flyout).
/// </summary>
public class RightClickHandler : MonoBehaviour, IPointerClickHandler
{
    public UnityAction onRightClick;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
            onRightClick?.Invoke();
    }
}
