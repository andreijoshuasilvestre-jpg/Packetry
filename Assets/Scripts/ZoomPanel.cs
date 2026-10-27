using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Scale-based entrance/exit for a UI panel — zooms from 0 up to its resting
/// scale (or the reverse) instead of sliding in from an offscreen edge (see
/// SlidePanel). Used by LevelSelectManager's LevelSelect_Panel: a basic
/// zoom-in on Open(), zoom-out on ClosePanel()/OnLevelClicked, in place of
/// the slide it used to share with the main menu. Every other centered
/// popup panel now shares it too — level description popup,
/// ConfirmDialogManager, both SettingsPanelManager surfaces,
/// LevelCompletePanel — while edge-attached chrome (menu buttons, HUD
/// header, notification toast) keeps SlidePanel.
///
/// Deliberately a separate small component rather than a mode on SlidePanel
/// — the two animate a different property (localScale vs anchoredPosition)
/// and are swapped independently per panel, so keeping them apart avoids one
/// component branching between two unrelated animation styles.
///
/// Same call shape as SlidePanel (an in/out pair, optional onComplete) so a
/// caller already used to that pattern only has to learn the new method
/// names.
/// </summary>
public class ZoomPanel : MonoBehaviour
{
    [SerializeField] float duration = 0.28f;

    RectTransform rt;
    Coroutine routine;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
    }

    // The prompt sound lives here (and in PanelRevealFX.Play) rather than in
    // each panel's own Show(), so any panel that enters this way gets it — a
    // panel with its own entrance sound (LevelCompletePanel) just swaps it.
    public SfxId PromptSfx { get; set; } = SfxId.PanelPrompt;

    public void ZoomIn(Action onComplete = null)
    {
        AudioManager.Instance?.PlaySfx(PromptSfx);
        Restart(0f, 1f, EaseOutCubic, onComplete);
    }

    // Starts from wherever the scale currently is (same as SlidePanel.
    // SlideOut) — a dismiss clicked while ZoomIn is still growing the panel
    // shrinks it from there instead of popping it back to full size first.
    public void ZoomOut(Action onComplete = null)
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        Restart(rt.localScale.x, 0f, EaseInCubic, onComplete);
    }

    void Restart(float from, float to, Func<float, float> ease, Action onComplete)
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Animate(from, to, ease, onComplete));
    }

    IEnumerator Animate(float from, float to, Func<float, float> ease, Action onComplete)
    {
        rt.localScale = Vector3.one * from;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = ease(Mathf.Clamp01(t / duration));
            rt.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, p);
            yield return null;
        }
        rt.localScale = Vector3.one * to;
        routine = null;
        onComplete?.Invoke();
    }

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    static float EaseInCubic(float t)  => t * t * t;
}
