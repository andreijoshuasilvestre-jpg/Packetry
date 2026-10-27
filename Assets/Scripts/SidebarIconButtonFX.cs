using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover/active/click feedback for a sidebar button whose entire look (box +
/// icon + label) is a single baked sprite:
///   - Hover:        the artwork swaps from its normal sprite to its "_Clicked"
///                    (lit-up) variant, and the whole button scales up by a
///                    hair (see HoverScaleRoutine).
///   - Forced active: same lit artwork, held on while the button's mode/tool
///                    is currently engaged (see NetworkUIManager.Update).
///   - Click:         the icon does a small decaying tilt-shake while a
///                     layered additive glow (soft rounded-rect outline +
///                     a tighter, faster hot-white core — see
///                     ClickPunchRoutine) pulses behind it, the artwork
///                     itself flashes (iconGlowImage), and a burst of
///                     electric sparks (UISparks) kicks outward from it.
///
/// Attached to the button GameObject by NetworkUIManager.CreateSidebarButton;
/// purely presentational — it does not know what the button *does*, so it
/// composes cleanly with whatever functional onClick listener (drag device,
/// begin cable mode, etc.) is also registered.
/// </summary>
public class SidebarIconButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image iconImage;
    public Image iconGlowImage;
    public Image glowImage;
    public Image glowCoreImage;
    public Sprite normalSprite;
    public Sprite hoverSprite;
    public Color glowColor = Color.white;

    /// <summary>Set by NetworkUIManager.CreateSidebarButton when
    /// SpriteSilhouetteGlow could bake one for this button's icon — makes
    /// glowImage trace the icon's actual silhouette instead of the generic
    /// RoundedGlowOutlineSprite rounded-rect ring. Null falls back to that ring.</summary>
    public Sprite glowSilhouette;

    const float HoverScale         = 1.045f; // "teeny tiny" — NetworkUIManager adds extra layout spacing to cover this
    const float HoverScaleDuration = 0.12f;

    RectTransform selfRect;
    RectTransform sparkParent;
    Camera eventCamera;

    Coroutine punchRoutine;
    Coroutine hoverScaleRoutine;
    bool hovering;
    bool forcedActive;

    /// <summary>
    /// Call once after setting iconImage/iconGlowImage/glowImage/glowCoreImage/normalSprite/hoverSprite/glowColor.
    /// Not done in Awake() because those fields are assigned by the caller right
    /// after AddComponent — Awake would already have fired with everything null.
    /// </summary>
    public void Init()
    {
        selfRect = transform as RectTransform;

        var glowMat = RadialGlowSprite.GetAdditiveMaterial();

        if (glowImage != null)
        {
            if (glowSilhouette != null)
            {
                // Baked to this icon's own alpha silhouette (see
                // SpriteSilhouetteGlow) — stretched (Simple, not Sliced) to
                // fill the same rect as the icon on top of it.
                glowImage.sprite = glowSilhouette;
                glowImage.type   = Image.Type.Simple;
            }
            else
            {
                // Fallback: sliced (9-slice) rounded-rect ring instead of a
                // plain circle — a circle stretched to the button's own
                // width/height would read as a squashed oval; sliced, the
                // ring's corner radius stays fixed and it actually traces a
                // rounded-rect approximation of the button's shape.
                glowImage.sprite = RoundedGlowOutlineSprite.Get();
                glowImage.type   = Image.Type.Sliced;
            }
            glowImage.material = glowMat;
            glowImage.color    = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            glowImage.raycastTarget = false;
        }
        if (glowCoreImage != null)
        {
            glowCoreImage.sprite   = RadialGlowSprite.Get();
            glowCoreImage.material = glowMat;
            glowCoreImage.color    = new Color(1f, 1f, 1f, 0f);
            glowCoreImage.raycastTarget = false;
        }
        if (iconGlowImage != null)
        {
            // Uses the icon's own sprite (kept in sync by RefreshSprite) so
            // the flash traces the artwork's actual silhouette rather than a
            // generic shape.
            iconGlowImage.material = glowMat;
            iconGlowImage.color    = new Color(1f, 1f, 1f, 0f);
            iconGlowImage.raycastTarget = false;
        }

        // Sparks need a stable, canvas-space parent to spawn into (never a
        // rect that's itself being tilt-shaken/scaled) — the button's own
        // Canvas root always qualifies and needs no extra wiring from the
        // sidebar builder (mirrors MenuButtonFX.Init).
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            sparkParent = canvas.transform as RectTransform;
            eventCamera = canvas.worldCamera;
        }

        RefreshSprite();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        RefreshSprite();
        AnimateHoverScale(HoverScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        RefreshSprite();
        AnimateHoverScale(1f);
    }

    /// <summary>Keep showing the lit "_Clicked" artwork while this button's mode is engaged.</summary>
    public void SetForcedActive(bool active)
    {
        if (forcedActive == active) return;
        forcedActive = active;
        RefreshSprite();
    }

    void RefreshSprite()
    {
        if (iconImage == null) return;
        bool lit = hovering || forcedActive;
        Sprite s = (lit && hoverSprite != null) ? hoverSprite : normalSprite;
        iconImage.sprite = s;
        if (iconGlowImage != null) iconGlowImage.sprite = s;
    }

    void AnimateHoverScale(float target)
    {
        if (selfRect == null) return;
        if (hoverScaleRoutine != null) StopCoroutine(hoverScaleRoutine);
        hoverScaleRoutine = StartCoroutine(HoverScaleRoutine(target));
    }

    IEnumerator HoverScaleRoutine(float target)
    {
        float start = selfRect.localScale.x;
        float t = 0f;
        while (t < HoverScaleDuration)
        {
            t += Time.unscaledDeltaTime;
            float s = Mathf.Lerp(start, target, Mathf.Clamp01(t / HoverScaleDuration));
            selfRect.localScale = Vector3.one * s;
            yield return null;
        }
        selfRect.localScale = Vector3.one * target;
        hoverScaleRoutine = null;
    }

    /// <summary>Call from Button.onClick — plays the tilt-shake + glow pulse.</summary>
    public void PlayClickPunch()
    {
        if (!isActiveAndEnabled) return;
        if (punchRoutine != null) StopCoroutine(punchRoutine);
        punchRoutine = StartCoroutine(ClickPunchRoutine());
    }

    IEnumerator ClickPunchRoutine()
    {
        const float duration  = 0.28f;
        const float maxAngle  = 3.5f; // subtle — was 12; just enough tilt to read as a click, not a shake
        const float wobbles   = 2f;   // was 5; fewer oscillations reads calmer at the smaller angle
        const float glowPeak  = 0.32f; // dim, ambient — was 0.55
        // glowImage now traces the icon's own silhouette (SpriteSilhouetteGlow),
        // filled solid throughout its interior rather than a thin ring — scaling
        // that up used to expose a visibly bigger, sharply-bordered duplicate of
        // the button's own shape peeking out ("a scaled up solid outline"). Left
        // at 1x: the pulse now reads purely through the baked-in soft outward
        // bleed (see SpriteSilhouetteGlow.GlowRadius) instead of inflating the
        // whole shape.
        const float glowScale = 1f;

        // The core is a second, tighter glow layer that flashes brighter-than-
        // tint and burns out inside the first third of the click — while the
        // outer halo above is still swelling. A single glow blob reads as a
        // flat colored sprite; a fast hot core plus a slower soft halo reads
        // as an actual light source with something bright at its center, i.e.
        // the "3D" depth the flat version was missing. Toned down alongside the
        // halo above so neither reads as a bright flash — just a soft glow.
        const float coreFraction = 0.35f;
        const float corePeak     = 1.0f;
        const float coreScale    = 1.1f;

        // The icon's own PNG flash shares the core's fast attack/decay so the
        // artwork itself looks like the light source, not just the halo
        // behind it — peak kept moderate (<corePeak) since it's stacked
        // directly on already-opaque art instead of empty space.
        const float iconGlowPeak = 0.5f;

        if (sparkParent != null && iconImage != null)
            UISparks.EmitBurst(iconImage.rectTransform, sparkParent, eventCamera, glowColor);

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p     = Mathf.Clamp01(t / duration);
            float decay = 1f - p;

            if (iconImage != null)
            {
                float angle = Mathf.Sin(p * Mathf.PI * wobbles) * maxAngle * decay;
                iconImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (glowImage != null)
            {
                float pulse = Mathf.Sin(p * Mathf.PI); // 0 -> 1 -> 0
                glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, pulse * glowPeak);
                glowImage.rectTransform.localScale = Vector3.one * (1f + pulse * (glowScale - 1f));
            }

            float corePulse = 0f;
            if (glowCoreImage != null || iconGlowImage != null)
            {
                float pc = p / coreFraction;
                corePulse = pc <= 1f ? Mathf.Sin(pc * Mathf.PI) : 0f;
            }

            if (glowCoreImage != null)
            {
                Color hot = Color.Lerp(glowColor, Color.white, 0.6f);
                glowCoreImage.color = new Color(hot.r, hot.g, hot.b, corePulse * corePeak);
                glowCoreImage.rectTransform.localScale = Vector3.one * (1f + corePulse * (coreScale - 1f));
            }

            if (iconGlowImage != null)
            {
                Color hot = Color.Lerp(glowColor, Color.white, 0.5f);
                iconGlowImage.color = new Color(hot.r, hot.g, hot.b, corePulse * iconGlowPeak);
            }

            yield return null;
        }

        ResetPunchVisuals();
        punchRoutine = null;
    }

    void ResetPunchVisuals()
    {
        if (iconImage != null)
            iconImage.rectTransform.localRotation = Quaternion.identity;
        if (glowImage != null)
        {
            glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            glowImage.rectTransform.localScale = Vector3.one;
        }
        if (glowCoreImage != null)
        {
            glowCoreImage.color = new Color(1f, 1f, 1f, 0f);
            glowCoreImage.rectTransform.localScale = Vector3.one;
        }
        if (iconGlowImage != null)
            iconGlowImage.color = new Color(1f, 1f, 1f, 0f);
    }

    // A button switched off while the pointer is on it never gets
    // OnPointerExit — the EventSystem doesn't deliver to inactive objects —
    // and disabling also kills its coroutines mid-way. That happens all the
    // time to a compound button's tools (CompoundSidebarGroup parks the old
    // face in its pool, retracts flyout items, sometimes right under the
    // cursor), so without this a tool came back still lit, still enlarged,
    // or frozen mid click-flash.
    void OnDisable()
    {
        hovering = false;
        hoverScaleRoutine = null;
        punchRoutine = null;
        if (selfRect != null) selfRect.localScale = Vector3.one;
        ResetPunchVisuals();
        RefreshSprite();
    }
}
