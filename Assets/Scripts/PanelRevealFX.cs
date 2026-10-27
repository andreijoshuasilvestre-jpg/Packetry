using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VHS/CRT-style "power-on" reveal for a popup panel — the same technique
/// CLIPanelManager uses for its own opening: a wrapper's RectMask2D-clipped
/// rect grows from a thin centered line to full height, so the panel appears
/// to unfold from its own vertical center rather than just popping in. Also
/// fires a sharp glow flash (tracing the wrapper's growing edge, front-loaded
/// into the first slice of the reveal instead of tracking its whole length —
/// see FlashPulse) and a spark burst — the same click-glow treatment
/// sidebar/CLI/popup buttons get (see ButtonClickGlow), just played once on
/// open instead of per click. PlayClose() mirrors this for dismissal: the
/// same shrink-and-flash, run backwards, before the caller actually hides
/// the panel.
///
/// Caller owns building the wrapper (RectMask2D already added, sized to its
/// FULL final WIDTH — this only ever animates height) and keeping its actual
/// content (rows/fields) at a fixed size/position inside it, same contract as
/// CLIPanelManager's _panelRoot/_panelWrapper split.
///
/// The glow is a SIBLING of the wrapper, not a child of it — the wrapper's
/// own RectMask2D would otherwise clip the glow's entire outward bleed the
/// instant it tried to peek past the wrapper's own edge, making the halo
/// invisible (it has to render OUTSIDE the masked rect to read as a glow
/// around the panel at all). Since it can't just stretch-anchor to a parent
/// it isn't inside, it re-syncs its own rect to match the wrapper's every
/// frame instead (see SyncGlowRect).
/// </summary>
public class PanelRevealFX : MonoBehaviour
{
    const float Duration          = 0.16f; // was 0.35 — snappier open/close
    const float StartHeight       = 6f;
    const float GlowPeak          = 1f;    // bright — this is a one-shot flash, not the dim ambient button glow
    const float GlowMargin        = 10f;   // how far the glow ring bleeds past the wrapper's own edge on each side
    const float GlowFlashFraction = 0.4f;  // flash's own attack+decay finishes in this fraction of Duration —
                                            // decoupled from the full grow/shrink length so it reads as a quick
                                            // flash instead of a glow that tracks the whole reveal
    const float GlowFlashScale    = 1.1f;  // slight punch-out on the glow rect itself while it flashes

    RectTransform wrapperRt;
    RectTransform glowRt;
    Image glowImage;
    Coroutine routine;

    /// <summary>Adds (once) and (re)plays the reveal on wrapperGo — call every
    /// time the panel is shown, after positioning wrapperGo at its final
    /// on-screen spot. fullHeight is the wrapper's target end height (its
    /// current sizeDelta.x is left untouched — only height animates).
    /// sparkParent/eventCamera match UISparks.EmitBurst's own parameters.</summary>
    public static void Play(GameObject wrapperGo, float fullHeight, Color glowColor,
                             RectTransform sparkParent, Camera eventCamera)
    {
        AudioManager.Instance?.PlaySfx(SfxId.PanelPrompt); // same entrance sound as ZoomPanel.ZoomIn
        GetOrAdd(wrapperGo).PlayOpen(fullHeight, glowColor, sparkParent, eventCamera);
    }

    /// <summary>The reverse of Play(): shrinks wrapperGo from fullHeight back
    /// down to its thin start line (with the same flash) and invokes
    /// onComplete once fully collapsed — call BEFORE actually deactivating
    /// the panel, and do the deactivation inside onComplete, so the panel
    /// stays visible for the shrink instead of popping off mid-animation.</summary>
    public static void PlayClose(GameObject wrapperGo, float fullHeight, Color glowColor,
                                  RectTransform sparkParent, Camera eventCamera, System.Action onComplete)
    {
        GetOrAdd(wrapperGo).PlayCloseInternal(fullHeight, glowColor, sparkParent, eventCamera, onComplete);
    }

    static PanelRevealFX GetOrAdd(GameObject wrapperGo)
    {
        var fx = wrapperGo.GetComponent<PanelRevealFX>();
        if (fx == null) fx = wrapperGo.AddComponent<PanelRevealFX>();
        return fx;
    }

    void EnsureGlow()
    {
        wrapperRt = GetComponent<RectTransform>();
        if (glowImage != null) return;

        var glowGo = new GameObject("RevealGlow", typeof(RectTransform));
        glowGo.transform.SetParent(transform.parent, false); // sibling, NOT child — see class comment
        glowGo.transform.SetSiblingIndex(transform.GetSiblingIndex()); // sits just behind the wrapper

        glowRt = glowGo.GetComponent<RectTransform>();
        glowRt.anchorMin = wrapperRt.anchorMin;
        glowRt.anchorMax = wrapperRt.anchorMax;
        glowRt.pivot     = wrapperRt.pivot;

        glowImage = glowGo.AddComponent<Image>();
        glowImage.sprite   = RoundedGlowOutlineSprite.Get();
        glowImage.type     = Image.Type.Sliced;
        glowImage.material = RadialGlowSprite.GetAdditiveMaterial();
        glowImage.raycastTarget = false;
    }

    // Keeps the glow tracking the wrapper's own current rect every frame —
    // it shares the wrapper's anchors/pivot (set once in EnsureGlow) so its
    // anchoredPosition is directly comparable/copyable.
    void SyncGlowRect()
    {
        glowRt.anchoredPosition = wrapperRt.anchoredPosition;
        glowRt.sizeDelta = wrapperRt.sizeDelta + new Vector2(GlowMargin * 2f, GlowMargin * 2f);
    }

    // 0 -> 1 -> 0, but the whole rise+fall happens inside GlowFlashFraction of
    // p instead of across the full reveal — a sharp flash rather than a glow
    // that pulses in step with the grow/shrink.
    static float FlashPulse(float p)
    {
        float pc = p / GlowFlashFraction;
        return pc <= 1f ? Mathf.Sin(pc * Mathf.PI) : 0f;
    }

    void PlayOpen(float fullHeight, Color glowColor, RectTransform sparkParent, Camera eventCamera)
    {
        EnsureGlow();
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Reveal(fullHeight, glowColor, sparkParent, eventCamera));
    }

    IEnumerator Reveal(float fullHeight, Color glowColor, RectTransform sparkParent, Camera eventCamera)
    {
        float width = wrapperRt.sizeDelta.x;
        Color hot = Color.Lerp(glowColor, Color.white, 0.5f); // pushed toward white — reads as a hot flash, not a tinted glow

        // Spawning sparks reads their spread from the origin rect's CURRENT
        // size (see UISparks.EmitBurst) — momentarily sizing to the full
        // target rect (invisible: this happens before any frame renders)
        // gets the correct spread instead of a condensed point, same fix as
        // the flyout's own pop-in sparks needed (see CompoundSidebarGroup).
        wrapperRt.sizeDelta = new Vector2(width, fullHeight);
        if (sparkParent != null)
            UISparks.EmitBurst(wrapperRt, sparkParent, eventCamera, glowColor);

        wrapperRt.sizeDelta = new Vector2(width, StartHeight);
        SyncGlowRect();
        glowImage.color = new Color(hot.r, hot.g, hot.b, 0f);

        float t = 0f;
        while (t < Duration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / Duration);
            float eased = 1f - Mathf.Pow(1f - p, 3f); // ease-out cubic
            wrapperRt.sizeDelta = new Vector2(width, Mathf.Lerp(StartHeight, fullHeight, eased));

            float flash = FlashPulse(p);
            glowRt.localScale = Vector3.one * (1f + flash * (GlowFlashScale - 1f));
            SyncGlowRect();
            glowImage.color = new Color(hot.r, hot.g, hot.b, flash * GlowPeak);

            yield return null;
        }
        wrapperRt.sizeDelta = new Vector2(width, fullHeight);
        glowRt.localScale = Vector3.one;
        SyncGlowRect();
        glowImage.color = new Color(hot.r, hot.g, hot.b, 0f);
        routine = null;
    }

    void PlayCloseInternal(float fullHeight, Color glowColor, RectTransform sparkParent, Camera eventCamera, System.Action onComplete)
    {
        EnsureGlow();
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Collapse(fullHeight, glowColor, sparkParent, eventCamera, onComplete));
    }

    IEnumerator Collapse(float fullHeight, Color glowColor, RectTransform sparkParent, Camera eventCamera, System.Action onComplete)
    {
        float width = wrapperRt.sizeDelta.x;
        Color hot = Color.Lerp(glowColor, Color.white, 0.5f);

        wrapperRt.sizeDelta = new Vector2(width, fullHeight);
        SyncGlowRect();
        glowImage.color = new Color(hot.r, hot.g, hot.b, 0f);

        if (sparkParent != null)
            UISparks.EmitBurst(wrapperRt, sparkParent, eventCamera, glowColor);

        float t = 0f;
        while (t < Duration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / Duration);
            float eased = 1f - Mathf.Pow(1f - p, 3f); // same ease-out cubic as the open reveal, now driving the shrink
            wrapperRt.sizeDelta = new Vector2(width, Mathf.Lerp(fullHeight, StartHeight, eased));

            float flash = FlashPulse(p);
            glowRt.localScale = Vector3.one * (1f + flash * (GlowFlashScale - 1f));
            SyncGlowRect();
            glowImage.color = new Color(hot.r, hot.g, hot.b, flash * GlowPeak);

            yield return null;
        }
        wrapperRt.sizeDelta = new Vector2(width, StartHeight);
        glowRt.localScale = Vector3.one;
        SyncGlowRect();
        glowImage.color = new Color(hot.r, hot.g, hot.b, 0f);
        routine = null;
        onComplete?.Invoke();
    }
}
