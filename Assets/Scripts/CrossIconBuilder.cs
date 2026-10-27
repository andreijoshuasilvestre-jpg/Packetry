using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws a small pixel-art "X" cross-mark icon — sampled once
/// per logical cell and upscaled into solid blocks, same as
/// CheckmarkIconBuilder (see PixelGlyph). This is the failure counterpart
/// PingCheckmarkEffect swaps in when a ping comes back unreachable instead of
/// successful, so a failed ping gets the same "materialize above the device"
/// payoff as a successful one, just recolored red with a cross instead of a
/// tick.
/// </summary>
public static class CrossIconBuilder
{
    static readonly Dictionary<Params, Sprite> s_cache = new();

    readonly struct Params : IEquatable<Params>
    {
        public readonly Color32 stroke;
        public readonly int logicalSize, strokeWidth, pixelScale;

        public Params(Color32 stroke, int logicalSize, int strokeWidth, int pixelScale)
        {
            this.stroke = stroke;
            this.logicalSize = logicalSize;
            this.strokeWidth = strokeWidth;
            this.pixelScale = pixelScale;
        }

        public bool Equals(Params o) =>
            stroke.Equals(o.stroke) && logicalSize == o.logicalSize &&
            strokeWidth == o.strokeWidth && pixelScale == o.pixelScale;
        public override bool Equals(object obj) => obj is Params p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(stroke, logicalSize, strokeWidth, pixelScale);
    }

    /// <summary>
    /// Builds (or reuses a cached) cross-mark Sprite — two corner-to-corner
    /// diagonal strokes, inset slightly so the X doesn't touch the sprite's
    /// edges, each logical cell a solid <paramref name="pixelScale"/>-texel
    /// block.
    /// </summary>
    public static Sprite GetSprite(Color32 stroke, int logicalSize = 12, int strokeWidth = 2, int pixelScale = 6)
    {
        var key = new Params(stroke, logicalSize, strokeWidth, pixelScale);
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        float inset = logicalSize * 0.14f;
        Vector2 a1 = new Vector2(inset, inset);
        Vector2 b1 = new Vector2(logicalSize - inset, logicalSize - inset);
        Vector2 a2 = new Vector2(inset, logicalSize - inset);
        Vector2 b2 = new Vector2(logicalSize - inset, inset);
        float half = strokeWidth * 0.5f;

        bool[] mask = PixelGlyph.Rasterize(logicalSize, p =>
            Mathf.Min(PixelGlyph.DistToSegment(p, a1, b1), PixelGlyph.DistToSegment(p, a2, b2)) <= half);

        var sprite = PixelGlyph.ToSprite(PixelGlyph.Flat(mask, stroke), logicalSize, pixelScale, "CrossIcon");
        s_cache[key] = sprite;
        return sprite;
    }
}
