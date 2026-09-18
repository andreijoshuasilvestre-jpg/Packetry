using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover/active/click feedback for a sidebar button whose entire look (box +
/// icon + label) is a single baked sprite:
///   - Hover:        the artwork swaps from its normal sprite to its "_Clicked"
///                    (lit-up) variant.
///   - Forced active: same lit artwork, held on while the button's mode/tool
///                    is currently engaged (see NetworkUIManager.Update).
///   - Click:         the whole button does a decaying tilt-shake while a soft
///                     glow pulses behind it.
///
/// Attached to the button GameObject by NetworkUIManager.CreateSidebarButton;
/// purely presentational — it does not know what the button *does*, so it
/// composes cleanly with whatever functional onClick listener (drag device,
/// begin cable mode, etc.) is also registered.
/// </summary>
public class SidebarIconButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image iconImage;
    public Image glowImage;
    public Sprite normalSprite;
    public Sprite hoverSprite;
    public Color glowColor = Color.white;

    Coroutine punchRoutine;
    bool hovering;
    bool forcedActive;

    /// <summary>
    /// Call once after setting iconImage/glowImage/normalSprite/hoverSprite/glowColor.
    /// Not done in Awake() because those fields are assigned by the caller right
    /// after AddComponent — Awake would already have fired with everything null.
    /// </summary>
    public void Init()
    {
        if (glowImage != null)
        {
            glowImage.sprite = RadialGlowSprite.Get();
            glowImage.color  = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            glowImage.raycastTarget = false;
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
        iconImage.sprite = (lit && hoverSprite != null) ? hoverSprite : normalSprite;
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
        const float maxAngle  = 12f;
        const float wobbles   = 5f;
        const float glowPeak  = 0.55f;
        const float glowScale = 1.35f;

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

            yield return null;
        }

        if (iconImage != null)
            iconImage.rectTransform.localRotation = Quaternion.identity;
        if (glowImage != null)
        {
            glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            glowImage.rectTransform.localScale = Vector3.one;
        }
        punchRoutine = null;
    }
}
