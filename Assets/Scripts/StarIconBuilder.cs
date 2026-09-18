using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws a small pixel-art 5-point star icon — same runtime-
/// generated, point-filtered, upscaled-blocky approach as CheckmarkIconBuilder/
/// PlateSpriteBuilder (see those classes). Exists so LevelCompletePanel's star
/// rating row doesn't depend on a "★" glyph actually existing in whatever font
/// is assigned — TMP's fallback for a missing glyph renders as an oversized,
/// badly-positioned notdef box (reported as "squares instead of stars, way
/// off from the panel"), not a scaled-down star, so a bitmap glyph can't fix
/// this no matter which font is picked. A dedicated Image + baked sprite
/// sidesteps the whole font-coverage question.
///
/// Fill color is baked into the sprite (not tinted via Image.color) so a
/// filled and an empty star are two distinct cached sprites, same convention
/// CheckmarkIconBuilder uses for its stroke color.
/// </summary>
public static class StarIconBuilder
{
    static readonly Dictionary<Params, Sprite> s_cache = new();

    readonly struct Params : IEquatable<Params>
    {
        public readonly Color32 fill;
        public readonly int logicalSize, pixelScale;

        public Params(Color32 fill, int logicalSize, int pixelScale)
        {
            this.fill = fill;
            this.logicalSize = logicalSize;
            this.pixelScale = pixelScale;
        }

        public bool Equals(Params o) =>
            fill.Equals(o.fill) && logicalSize == o.logicalSize && pixelScale == o.pixelScale;
        public override bool Equals(object obj) => obj is Params p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(fill, logicalSize, pixelScale);
    }

    /// <summary>
    /// Builds (or reuses a cached) upright 5-point star Sprite, point-filtered
    /// and upscaled by <paramref name="pixelScale"/> so it stays crisp/blocky
    /// at large on-screen sizes instead of blurring like a smooth vector star.
    /// </summary>
    public static Sprite GetSprite(Color32 fill, int logicalSize = 24, int pixelScale = 6)
    {
        var key = new Params(fill, logicalSize, pixelScale);
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        int size = logicalSize * pixelScale;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "StarIcon",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        // 10 vertices alternating outer/inner radius, starting straight up
        // (angle -90°) so the star points up — logical space has y=0 at the
        // bottom (matches CheckmarkIconBuilder's convention), which lines up
        // directly with the texture row index used below.
        Vector2 center = new Vector2(logicalSize * 0.5f, logicalSize * 0.5f);
        float outerR = logicalSize * 0.48f;
        float innerR = outerR * 0.40f;
        var verts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float angle = -Mathf.PI / 2f + i * Mathf.PI / 5f;
            float r = (i % 2 == 0) ? outerR : innerR;
            verts[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
        }

        var pixels = new Color32[size * size];
        var clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
        {
            float ly = (y + 0.5f) / pixelScale;
            for (int x = 0; x < size; x++)
            {
                float lx = (x + 0.5f) / pixelScale;
                pixels[y * size + x] = PointInPolygon(new Vector2(lx, ly), verts) ? fill : clear;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f * pixelScale);
        sprite.name = "StarIcon";

        s_cache[key] = sprite;
        return sprite;
    }

    // Standard even-odd ray-casting point-in-polygon test.
    static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        int j = poly.Length - 1;
        for (int i = 0; i < poly.Length; i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
            j = i;
        }
        return inside;
    }
}
