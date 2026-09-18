using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws a small pixel-art checkmark icon — same runtime-
/// generated, point-filtered, upscaled-blocky approach as PlateSpriteBuilder's
/// chamfered plates (see that class), just for a badge glyph instead of a
/// panel. Exists so LevelSelectManager's "completed" badge doesn't need an
/// external image asset, and stays visually consistent with every other
/// procedurally-drawn piece of this menu's art.
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
    /// Builds (or reuses a cached) checkmark Sprite, point-filtered and
    /// upscaled by <paramref name="pixelScale"/> so it stays crisp/blocky at
    /// large on-screen sizes instead of blurring like a smooth vector tick.
    /// </summary>
    public static Sprite GetSprite(Color32 stroke, int logicalSize = 12, int strokeWidth = 2, int pixelScale = 6)
    {
        var key = new Params(stroke, logicalSize, strokeWidth, pixelScale);
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        int size = logicalSize * pixelScale;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "CheckmarkIcon",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        // Classic tick silhouette in logical-pixel space (y=0 at the bottom):
        // a short stroke down to the low point (a→b), then a longer stroke
        // back up and to the right (b→c).
        Vector2 a = new Vector2(logicalSize * 0.06f, logicalSize * 0.52f);
        Vector2 b = new Vector2(logicalSize * 0.40f, logicalSize * 0.16f);
        Vector2 c = new Vector2(logicalSize * 0.96f, logicalSize * 0.86f);

        float half = strokeWidth * 0.5f;
        var pixels = new Color32[size * size];
        var clear = new Color32(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            float ly = (y + 0.5f) / pixelScale;
            for (int x = 0; x < size; x++)
            {
                float lx = (x + 0.5f) / pixelScale;
                Vector2 p = new Vector2(lx, ly);

                float d = Mathf.Min(DistToSegment(p, a, b), DistToSegment(p, b, c));
                pixels[y * size + x] = d <= half ? stroke : clear;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f * pixelScale);
        sprite.name = "CheckmarkIcon";

        s_cache[key] = sprite;
        return sprite;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f);
        t = Mathf.Clamp01(t);
        Vector2 closest = a + ab * t;
        return Vector2.Distance(p, closest);
    }
}
