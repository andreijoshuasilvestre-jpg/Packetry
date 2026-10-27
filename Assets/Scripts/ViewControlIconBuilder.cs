using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pixel-art glyphs for ViewControlsHud's buttons, sampled once per logical
/// cell through PixelGlyph like the game's other procedural icons.
///
/// The turn glyphs show what the player will actually SEE, not what the
/// camera does: a turntable (a flattened ring) turning the way the scene is
/// about to turn on screen. Q/E were hard to read precisely because "turn the
/// camera left" and "the room spins clockwise" describe the same move — the
/// arrow sidesteps that by showing the spin itself.
/// </summary>
public static class ViewControlIconBuilder
{
    const int Size = 17;          // logical cells, before the 1-cell outline margin
    const int TexelsPerCell = 4;

    static readonly Color32 GlyphFill = new Color32(236, 240, 248, 255);
    static readonly Dictionary<string, Sprite> s_cache = new();

    /// <summary>Turntable + arrow; swingRight = the near edge moves right (E).</summary>
    public static Sprite GetTurnSprite(bool swingRight) =>
        Cached(swingRight ? "TurnRight" : "TurnLeft", p => Turn(p, swingRight));

    /// <summary>A target reticle — "back to the starting view".</summary>
    public static Sprite GetResetSprite() => Cached("ResetView", Reset);

    /// <summary>A magnifying glass with a plus (zoomIn) or minus in its lens.</summary>
    public static Sprite GetZoomSprite(bool zoomIn) =>
        Cached(zoomIn ? "ZoomIn" : "ZoomOut", p => Zoom(p, zoomIn));

    static Sprite Cached(string name, System.Func<Vector2, bool> covers)
    {
        if (s_cache.TryGetValue(name, out Sprite cached) && cached != null) return cached;
        bool[] mask = PixelGlyph.Pad(PixelGlyph.Rasterize(Size, covers), Size, 1);
        var sprite = PixelGlyph.ToSprite(PixelGlyph.Shaded(mask, Size + 2, GlyphFill), Size + 2, TexelsPerCell, name);
        s_cache[name] = sprite;
        return sprite;
    }

    // A turntable seen at an angle: an ellipse, open at the top, with an
    // arrowhead at one end. Drawn clockwise (↻) — the near edge (bottom of the
    // ellipse) moving left, which is what Q does to the scene — and mirrored
    // into ↺ for E.
    const float RingRx = 6.8f, RingRy = 5.0f, RingCenterY = 7.6f;
    const float RingInner = 0.60f, RingOuter = 1.0f;
    const float GapFrom = 20f, GapTo = 145f;   // degrees, CCW from +x: the opening at the top
    const float HeadLength = 4.8f, HeadHalfWidth = 3.8f;

    static bool Turn(Vector2 p, bool swingRight)
    {
        if (swingRight) p.x = Size - p.x;

        var c = new Vector2(8.5f, RingCenterY);
        var q = new Vector2((p.x - c.x) / RingRx, (p.y - c.y) / RingRy);
        float d = q.magnitude;
        float angle = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;
        if (d >= RingInner && d <= RingOuter && (angle <= GapFrom || angle >= GapTo)) return true;

        // Arrowhead on the gap's left end, pointing clockwise into the gap.
        float a = GapTo * Mathf.Deg2Rad;
        float mid = (RingInner + RingOuter) * 0.5f;
        var head = new Vector2(c.x + mid * RingRx * Mathf.Cos(a), c.y + mid * RingRy * Mathf.Sin(a));
        Vector2 dir = new Vector2(RingRx * Mathf.Sin(a), -RingRy * Mathf.Cos(a)).normalized; // clockwise tangent
        var normal = new Vector2(-dir.y, dir.x);
        return InTriangle(p, head + dir * HeadLength, head + normal * HeadHalfWidth, head - normal * HeadHalfWidth);
    }

    static bool Reset(Vector2 p)
    {
        var c = new Vector2(8.5f, 8.5f);
        float r = Vector2.Distance(p, c);
        if (r >= 4.2f && r <= 5.6f) return true;          // ring
        if (r <= 1.6f) return true;                       // center dot
        bool vertical   = Mathf.Abs(p.x - c.x) <= 0.6f && r >= 5.6f && r <= 8.2f;
        bool horizontal = Mathf.Abs(p.y - c.y) <= 0.6f && r >= 5.6f && r <= 8.2f;
        return vertical || horizontal;                    // four ticks
    }

    // Lens up and to the left, its handle running down to the bottom-right
    // corner, the sign centred in the lens.
    // Centred on a cell so the sign is one crisp cell thick, and short enough
    // to leave a gap before the rim — the outline Shaded() draws around both
    // shapes otherwise closes that gap and merges the plus into a blob.
    static readonly Vector2 LensCenter = new Vector2(7.5f, 9.5f);
    const float LensInner = 4.7f, LensOuter = 6.2f;
    const float HandleFrom = 6.0f, HandleTo = 11f, HandleHalfWidth = 1.0f;
    const float SignHalfLength = 2.5f, SignHalfWidth = 0.5f;

    static bool Zoom(Vector2 p, bool zoomIn)
    {
        Vector2 d = p - LensCenter;
        float r = d.magnitude;
        if (r >= LensInner && r <= LensOuter) return true;  // lens rim

        Vector2 axis = new Vector2(1f, -1f).normalized;     // toward the bottom-right
        float along  = Vector2.Dot(d, axis);
        float across = Mathf.Abs(d.x * axis.y - d.y * axis.x);
        if (along >= HandleFrom && along <= HandleTo && across <= HandleHalfWidth) return true;

        bool bar  = Mathf.Abs(d.y) <= SignHalfWidth && Mathf.Abs(d.x) <= SignHalfLength;
        bool stem = Mathf.Abs(d.x) <= SignHalfWidth && Mathf.Abs(d.y) <= SignHalfLength;
        return bar || (zoomIn && stem);
    }

    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }
}
