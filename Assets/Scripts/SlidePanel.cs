using System;
using System.Collections;
using UnityEngine;

/// <summary>Screen edge a SlidePanel enters from / exits toward.</summary>
public enum SlideEdge { Left, Right, Top, Bottom }

/// <summary>
/// Directional slide transition for a UI panel (the main menu's MenuRoot,
/// LevelSelectManager's LevelSelect_Panel, NetworkUIManager's sidebar/status
/// bar, TaskUIManager's task panel, ...). Slides in from off-screen toward
/// rest position with ease-out (fast start, gentle arrival) and slides back
/// out with ease-in (mirrors slide-in in reverse) when a button executes a
/// function that moves to a different screen.
///
/// Works on any anchor setup — full-stretch (MenuRoot) or point-anchored
/// (a corner-pinned panel like TaskPanel) — anchoredPosition moves the whole
/// rect the same way in both cases.
///
/// Purely presentational: callers decide when to call SlideIn/SlideOut and
/// what happens on completion (toggling the next panel active, actually
/// loading a scene, ...) via the onComplete callback.
/// </summary>
public class SlidePanel : MonoBehaviour
{
    [SerializeField] SlideEdge edge = SlideEdge.Bottom;
    [SerializeField] float distance = 260f; // how far off rest position the panel starts/ends at
    [SerializeField] float duration = 0.35f;

    RectTransform rt;
    Vector2 restPosition;
    Coroutine routine;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        restPosition = rt.anchoredPosition;
    }

    /// <summary>Call right after AddComponent to set which edge this panel enters from/exits toward.</summary>
    public void Configure(SlideEdge edge, float distance = 260f, float duration = 0.35f)
    {
        this.edge     = edge;
        this.distance = distance;
        this.duration = duration;
    }

    public void SlideIn(Action onComplete = null)
    {
        Vector2 from = restPosition + EdgeOffset();
        Restart(from, restPosition, EaseOutCubic, onComplete);
    }

    public void SlideOut(Action onComplete = null)
    {
        Vector2 from = rt != null ? rt.anchoredPosition : restPosition;
        Vector2 to   = restPosition + EdgeOffset();
        Restart(from, to, EaseInCubic, onComplete);
    }

    Vector2 EdgeOffset()
    {
        switch (edge)
        {
            case SlideEdge.Left:  return Vector2.left  * distance;
            case SlideEdge.Right: return Vector2.right * distance;
            case SlideEdge.Top:   return Vector2.up    * distance;
            default:              return Vector2.down  * distance;
        }
    }

    void Restart(Vector2 from, Vector2 to, Func<float, float> ease, Action onComplete)
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Animate(from, to, ease, onComplete));
    }

    IEnumerator Animate(Vector2 from, Vector2 to, Func<float, float> ease, Action onComplete)
    {
        rt.anchoredPosition = from;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = ease(Mathf.Clamp01(t / duration));
            rt.anchoredPosition = Vector2.LerpUnclamped(from, to, p);
            yield return null;
        }
        rt.anchoredPosition = to;
        routine = null;
        onComplete?.Invoke();
    }

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    static float EaseInCubic(float t)  => t * t * t;

    /// <summary>
    /// Infers which screen edge a RectTransform is anchored nearest to, for
    /// entrance animations that approach from "wherever this panel already
    /// lives" (e.g. a left sidebar slides in from the left). Horizontal edges
    /// take priority over vertical ones when a rect is pinned to a side (not
    /// stretched) — matches how this project's HUD panels are laid out (side
    /// panels pinned by x, bars stretched full-width and pinned by y). Falls
    /// back to Bottom for anything centered/stretched on both axes.
    /// </summary>
    public static SlideEdge EdgeFromAnchor(RectTransform rt)
    {
        bool pinnedLeft  = rt.anchorMin.x <= 0f && rt.anchorMax.x <= 0f;
        bool pinnedRight = rt.anchorMin.x >= 1f && rt.anchorMax.x >= 1f;
        if (pinnedLeft)  return SlideEdge.Left;
        if (pinnedRight) return SlideEdge.Right;

        bool pinnedTop    = rt.anchorMin.y >= 1f && rt.anchorMax.y >= 1f;
        bool pinnedBottom = rt.anchorMin.y <= 0f && rt.anchorMax.y <= 0f;
        if (pinnedTop)    return SlideEdge.Top;
        if (pinnedBottom) return SlideEdge.Bottom;

        return SlideEdge.Bottom;
    }
}
