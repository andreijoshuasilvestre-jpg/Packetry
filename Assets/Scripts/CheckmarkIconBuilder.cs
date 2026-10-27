using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws a small pixel-art checkmark icon — sampled once per
/// logical cell and upscaled into solid blocks (see PixelGlyph), the same
/// pixel art as PlateSpriteBuilder's plates and StarIconBuilder's stars.
/// Exists so the task list, the packet-type picker and PingCheckmarkEffect
/// don't need an external image asset.
///
/// The tick is rasterized from two line segments (short down-stroke, longer
/// up-stroke) rather than a hand-authored bitmap — reliable at any logical
/// grid size instead of depending on someone getting an ASCII sketch of a
/// checkmark exactly right pixel-by-pixel.
/// </summary>
public static class CheckmarkIconBuilder
{
    static readonly Dictionary<Params, Sprite> s_cache = new();

    readonly struct Params : IEquatable<Params>
    {
        public readonly Color32 stroke;
        public readonly int logicalSize, strokeWidth, pixelScale;
        public readonly bool shaded;

        public Params(Color32 stroke, int logicalSize, int strokeWidth, int pixelScale, bool shaded)
        {
            this.stroke = stroke;
            this.logicalSize = logicalSize;
            this.strokeWidth = strokeWidth;
            this.pixelScale = pixelScale;
            this.shaded = shaded;
        }

        public bool Equals(Params o) =>
            stroke.Equals(o.stroke) && logicalSize == o.logicalSize &&
            strokeWidth == o.strokeWidth && pixelScale == o.pixelScale && shaded == o.shaded;
        public override bool Equals(object obj) => obj is Params p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(stroke, logicalSize, strokeWidth, pixelScale, shaded);
    }

    /// <summary>
    /// Builds (or reuses a cached) checkmark Sprite, each logical cell a
    /// solid <paramref name="pixelScale"/>-texel block.
    /// </summary>
    /// <param name="shaded">Draws the tick in StarIconBuilder's style — dark
    /// outline plus highlight/shadow edge cells, all derived from
    /// <paramref name="stroke"/> — instead of one flat color. The tick is
    /// drawn on a grid two cells smaller so the outline fits inside
    /// <paramref name="logicalSize"/>. Leave it off for a glyph that gets
    /// tinted at runtime (PingCheckmarkEffect, the task list).</param>
    public static Sprite GetSprite(Color32 stroke, int logicalSize = 12, int strokeWidth = 2, int pixelScale = 6,
                                   bool shaded = false)
    {
        var key = new Params(stroke, logicalSize, strokeWidth, pixelScale, shaded);
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        Color32[] cells;
        if (shaded)
        {
            int inner = logicalSize - 2;
            bool[] mask = PixelGlyph.Pad(RasterizeTick(inner, strokeWidth), inner, 1);
            cells = PixelGlyph.Shaded(mask, logicalSize, stroke);
        }
        else
        {
            cells = PixelGlyph.Flat(RasterizeTick(logicalSize, strokeWidth), stroke);
        }

        var sprite = PixelGlyph.ToSprite(cells, logicalSize, pixelScale, "CheckmarkIcon");
        s_cache[key] = sprite;
        return sprite;
    }

    // Classic tick silhouette in logical-pixel space (y=0 at the bottom): a
    // short stroke down to the low point (a→b), then a longer stroke back up
    // and to the right (b→c).
    static bool[] RasterizeTick(int logicalSize, int strokeWidth)
    {
        Vector2 a = new Vector2(logicalSize * 0.06f, logicalSize * 0.52f);
        Vector2 b = new Vector2(logicalSize * 0.40f, logicalSize * 0.16f);
        Vector2 c = new Vector2(logicalSize * 0.96f, logicalSize * 0.86f);
        float half = strokeWidth * 0.5f;

        return PixelGlyph.Rasterize(logicalSize, p =>
            Mathf.Min(PixelGlyph.DistToSegment(p, a, b), PixelGlyph.DistToSegment(p, b, c)) <= half);
    }
}
