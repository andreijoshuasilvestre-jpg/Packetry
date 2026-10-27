using System;
using UnityEngine;

/// <summary>Which side of its target a hover tooltip opens on — it flips to
/// the opposite side when there isn't room on screen.</summary>
public enum TooltipSide { Right, Left, Above, Below }

/// <summary>
/// What a hover tooltip shows. Everything but Title is optional — an empty
/// field is simply left out of the card, so a one-line hint and a full
/// command explanation share the same card.
/// </summary>
public sealed class TooltipContent
{
    public string Title;
    /// <summary>Short category pill beside the title, e.g. "MOVE" or "CHANGE".</summary>
    public string Tag;
    public Color  TagColor = GameTheme.Info;
    /// <summary>Terminal-green line under the title, e.g. a prompt change "Router&gt; → Router#".</summary>
    public string Subtitle;
    /// <summary>The explanation itself (TMP rich text).</summary>
    public string Body;
    /// <summary>A terminal-green example line on a black inset.</summary>
    public string Example;
    /// <summary>Dim hint line at the bottom — shortcuts, "right-click to cancel", etc.</summary>
    public string Footer;
}

/// <summary>
/// Explain-on-hover for any UI element — the way the game teaches what a
/// button, tool or command does without a guide having to say it: rest the
/// pointer on something for a moment and a card says what it is, what it's
/// for and how to use it. Never blocks anything (the card ignores raycasts),
/// hides on any click, and once one card has shown, moving straight onto the
/// next target shows its card without the delay — the usual "warm" tooltip
/// behavior.
///
/// Content is a callback evaluated when the card opens, not a baked string,
/// so a target whose meaning depends on state (a CLI command's prompt change
/// depends on the current mode) always shows what's true right now.
/// </summary>
public static class HoverTooltip
{
    public const float DefaultDelay = 0.45f;

    /// <summary>Adds (or reconfigures) hover explanation on `target`. The
    /// target, or one of its children, needs a raycastable Graphic.</summary>
    public static HoverTooltipTrigger Attach(GameObject target, Func<TooltipContent> content,
        TooltipSide side = TooltipSide.Right, float delay = DefaultDelay)
    {
        var trigger = target.GetComponent<HoverTooltipTrigger>();
        if (trigger == null) trigger = target.AddComponent<HoverTooltipTrigger>();
        trigger.Content = content;
        trigger.Side    = side;
        trigger.Delay   = delay;
        return trigger;
    }

    /// <summary>Hides whatever card is showing right now.</summary>
    public static void Hide()
    {
        if (HoverTooltipView.Instance != null) HoverTooltipView.Instance.HideNow(warm: false);
    }

    internal static void Enter(HoverTooltipTrigger trigger) => HoverTooltipView.GetOrCreate().Enter(trigger);

    internal static void Exit(HoverTooltipTrigger trigger)
    {
        if (HoverTooltipView.Instance != null) HoverTooltipView.Instance.Exit(trigger);
    }
}
