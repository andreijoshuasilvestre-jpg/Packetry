using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover/click feedback for a main-menu or level-card button whose look is a
/// background plate sprite (BlankButton / BlankButton_Hover) with a text
/// label drawn on top. Mirrors the sidebar buttons' visual language (see
/// SidebarIconButtonFX) but works on a "Visual" sub-rect that holds *both*
/// the background and the label together, rather than a single baked icon
/// sprite, since the menu buttons compose art + TextMeshPro text separately.
///
/// While hovered, the plate eases up to hoverScale (and back down to 1 on
/// exit) — a lighter, continuous cousin of the click bounce below, not a
/// pop, so it doesn't fight visually with the sharper click-time animation.
///
/// On click the plate does a single subtle bounce-scale pop (no tilt), a
/// soft glow pulse behind it, and a short burst of blue UI sparks — the same
/// "electric construction" language ConstructionSparks/ConstructionRevealEffect
/// use when a device is placed in the game scene, just rendered in UI space
/// (see UISparks) instead of world space.
///
/// Buttons don't run their actual function the instant they're clicked —
/// PlayClickThen() plays the FX first and only invokes the callback once the
/// bounce/glow/sparks have had time to read, instead of the resulting screen
/// change (scene load, slide transition, ...) skipping straight past them.
///
/// Attached to the button GameObject by MainMenuShellBuilder.CreateMenuButton /
/// LevelSelectShellBuilder.CreateStaticButton / LevelSelectManager.CreateButton;
/// purely presentational — it does not know what the button does.
/// </summary>
public class MenuButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public RectTransform visualRect;   // bounce-scales on click; holds background + label
    public Image backgroundImage;
    public Image glowImage;
    public Sprite normalSprite;
    public Sprite hoverSprite;
    public Color glowColor = Color.white;

    // Optional — set by a builder that wants this button to start invisible
    // and be revealed later via PlayRevealIn() (see MainMenuShellBuilder's
    // main-menu buttons) instead of being visible from the first frame.
    // Left null by builders that don't use that entrance (LevelSelect's
    // cards/static buttons), which is a harmless no-op for PlayRevealIn.
    public CanvasGroup revealGroup;

    [Tooltip("localScale visualRect eases to while the pointer is over the button.")]
    public float hoverScale = 1.06f;
    const float HoverScaleDuration = 0.12f;

    // Opt-in continuous "breathing" pulse for buttons that should draw a
    // little idle attention (see LevelSelectManager's level cards) without
    // it fighting the hover/click scale routines below — Update() only ever
    // writes visualRect.localScale from this when neither of those currently
    // owns it (see the guard in Update()), and the amplitude is deliberately
    // tiny: this rect is also the raycast target's own transform (Background
    // lives under it), so a bigger pulse would visibly nudge the clickable
    // area around instead of just reading as gentle life in the card.
    public bool idlePulse = false;
    const float IdlePulseAmplitude = 0.018f;
    const float IdlePulseSpeed     = 1.6f; // radians/sec — ~4s full cycle
    float idlePulsePhase;

    RectTransform sparkParent;
    Camera eventCamera;

    Coroutine clickRoutine;
    Coroutine hoverScaleRoutine;
    Coroutine revealRoutine;
    bool hovering;

    /// <summary>Call once after assigning all fields.</summary>
    public void Init()
    {
        if (glowImage != null)
        {
            glowImage.sprite = RadialGlowSprite.Get();
            glowImage.color  = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            glowImage.raycastTarget = false;
        }

        // Sparks need a stable, canvas-space parent to spawn into (never the
        // "Visual" rect, which is itself being scaled by the click pop) —
        // the button's own Canvas root always qualifies and needs no extra
        // wiring from the callers that build these buttons.
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            sparkParent = canvas.transform as RectTransform;
            eventCamera = canvas.worldCamera;
        }

        // Random per-button phase so a row of idle-pulsing cards breathes
        // out of sync with each other instead of all pulsing in lockstep.
        idlePulsePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        RefreshSprite();
    }

    // Only ever runs the pulse math while nothing else owns visualRect's
    // scale — hover-enter/exit, the click bounce and the reveal all drive it
    // directly (see SetHoverScaleTarget/ClickRoutine/PlayRevealIn) and take
    // priority; this simply stays quiet until they're done, so it can't
    // stutter or fight them.
    void Update()
    {
        if (!idlePulse || visualRect == null) return;
        if (hovering || clickRoutine != null || hoverScaleRoutine != null || revealRoutine != null) return;

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * IdlePulseSpeed + idlePulsePhase) * IdlePulseAmplitude;
        visualRect.localScale = Vector3.one * pulse;
    }

    /// <summary>
    /// Plays this button's first-appearance reveal — pops in from
    /// revealGroup's alpha 0 with the same glow-pulse-and-spark language as
    /// the click flash (see MenuRevealFlash) — instead of the button just
    /// being visible from frame one. Call once per menu appearance, after
    /// Init(); a no-op fade if revealGroup was never assigned. Also how
    /// LevelSelectManager transforms a freshly unlocked level card from its
    /// old locked look into a live one (see LevelSelectManager.PlayUnlock).
    /// </summary>
    public void PlayRevealIn()
    {
        if (revealGroup != null) revealGroup.alpha = 0f;
        if (visualRect != null) visualRect.localScale = Vector3.one * MenuRevealFlash.StartScale;
        if (revealRoutine != null) StopCoroutine(revealRoutine);
        revealRoutine = StartCoroutine(RevealRoutine());
    }

    IEnumerator RevealRoutine()
    {
        yield return MenuRevealFlash.Play(revealGroup, visualRect, glowImage, glowColor, visualRect, sparkParent, eventCamera);
        revealRoutine = null;
        // A hover that began mid-reveal was deferred (see SetHoverScaleTarget)
        // — pick it up now that the reveal has let go of the scale.
        if (hovering) SetHoverScaleTarget(hoverScale);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        RefreshSprite();
        SetHoverScaleTarget(hoverScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        RefreshSprite();
        SetHoverScaleTarget(1f);
    }

    void RefreshSprite()
    {
        if (backgroundImage == null) return;
        backgroundImage.sprite = (hovering && hoverSprite != null) ? hoverSprite : normalSprite;
    }

    // Click's bounce pop (ClickRoutine) and the first-appearance reveal
    // (PlayRevealIn/MenuRevealFlash) both drive visualRect.localScale too —
    // deferring to them here (rather than fighting over the same
    // transform) means a hover right as either plays just resumes once
    // they're done, via the hovering-aware scale ClickRoutine settles to
    // and the natural pointer-enter/exit events either leaves behind.
    void SetHoverScaleTarget(float target)
    {
        if (clickRoutine != null || revealRoutine != null || visualRect == null) return;
        if (hoverScaleRoutine != null) StopCoroutine(hoverScaleRoutine);
        hoverScaleRoutine = StartCoroutine(HoverScaleRoutine(target));
    }

    IEnumerator HoverScaleRoutine(float target)
    {
        float start = visualRect.localScale.x;
        float t = 0f;
        while (t < HoverScaleDuration)
        {
            t += Time.unscaledDeltaTime;
            float scale = Mathf.Lerp(start, target, Mathf.Clamp01(t / HoverScaleDuration));
            visualRect.localScale = Vector3.one * scale;
            yield return null;
        }
        visualRect.localScale = Vector3.one * target;
        hoverScaleRoutine = null;
    }

    /// <summary>
    /// Call from Button.onClick instead of wiring the function directly —
    /// plays the bounce/glow/spark FX, then invokes onComplete once it's
    /// finished playing. Re-clicks are ignored while already animating
    /// rather than restarting, so onComplete never fires twice for one click.
    /// </summary>
    public void PlayClickThen(Action onComplete)
    {
        if (!isActiveAndEnabled) { onComplete?.Invoke(); return; }
        if (clickRoutine != null) return;
        clickRoutine = StartCoroutine(ClickRoutine(onComplete));
    }

    IEnumerator ClickRoutine(Action onComplete)
    {
        const float duration  = 0.12f; // shortened from 0.22f — trims the delay before onComplete actually fires (see PlayClickThen)
        const float peakScale = 1.12f;
        const float glowPeak  = 0.6f;
        const float glowScale = 1.3f;

        if (sparkParent != null)
            UISparks.EmitBurst(visualRect != null ? visualRect : (RectTransform)transform, sparkParent, eventCamera, glowColor);

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / duration);
            float arch = Mathf.Sin(p * Mathf.PI); // 0 -> 1 -> 0, single pop — no tilt, no repeated wobble

            float visualScale = 1f + arch * (peakScale - 1f);
            if (visualRect != null)
                visualRect.localScale = Vector3.one * visualScale;

            if (glowImage != null)
            {
                glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, arch * glowPeak);
                // Glow is a child of visualRect (so hand-moving/resizing the
                // Visual rect in the Editor carries the glow along with it —
                // see the shell builders), which means it inherits
                // visualScale automatically. Divide out that inherited scale
                // here so the glow's actual on-screen peak size still hits
                // glowScale exactly, same as when it was an unscaled sibling.
                float desiredScale = 1f + arch * (glowScale - 1f);
                float compensated  = visualScale > 0.0001f ? desiredScale / visualScale : desiredScale;
                glowImage.rectTransform.localScale = Vector3.one * compensated;
            }

            yield return null;
        }

        if (visualRect != null)
            visualRect.localScale = Vector3.one * (hovering ? hoverScale : 1f);
        if (glowImage != null)
        {
            glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            glowImage.rectTransform.localScale = Vector3.one;
        }
        clickRoutine = null;

        onComplete?.Invoke();
    }
}
