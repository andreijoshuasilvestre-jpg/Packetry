using UnityEngine;

/// <summary>
/// Continuous "look here" beacon that pulses the TARGET button itself — scales
/// its whole RectTransform up and down in place — rather than decorating it
/// with a separate glow sitting behind/around it. Reads as "this button is
/// breathing," which is a much stronger click-me signal than a static button
/// with a soft aura next to it.
///
/// This component renders nothing of its own; it just drives another
/// RectTransform's localScale every frame while active, and restores it
/// exactly on Hide()/re-Attach() so it never leaves a button stuck enlarged.
/// </summary>
public class PulsingHighlight : MonoBehaviour
{
    [SerializeField] private float pulseSpeed = 2.2f;
    [SerializeField] private float minScale = 1.0f;
    [SerializeField] private float maxScale = 1.16f;

    private RectTransform target;
    private Vector3 originalScale = Vector3.one;
    private bool active;

    /// <summary>Points this beacon at a new target, restoring whatever it was
    /// previously pulsing first. Call Show() separately once ready to display.</summary>
    public void Attach(RectTransform newTarget)
    {
        if (target == newTarget) return;
        RestoreCurrent();
        target = newTarget;
        if (target != null) originalScale = target.localScale;
    }

    public void Show() => active = true;

    public void Hide()
    {
        active = false;
        RestoreCurrent();
    }

    void RestoreCurrent()
    {
        if (target != null) target.localScale = originalScale;
    }

    void Update()
    {
        if (!active || target == null) return;
        float t = Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f; // continuous 0..1
        float scale = Mathf.Lerp(minScale, maxScale, t);
        target.localScale = originalScale * scale;
    }
}
