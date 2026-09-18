using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A thin, static rectangular border (four bars framing the target's bounds)
/// with a faint soft glow behind it that pulses in intensity — rather than a
/// directional arrow/dot marker — for pointing at a specific UI element
/// ("outline what to click") or framing one to draw attention to ("outline
/// what's crucial to notice"), see CLIPanelManager's SetHighlightedCommand/
/// HighlightPrompt. The glow reuses RadialGlowSprite (the same soft blob
/// MenuButtonFX pulses behind a clicked button), stretched into a rectangle
/// and sized a bit larger than the border so it reads as a halo bleeding
/// outward, not a second box.
///
/// Reparents itself as a child of whatever RectTransform it's Attach()ed to,
/// so it automatically travels with that element regardless of which canvas
/// (screen-space overlay or the floating world-space one CLIPanelManager uses
/// for a Router's CLI panel) or transform hierarchy currently owns it — no
/// separate position-tracking code needed. Detach (Attach(null)) reparents
/// back to whatever transform this component started under, which callers
/// should make something stable (never a GameObject that gets destroyed and
/// rebuilt, like a Command Builder chip) so the outline itself never gets
/// destroyed along with a transient target.
/// </summary>
public class PulsingOutline : MonoBehaviour
{
    [SerializeField] private float pulseSpeed  = 2.2f;
    [SerializeField] private float glowMinAlpha = 0.05f;
    [SerializeField] private float glowMaxAlpha = 0.65f;
    [SerializeField] private float glowMargin   = 16f; // how far the glow halo bleeds past the border
    [SerializeField] private float borderAlpha  = 0.95f; // static — only the glow behind it pulses
    [SerializeField] private float thickness    = 1.5f;
    [SerializeField] private float padding      = 4f; // border sits just outside the target's bounds
    [SerializeField] private Color color        = new Color(1f, 0.82f, 0.20f, 1f);

    Transform homeParent;
    RectTransform target;
    Image glow;
    Image top, bottom, left, right;
    bool active;

    void Awake()
    {
        homeParent = transform.parent;

        // Glow first so it renders behind the crisp border bars added after it.
        glow = MakeGlow();
        top    = MakeBar("Top");
        bottom = MakeBar("Bottom");
        left   = MakeBar("Left");
        right  = MakeBar("Right");
        SetVisible(false);
    }

    Image MakeGlow()
    {
        var go = new GameObject("Glow", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.sprite = RadialGlowSprite.Get();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    Image MakeBar(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        var c = color;
        c.a = borderAlpha;
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Points the outline at a new target, or pass null to park it back
    /// under its home parent (hidden) without destroying it — use this before
    /// the current target's GameObject might be destroyed.</summary>
    public void Attach(RectTransform newTarget)
    {
        if (target == newTarget) return;
        target = newTarget;

        if (target == null)
        {
            transform.SetParent(homeParent, false);
            return;
        }

        transform.SetParent(target, false);
        var rt = (RectTransform)transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.SetAsLastSibling();
        LayoutBars();
    }

    public void Show()
    {
        if (target == null) return;
        active = true;
        SetVisible(true);
    }

    public void Hide()
    {
        active = false;
        SetVisible(false);
    }

    void SetVisible(bool on)
    {
        glow.gameObject.SetActive(on);
        top.gameObject.SetActive(on);
        bottom.gameObject.SetActive(on);
        left.gameObject.SetActive(on);
        right.gameObject.SetActive(on);
    }

    void LayoutBars()
    {
        SetRect(glow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (glowMargin * 2f));

        SetRect(top.rectTransform,    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, padding),  new Vector2(padding * 2f, thickness));
        SetRect(bottom.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(0, -padding), new Vector2(padding * 2f, thickness));
        SetRect(left.rectTransform,   new Vector2(0, 0), new Vector2(0, 1), new Vector2(0f, 0.5f), new Vector2(-padding, 0), new Vector2(thickness, padding * 2f));
        SetRect(right.rectTransform,  new Vector2(1, 0), new Vector2(1, 1), new Vector2(1f, 0.5f), new Vector2(padding, 0),  new Vector2(thickness, padding * 2f));
    }

    static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot             = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = sizeDelta;
    }

    // Only the glow halo pulses — the border itself stays put at a fixed
    // alpha, so it always reads as "here's the precise outline" while the
    // glow behind it is what actually catches the eye.
    void Update()
    {
        if (!active || target == null) return;
        float t = Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f; // continuous 0..1
        float a = Mathf.Lerp(glowMinAlpha, glowMaxAlpha, t);
        Color c = color;
        c.a = a;
        glow.color = c;
    }
}
