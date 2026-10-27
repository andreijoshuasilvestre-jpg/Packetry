using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Continuous "look here" beacon that pulses the TARGET button itself — scales
/// its whole RectTransform up and down in place — rather than decorating it
/// with a separate glow sitting behind/around it. Reads as "this button is
/// breathing," which is a much stronger click-me signal than a static button
/// with a soft aura next to it.
///
/// Also draws a glowing outline ring just outside the target's own edges,
/// breathing alpha in sync with the scale pulse — the scale pulse alone still
/// went unnoticed/ignored by a first-time player, so this layers on a second,
/// more literal "here, THIS one" cue instead of relying on scale alone.
/// </summary>
public class PulsingHighlight : MonoBehaviour
{
    [SerializeField] private float pulseSpeed = 2.2f;
    [SerializeField] private float minScale = 1.0f;
    [SerializeField] private float maxScale = 1.16f;

    const float RingMargin = 8f;
    const int RingBorderThickness = 3;

    private RectTransform target;
    private Vector3 originalScale = Vector3.one;
    private bool active;
    private RectTransform ringRt;
    private Image ringImg;

    /// <summary>Points this beacon at a new target, restoring whatever it was
    /// previously pulsing first. Call Show() separately once ready to display.</summary>
    public void Attach(RectTransform newTarget)
    {
        if (target == newTarget) return;
        RestoreCurrent();
        target = newTarget;
        if (target != null) originalScale = target.localScale;
        AttachRingToTarget();
    }

    public void Show()
    {
        active = true;
        if (ringRt != null) ringRt.gameObject.SetActive(target != null);
    }

    public void Hide()
    {
        active = false;
        RestoreCurrent();
        if (ringRt != null) ringRt.gameObject.SetActive(false);
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

        if (ringImg != null)
        {
            var c = ringImg.color;
            c.a = Mathf.Lerp(0.5f, 1f, t);
            ringImg.color = c;
        }
    }

    // Built once, lazily, then reparented (not rebuilt) onto whatever target
    // Attach() points at — a child of target so it inherits target's own
    // scale pulse above for free instead of needing its own scale animation.
    void EnsureRing()
    {
        if (ringRt != null) return;

        var go = new GameObject("PulsingHighlightRing", typeof(RectTransform));
        ringRt = go.GetComponent<RectTransform>();
        ringImg = go.AddComponent<Image>();
        ringImg.raycastTarget = false;
        ringImg.type = Image.Type.Sliced;
        // Transparent fill/trim, opaque border only — reads as a pure outline
        // ring rather than another filled plate, so it never obscures the
        // target's own icon underneath.
        ringImg.sprite = PlateSpriteBuilder.GetSprite(
            new Color32(0, 0, 0, 0), (Color32)GameTheme.ElectricBlue, new Color32(0, 0, 0, 0),
            RingBorderThickness, chamfer: 10, pixelScale: 4);

        // A target that's itself a layout group (ViewControlsHud's pair of
        // turn buttons) would otherwise lay the ring out as one more child.
        go.AddComponent<LayoutElement>().ignoreLayout = true;

        ringRt.anchorMin = Vector2.zero;
        ringRt.anchorMax = Vector2.one;
        ringRt.pivot     = new Vector2(0.5f, 0.5f);
        ringRt.offsetMin = new Vector2(-RingMargin, -RingMargin);
        ringRt.offsetMax = new Vector2(RingMargin, RingMargin);
    }

    void AttachRingToTarget()
    {
        EnsureRing();
        if (target == null) { ringRt.gameObject.SetActive(false); return; }
        ringRt.SetParent(target, false);
        ringRt.SetAsLastSibling(); // draws on top of the target's own icon/glow art so the ring is never itself obscured
        ringRt.localScale = Vector3.one;
    }
}
