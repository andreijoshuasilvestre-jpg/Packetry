using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws a small pixel-art "globe with a lat/long grid" icon —
/// same runtime-generated, point-filtered, upscaled-blocky approach as
/// CheckmarkIconBuilder (see that class). Used by PacketVisual as the glyph
/// for HTTP request/response packets in flight.
/// </summary>
public static class GridGlobeIconBuilder
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
    /// Builds (or reuses a cached) globe Sprite: an outer circle ring, one
    /// horizontal "equator" line, and one vertical "meridian" ellipse — enough
    /// grid lines to read as a globe rather than a plain circle even at the
    /// small on-screen size a traveling packet glyph gets.
    /// </summary>
    public static Sprite GetSprite(Color32 stroke, int logicalSize = 14, int strokeWidth = 1, int pixelScale = 8)
    {
        var key = new Params(stroke, logicalSize, strokeWidth, pixelScale);
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        int size = logicalSize * pixelScale;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "GridGlobeIcon",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        Vector2 center = new Vector2(logicalSize * 0.5f, logicalSize * 0.5f);
        float radius = logicalSize * 0.42f;
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
                float distFromCenter = Vector2.Distance(p, center);
                if (distFromCenter > radius + half) { pixels[y * size + x] = clear; continue; }

                bool onRing     = Mathf.Abs(distFromCenter - radius) <= half;
                bool onEquator  = Mathf.Abs(p.y - center.y) <= half;
                bool onMeridian = Mathf.Abs(EllipseBoundary(p, center, radius * 0.5f, radius)) <= half;
                pixels[y * size + x] = (onRing || onEquator || onMeridian) ? stroke : clear;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f * pixelScale);
        sprite.name = "GridGlobeIcon";

        s_cache[key] = sprite;
        return sprite;
    }

    // Approximate signed distance to an axis-aligned ellipse boundary (good
    // enough for pixel art at this resolution) — used for the globe's curved
    // vertical meridian line, semiA/semiB being its horizontal/vertical radii.
    static float EllipseBoundary(Vector2 p, Vector2 center, float semiA, float semiB)
    {
        float nx = (p.x - center.x) / Mathf.Max(semiA, 0.0001f);
        float ny = (p.y - center.y) / Mathf.Max(semiB, 0.0001f);
        float t = Mathf.Sqrt(nx * nx + ny * ny);
        return (t - 1f) * Mathf.Min(semiA, semiB);
    }
}
