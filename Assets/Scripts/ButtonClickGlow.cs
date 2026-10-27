using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight click-time glow + spark burst for a plate-style UI button —
/// CLIPanelManager's Close/Run/Clear, InterfacePickerPopup's port rows,
/// DeviceAddressPopup's Close/stepper/Apply/Cancel. Mirrors the sidebar
/// buttons' toned-down click glow (see SidebarIconButtonFX) — a soft, dim
/// pulse rather than a bright flash — but uses a sliced rounded-rect ring
/// (RoundedGlowOutlineSprite) instead of a per-icon silhouette bake, since
/// these buttons are plain code-drawn chamfered rectangles (PlateSpriteBuilder)
/// with no transparent-padding problem to solve.
///
/// Deliberately independent of whatever hover feedback a button already has
/// (stock ColorTint, or InterfacePickerPopup's own sprite-swap EventTrigger) —
/// this only ever touches its own glow child, never the button's background or
/// label, so it layers on top of any existing button without fighting it.
/// </summary>
public class ButtonClickGlow : MonoBehaviour
{
    const float Duration = 0.28f;
    const float GlowPeak = 0.32f; // dim, ambient — matches SidebarIconButtonFX's toned-down glow

    RectTransform glowRect;
    Image glowImage;
    Color glowColor;
    RectTransform sparkParent;
    Camera eventCamera;
    Coroutine routine;

    /// <summary>Adds and wires a ButtonClickGlow to buttonGo — call once at
    /// build time, after btn/its background Image already exist. Sizes the
    /// glow ring to just past the button's own bounds and hooks btn.onClick
    /// so the glow fires alongside whatever action is already wired, without
    /// replacing or delaying it.</summary>
    public static void Attach(GameObject buttonGo, Button btn, Color glowColor)
    {
        var glowGo = new GameObject("ClickGlow", typeof(RectTransform));
        glowGo.transform.SetParent(buttonGo.transform, false);
        glowGo.transform.SetAsFirstSibling(); // behind the button's own background/label

        var img = glowGo.AddComponent<Image>();
        img.sprite   = RoundedGlowOutlineSprite.Get();
        img.type     = Image.Type.Sliced;
        img.material = RadialGlowSprite.GetAdditiveMaterial();
        img.color    = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
        img.raycastTarget = false;

        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin = Vector2.zero;
        grt.anchorMax = Vector2.one;
        grt.offsetMin = new Vector2(-6f, -6f);
        grt.offsetMax = new Vector2(6f, 6f);

        var glow = buttonGo.AddComponent<ButtonClickGlow>();
        glow.glowRect  = grt;
        glow.glowImage = img;
        glow.glowColor = glowColor;

        var canvas = buttonGo.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            glow.sparkParent = canvas.transform as RectTransform;
            glow.eventCamera = canvas.worldCamera;
        }

        btn.onClick.AddListener(glow.Play);
    }

    public void Play()
    {
        if (!isActiveAndEnabled) return;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(PulseRoutine());
    }

    IEnumerator PulseRoutine()
    {
        if (sparkParent != null)
            UISparks.EmitBurst((RectTransform)transform, sparkParent, eventCamera, glowColor);

        float t = 0f;
        while (t < Duration)
        {
            t += Time.unscaledDeltaTime;
            float pulse = Mathf.Sin(Mathf.Clamp01(t / Duration) * Mathf.PI); // 0 -> 1 -> 0
            glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, pulse * GlowPeak);
            yield return null;
        }
        glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
        routine = null;
    }
}
