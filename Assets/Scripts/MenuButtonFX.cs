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

    RectTransform sparkParent;
    Camera eventCamera;

    Coroutine clickRoutine;
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

        RefreshSprite();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        RefreshSprite();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        RefreshSprite();
    }

    void RefreshSprite()
    {
        if (backgroundImage == null) return;
        backgroundImage.sprite = (hovering && hoverSprite != null) ? hoverSprite : normalSprite;
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
        const float duration  = 0.22f;
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
            visualRect.localScale = Vector3.one;
        if (glowImage != null)
        {
            glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            glowImage.rectTransform.localScale = Vector3.one;
        }
        clickRoutine = null;

        onComplete?.Invoke();
    }
}
