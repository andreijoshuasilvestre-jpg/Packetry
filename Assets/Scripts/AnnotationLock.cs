using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// "An annotation is waiting on the player" — true while a floating
/// annotation that only its own button can dismiss is on screen: a tutorial
/// callout step with a Next/Continue button (TutorialUIManager), a concept
/// card (ConceptCardManager), or a warning window (AnnotationWarningManager).
/// A tutorial step that asks the player to go DO something has no button and
/// never locks — the rest of the game has to stay live for that.
///
/// Two layers, since the game reads input both ways:
///   - Polled input (every manager's Update() reading Mouse.current /
///     Keyboard.current directly): folded into InteractionMode.IsSuspended,
///     the same gate the Settings dialog already uses.
///   - uGUI clicks (sidebar, HUD, task panel, CLI): each annotation raises a
///     full-screen blocker (CreateBlocker) on its own canvas, behind itself,
///     so nothing on a lower canvas can be clicked.
///
/// Freezes whatever mode was live rather than cancelling it — the Table Too
/// Small warning relies on table placement still holding its first corner
/// once the player closes it.
/// </summary>
public static class AnnotationLock
{
    // Light enough to keep the level readable behind a tutorial callout, dark
    // enough to say "the rest of the screen is paused" — ConceptCardManager's
    // own full-attention backdrop is darker (0.6).
    const float BlockerDim = 0.3f;
    const float BlockerFadeIn = 0.15f;

    public static bool IsActive =>
        (TutorialUIManager.Instance != null && TutorialUIManager.Instance.IsAwaitingButton) ||
        (ConceptCardManager.Instance != null && ConceptCardManager.Instance.IsOpen) ||
        (AnnotationWarningManager.Instance != null && AnnotationWarningManager.Instance.IsOpen);

    /// <summary>Full-screen click blocker for an annotation's canvas, placed first
    /// so the annotation itself (and anything else on that canvas, like the
    /// tutorial's Skip button) still draws and clicks above it. Starts inactive —
    /// toggle it with SetBlocker.</summary>
    public static GameObject CreateBlocker(Transform canvas)
    {
        var go = new GameObject("InputBlocker", typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        go.transform.SetAsFirstSibling();

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, BlockerDim);
        img.raycastTarget = true;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        go.SetActive(false);
        return go;
    }

    /// <summary>Raises or drops a blocker from CreateBlocker (see DropTextFocus for
    /// what raising it also does).</summary>
    public static void SetBlocker(GameObject blocker, bool on)
    {
        if (blocker == null || blocker.activeSelf == on) return;
        blocker.SetActive(on);
        if (!on) return;

        var img = blocker.GetComponent<Image>();
        img.canvasRenderer.SetAlpha(0f);
        img.CrossFadeAlpha(1f, BlockerFadeIn, true);

        DropTextFocus();
    }

    /// <summary>Takes keyboard focus off whatever text field had it (the CLI
    /// command line, a PC's address field). A blocker stops that field being
    /// clicked back into, but one already focused would otherwise keep taking
    /// typing and Enter straight through the lock. Call when a locking
    /// annotation appears.</summary>
    public static void DropTextFocus()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }
}
