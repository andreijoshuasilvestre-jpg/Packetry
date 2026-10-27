using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One end of a guidance visual (DragPathGuide's trail, a UIPulseMarker) —
/// either a UI element or a point in the 3D world, re-resolved every frame so
/// it follows things that move (a CLI panel bobbing, a device mid-drag, the
/// camera panning) and quietly reports "not there" for things that don't
/// exist yet (a palette chip for a command not in the current mode, a flyout
/// item while the flyout is closed, a device not placed yet) — the guide just
/// hides that end until it resolves again, so callers never need to toggle
/// it themselves.
/// </summary>
public readonly struct GuideAnchor
{
    readonly Func<RectTransform> rect;
    readonly Func<Vector3?> world;

    GuideAnchor(Func<RectTransform> rect, Func<Vector3?> world)
    {
        this.rect = rect;
        this.world = world;
    }

    /// <summary>A UI element on a ScreenSpaceOverlay canvas.</summary>
    public static GuideAnchor Ui(Func<RectTransform> rect) => new GuideAnchor(rect, null);

    /// <summary>A world-space point (typically a WorldPulseMarker's own spot —
    /// the trail stops short of it so the two don't overlap).</summary>
    public static GuideAnchor World(Func<Vector3?> point) => new GuideAnchor(null, point);

    public bool IsWorld => world != null;
    public bool IsDefined => rect != null || world != null;

    /// <summary>The live RectTransform for a UI anchor, or null when it isn't
    /// resolvable/visible right now. Always null for a world anchor.</summary>
    public RectTransform ResolveRect()
    {
        if (rect == null) return null;
        var rt = rect();
        return rt != null && rt.gameObject.activeInHierarchy ? rt : null;
    }

    /// <summary>For a UI anchor sitting inside a ScrollRect: true when it's
    /// scrolled (even partly) out of that list's visible viewport, with
    /// direction -1 when it's below the visible part (scroll down to reach it)
    /// or +1 when above. False for world anchors and anything not in a list.</summary>
    public bool TryGetScrollClip(out ScrollRect scroll, out int direction)
    {
        scroll = null;
        direction = 0;
        var rt = ResolveRect();
        if (rt == null) return false;

        scroll = rt.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.viewport == null) return false;

        const float Slack = 2f; // px — a sliver of rounding doesn't count as hidden
        Rect item = ScreenOccupancy.ScreenRectOf(rt);
        Rect view = ScreenOccupancy.ScreenRectOf(scroll.viewport);
        if (item.yMin < view.yMin - Slack) direction = -1;
        else if (item.yMax > view.yMax + Slack) direction = 1;
        return direction != 0;
    }

    /// <summary>Screen-space (pixel) footprint of this anchor right now. A world
    /// anchor projects to a zero-size rect at its point; `worldRadiusPx` is then
    /// the on-screen size of roughly half a world unit there, so callers can keep
    /// a sensible gap from whatever marker is drawn at that spot.</summary>
    public bool TryGetScreenRect(Camera cam, out Rect screenRect, out float worldRadiusPx)
    {
        screenRect = default;
        worldRadiusPx = 0f;

        if (rect != null)
        {
            var rt = ResolveRect();
            if (rt == null) return false;
            screenRect = ScreenOccupancy.ScreenRectOf(rt);
            return true;
        }

        if (world == null || cam == null) return false;
        Vector3? p = world();
        if (!p.HasValue) return false;

        Vector3 sp = cam.WorldToScreenPoint(p.Value);
        if (sp.z <= 0f) return false; // behind the camera

        Vector3 edge = cam.WorldToScreenPoint(p.Value + cam.transform.right * 0.5f);
        worldRadiusPx = Vector2.Distance(sp, edge);
        screenRect = new Rect(sp.x, sp.y, 0f, 0f);
        return true;
    }
}

/// <summary>
/// A "drag from here to there" route for DragPathGuide — the thing to grab,
/// where it goes, and (optionally) a live check for "the player is doing the
/// drag right now", during which the trail and the grab-end marker step back
/// so only the destination is still being pointed at.
/// </summary>
public class DragGuideRoute
{
    public GuideAnchor From;
    public GuideAnchor To;
    public Func<bool> InProgress;
    /// <summary>Trail tint — null for the default marker yellow. Set it to match
    /// ends that are themselves tinted (e.g. the table-corner markers).</summary>
    public Color? Color;
    /// <summary>Optional follow-up once the drag has landed: while Done reports
    /// true, the trail and both end markers step aside and a clicking-mouse marker
    /// points at Then instead — "now press this" (e.g. the CLI's Run button once
    /// the dragged command is in the queue). Back to the trail if Done turns false
    /// again (the queue was cleared, or the run failed).</summary>
    public Func<bool> Done;
    public GuideAnchor Then;
    /// <summary>Always a straight trail, never bowed around what's in the way —
    /// for a drag that can only run in a straight line (a table edge).</summary>
    public bool Straight;

    public DragGuideRoute(GuideAnchor from, GuideAnchor to, Func<bool> inProgress = null, Color? color = null,
        Func<bool> done = null, GuideAnchor then = default, bool straight = false)
    {
        From = from;
        To = to;
        InProgress = inProgress;
        Color = color;
        Done = done;
        Then = then;
        Straight = straight;
    }
}
