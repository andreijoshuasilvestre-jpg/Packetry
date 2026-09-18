using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws a small pixel-art "X" cross-mark icon — same runtime-
/// generated, point-filtered, upscaled-blocky approach as CheckmarkIconBuilder
/// (see that class). This is the failure counterpart PingCheckmarkEffect swaps
/// in when a ping comes back unreachable instead of successful, so a failed
/// ping gets the same "materialize above the device" payoff as a successful
/// one, just recolored red with a cross instead of a tick.
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
    /// edges. Same point-filtered upscale as CheckmarkIconBuilder so it stays
    /// crisp/blocky at large on-screen sizes instead of blurring.
    /// </summary>
    public static Sprite GetSprite(Color32 stroke, int logicalSize = 12, int strokeWidth = 2, int pixelScale = 6)
    {
        var key = new Params(stroke, logicalSize, strokeWidth, pixelScale);
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        int size = logicalSize * pixelScale;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "CrossIcon",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        float inset = logicalSize * 0.14f;
        Vector2 a1 = new Vector2(inset, inset);
        Vector2 b1 = new Vector2(logicalSize - inset, logicalSize - inset);
        Vector2 a2 = new Vector2(inset, logicalSize - inset);
        Vector2 b2 = new Vector2(logicalSize - inset, inset);

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

                float d = Mathf.Min(DistToSegment(p, a1, b1), DistToSegment(p, a2, b2));
                pixels[y * size + x] = d <= half ? stroke : clear;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f * pixelScale);
        sprite.name = "CrossIcon";

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
