using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot glow + outline flash + spark burst a popup panel plays the moment
/// it finishes zooming in (see ZoomPanel) — the panel-sized counterpart of
/// the glow and sparks MenuButtonFX plays on a menu button click.
///
/// Three layers, all driven by the same quick flash (snaps up, then eases
/// out — see Intensity):
///   - Outline: a crisp line tracing the panel's own silhouette, chamfered
///     corners included.
///   - Glow: a soft halo starting at full strength right on that outline and
///     fading out smoothly past it, its reach derived from the panel's
///     shorter side. A button's glow is a RadialGlowSprite blob sized
///     proportionally (1.15x its width, 1.6x its height) — right on a 220x64
///     button, but those same ratios on a 720x440 panel bleed hundreds of
///     units past its edge and stretch the circle into an oval.
///   - Sparks: UISparks.EmitOutlineBurst, spread around the whole outline and
///     scaled in count to the panel's perimeter — a button-sized burst is
///     lost against a panel.
///
/// Outline and glow are both baked from the panel Image's own sprite (see
/// PanelEdgeSprites), so they never repeat whatever PlateSpriteBuilder
/// parameters drew that panel; either is skipped if that sprite isn't
/// readable. Both are children of the panel (first siblings: drawn over the
/// plate's own Image, under all of its content), so they inherit its zoom
/// scale — ZoomOut shrinks a still-fading flash along with the panel.
/// </summary>
public class PanelArrivalGlow : MonoBehaviour
{
    const float Duration       = 0.24f; // whole flash — was a 0.34s symmetric pulse
    const float AttackFraction = 0.15f; // first slice of Duration spent rising; the rest eases out
    const float GlowPeak       = 0.6f;  // MenuButtonFX's click glowPeak

    const float GlowReachFraction = 0.035f; // halo reach past the outline, as a fraction of the panel's shorter side...
    const float MinGlowReach      = 10f;    // ...clamped, in canvas units
    const float MaxGlowReach      = 24f;

    const float OutlineThickness = 3f;   // canvas units — a hair thinner than the plates' own 4-unit border, so it reads as a lit edge
    const float OutlinePeak      = 0.95f;
    const float OutlineHeat      = 0.45f; // pushed this far toward white — a hot line, brighter than the tinted glow around it

    const float SparkSpacing  = 45f;   // one spark per this many units of perimeter...
    const int   MinSparks     = 28;    // ...clamped — still 2x a button's 14 on the smallest panel
    const int   MaxSparks     = 64;
    const float SparkLifetime = 0.55f; // vs a button's 0.32 — lingers after the drag stops them
    const float SparkSpeed    = 1.15f; // x a button spark's speed — carries them a little further out

    RectTransform panelRt;
    Image panelImage;
    Image glowImage;
    Image outlineImage;
    Color glowColor;
    int glowPadTexels = -1;
    Coroutine routine;

    /// <summary>Adds (once) the glow and outline layers to panelGo — call at
    /// build time, then call Play() each time the panel finishes opening,
    /// e.g. as ZoomPanel.ZoomIn's onComplete.</summary>
    public static PanelArrivalGlow Attach(GameObject panelGo, Color glowColor)
    {
        if (!panelGo.TryGetComponent(out PanelArrivalGlow glow))
            glow = panelGo.AddComponent<PanelArrivalGlow>();
        glow.glowColor = glowColor;
        glow.EnsureLayers();
        return glow;
    }

    void EnsureLayers()
    {
        if (panelRt != null) return;
        panelRt = (RectTransform)transform;
        panelImage = GetComponent<Image>();

        glowImage = CreateLayer("ArrivalGlow", 0);
        glowImage.material = RadialGlowSprite.GetAdditiveMaterial();
        glowImage.enabled  = false; // until SyncGlowSprite gives it a sprite — a sprite-less Image draws a white rect

        if (panelImage != null && panelImage.sprite != null)
        {
            int thickness = Mathf.Max(1, Mathf.RoundToInt(OutlineThickness * TexelsPerUnit()));
            var outlineSprite = PanelEdgeSprites.Outline(panelImage.sprite, thickness);
            if (outlineSprite != null)
            {
                outlineImage = CreateLayer("ArrivalOutline", 1);
                outlineImage.sprite = outlineSprite;
                outlineImage.type   = panelImage.type;
                outlineImage.pixelsPerUnitMultiplier = panelImage.pixelsPerUnitMultiplier;
            }
        }

        // Baked now, at build time, for the panel's authored size — so the
        // first open doesn't stall on it. Play() re-checks in case the panel
        // has been resized since (FitToScreen, a UI Scale change).
        SyncGlowSprite();
        HideLayers();
    }

    // The panel sprite's own texels per canvas unit, as its Image renders it
    // — converts canvas-unit sizes into that sprite's texel space.
    float TexelsPerUnit() => panelImage.pixelsPerUnit * panelImage.pixelsPerUnitMultiplier;

    // (Re)bakes the halo for the panel's current size, if its reach changed.
    // The glow rect is the panel's rect grown by exactly the sprite's
    // padding, so the halo's inner edge sits right on the panel's outline.
    void SyncGlowSprite()
    {
        if (panelImage == null || panelImage.sprite == null) return;

        Vector2 size = panelRt.rect.size;
        float reach = Mathf.Clamp(Mathf.Min(size.x, size.y) * GlowReachFraction, MinGlowReach, MaxGlowReach);
        float texelsPerUnit = TexelsPerUnit();
        int pad = Mathf.Max(1, Mathf.RoundToInt(reach * texelsPerUnit));
        if (pad == glowPadTexels) return;

        var glowSprite = PanelEdgeSprites.OuterGlow(panelImage.sprite, pad);
        if (glowSprite == null) return;

        glowPadTexels = pad;
        glowImage.sprite = glowSprite;
        glowImage.type   = Image.Type.Sliced;
        glowImage.pixelsPerUnitMultiplier = panelImage.pixelsPerUnitMultiplier;
        glowImage.enabled = true;

        float outset = pad / texelsPerUnit;
        glowImage.rectTransform.offsetMin = new Vector2(-outset, -outset);
        glowImage.rectTransform.offsetMax = new Vector2(outset, outset);
    }

    // Stretched over the panel's rect at siblingIndex — over the plate's own
    // Image, under every piece of its content.
    Image CreateLayer(string name, int siblingIndex)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        go.transform.SetSiblingIndex(siblingIndex);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    public void Play()
    {
        if (!isActiveAndEnabled) return;
        EnsureLayers();
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(PulseRoutine());
    }

    IEnumerator PulseRoutine()
    {
        SyncGlowSprite();
        EmitSparks(panelRt.rect.size);

        Color hot = Color.Lerp(glowColor, Color.white, OutlineHeat);

        float t = 0f;
        while (t < Duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Intensity(Mathf.Clamp01(t / Duration));
            glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, k * GlowPeak);
            if (outlineImage != null)
                outlineImage.color = new Color(hot.r, hot.g, hot.b, k * OutlinePeak);
            yield return null;
        }

        HideLayers();
        routine = null;
    }

    // 0 -> 1 -> 0 flash: a quick ease-out rise over AttackFraction, then an
    // ease-out fall over the rest — drops off fast and trails away softly,
    // instead of the old symmetric sine pulse lingering at its peak.
    static float Intensity(float p)
    {
        if (p < AttackFraction)
            return 1f - Mathf.Pow(1f - p / AttackFraction, 3f);
        float q = (p - AttackFraction) / (1f - AttackFraction);
        return (1f - q) * (1f - q);
    }

    // Sparks spawn into the root canvas (never the panel, whose scale
    // ZoomPanel drives) — same parent MenuButtonFX uses for its bursts.
    void EmitSparks(Vector2 panelSize)
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var root = canvas.rootCanvas;
        Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        float perimeter = 2f * (panelSize.x + panelSize.y);
        int count = Mathf.Clamp(Mathf.RoundToInt(perimeter / SparkSpacing), MinSparks, MaxSparks);
        UISparks.EmitOutlineBurst(panelRt, (RectTransform)root.transform, cam, glowColor, count, SparkLifetime, SparkSpeed);
    }

    void HideLayers()
    {
        glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
        if (outlineImage != null) outlineImage.color = new Color(1f, 1f, 1f, 0f);
    }

    // Unity already stops the coroutine when the panel is hidden — this just
    // makes sure a panel hidden mid-flash doesn't reopen with a stale,
    // half-lit glow/outline showing through its next zoom-in.
    void OnDisable()
    {
        routine = null;
        if (glowImage != null) HideLayers();
    }
}
