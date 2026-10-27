using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What kind of thing an IScreenOccupant is — lets each consumer pick which
/// occupants it actually cares about (see ScreenOccupancy.Collect's mask)
/// instead of every floating element dodging every other one, which would
/// chase itself in circles (e.g. the CLI panel sliding away from the very
/// markers that point at its own chips, those markers following it, ...).
/// </summary>
[System.Flags]
public enum OccupantLayer
{
    /// <summary>Fixed HUD chrome — sidebar/top-bar buttons, the task panel,
    /// the tutorial's Skip button.</summary>
    Hud = 1 << 0,
    /// <summary>Interactive panels/popups — the CLI panel and its command
    /// builder, the address/interface/packet-type pickers.</summary>
    Panel = 1 << 1,
    /// <summary>Screen-space guidance — UI markers, drag trails.</summary>
    Guide = 1 << 2,
    /// <summary>A keep-out zone an element claims for itself so FREELY
    /// positioned panels slide out of its way (see CLIPanelManager's combo
    /// placement) — the tutorial callout reserves its own home spot this way,
    /// rather than its live position, so the two never chase each other.</summary>
    Reservation = 1 << 3,
    /// <summary>A WorldPulseMarker's projected footprint — guidance too, but
    /// out in the 3D scene, often on the very device whose panel is now open
    /// (the Router marker behind its own CLI), so the tutorial callout weighs
    /// covering it like covering a panel rather than a trail or UI marker.</summary>
    WorldGuide = 1 << 4,

    All = Hud | Panel | Guide | Reservation | WorldGuide,
}

/// <summary>Anything that covers part of the screen and wants other UI to
/// know about it — see ScreenOccupancy.</summary>
public interface IScreenOccupant
{
    OccupantLayer Layer { get; }

    /// <summary>Appends every screen-space (pixel, bottom-left origin) rect
    /// this occupant currently covers. Append nothing while hidden.</summary>
    void CollectScreenRects(List<Rect> into);
}

/// <summary>
/// Single registry of "what's currently covering which part of the screen",
/// so a floating element (the tutorial callout, a UI marker's arrow, the CLI
/// panel) can place itself where it isn't covering something else — instead
/// of each one being tuned with its own hand-picked pixel offset that only
/// works until something else moves (see TutorialUIManager's old
/// PanelOffsetX/offsetX/clearFlyoutBelow special cases, which all patched
/// one instance of this same bug).
///
/// Everything is in SCREEN PIXELS (bottom-left origin) — the common space
/// every ScreenSpaceOverlay canvas's world corners already live in, and what
/// a world point projects to — so occupants on different canvases (each with
/// its own CanvasScaler) and world-space markers all compare directly.
/// Consumers convert to their own canvas units themselves.
///
/// Plain RectTransforms register through the OccupiesScreen component (which
/// follows its GameObject's active state automatically); anything with a
/// computed footprint (markers, trails) implements IScreenOccupant itself.
/// </summary>
public static class ScreenOccupancy
{
    public struct Entry
    {
        public IScreenOccupant Owner;
        public Rect Rect;
    }

    static readonly List<IScreenOccupant> occupants = new();
    static readonly List<Rect> scratch = new();

    public static void Register(IScreenOccupant occupant)
    {
        if (occupant != null && !occupants.Contains(occupant)) occupants.Add(occupant);
    }

    public static void Unregister(IScreenOccupant occupant) => occupants.Remove(occupant);

    /// <summary>Appends every rect covered by an occupant on one of `layers`,
    /// skipping `exclude` (the caller itself, typically).</summary>
    public static void Collect(List<Entry> into, OccupantLayer layers, IScreenOccupant exclude = null)
    {
        // Destroyed MonoBehaviour occupants whose OnDisable never ran (scene
        // teardown order) — compare against Unity's null, not C#'s.
        occupants.RemoveAll(o => o == null || (o is Object uo && uo == null));

        foreach (var o in occupants)
        {
            if (o == exclude || (o.Layer & layers) == 0) continue;
            scratch.Clear();
            o.CollectScreenRects(scratch);
            foreach (var r in scratch)
                if (r.width > 0f && r.height > 0f) into.Add(new Entry { Owner = o, Rect = r });
        }
    }

    public static float OverlapArea(Rect a, Rect b)
    {
        float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return w > 0f && h > 0f ? w * h : 0f;
    }

    public static float OverlapArea(Rect a, List<Entry> entries)
    {
        float total = 0f;
        foreach (var e in entries) total += OverlapArea(a, e.Rect);
        return total;
    }

    /// <summary>Screen-space bounds of a RectTransform on a ScreenSpaceOverlay
    /// canvas (its world corners already ARE screen pixels there).</summary>
    public static Rect ScreenRectOf(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    /// <summary>Slides `rect` horizontally (never vertically) by whatever
    /// amount within [minShift, maxShift] leaves it overlapping the fewest
    /// pixels of `entries`, preferring the smallest move on a tie — used by a
    /// freely-positioned panel to step aside from a Reservation.</summary>
    public static float BestHorizontalShift(Rect rect, List<Entry> entries, float minShift, float maxShift, float gap)
    {
        float bestShift = Mathf.Clamp(0f, minShift, maxShift);
        float bestOverlap = OverlapArea(Shifted(rect, bestShift), entries);
        if (bestOverlap <= 0f) return bestShift;

        foreach (var e in entries)
        {
            TryShift(e.Rect.xMax + gap - rect.xMin);  // just right of it
            TryShift(e.Rect.xMin - gap - rect.xMax);  // just left of it
        }
        return bestShift;

        void TryShift(float shift)
        {
            shift = Mathf.Clamp(shift, minShift, maxShift);
            float overlap = OverlapArea(Shifted(rect, shift), entries);
            if (overlap < bestOverlap - 0.5f ||
                (Mathf.Abs(overlap - bestOverlap) <= 0.5f && Mathf.Abs(shift) < Mathf.Abs(bestShift)))
            {
                bestOverlap = overlap;
                bestShift = shift;
            }
        }
    }

    static Rect Shifted(Rect r, float dx) => new Rect(r.x + dx, r.y, r.width, r.height);
}
