using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally-drawn placeholder glyph for a sidebar device button whose real
/// artwork (Assets/Resources/UIIcons/&lt;name&gt;.png) hasn't been authored yet
/// (see the entries list in NetworkUIManager.BuildSidebar). Draws a
/// small stacked "server rack" icon (three unit bars, each with a status LED)
/// using the same runtime-baked, point-filtered sprite approach as
/// CheckmarkIconBuilder/StarIconBuilder, so a missing icon reads as an
/// intentional placeholder glyph instead of NetworkUIManager.CreateSidebarButton
/// silently falling back to a blank white box.
/// </summary>
public static class DeviceIconFallbackBuilder
{
    static readonly Dictionary<Params, Sprite> s_cache = new();

    readonly struct Params : IEquatable<Params>
    {
        public readonly Color32 stroke;
        public readonly int logicalSize, pixelScale;

        public Params(Color32 stroke, int logicalSize, int pixelScale)
        {
            this.stroke = stroke;
            this.logicalSize = logicalSize;
            this.pixelScale = pixelScale;
        }

        public bool Equals(Params o) =>
            stroke.Equals(o.stroke) && logicalSize == o.logicalSize && pixelScale == o.pixelScale;
        public override bool Equals(object obj) => obj is Params p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(stroke, logicalSize, pixelScale);
    }

    /// <summary>
    /// Builds (or reuses a cached) rack-glyph Sprite, point-filtered and
    /// upscaled by <paramref name="pixelScale"/> to stay crisp/blocky like the
    /// rest of this project's procedural icons.
    /// </summary>
    public static Sprite GetSprite(Color32 stroke, int logicalSize = 32, int pixelScale = 6)
    {
        var key = new Params(stroke, logicalSize, pixelScale);
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        int size = logicalSize * pixelScale;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "DeviceIconFallback",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        var clear = new Color32(0, 0, 0, 0);
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        // Three stacked rack-unit bars (outline only) with a small filled LED
        // near the left edge of each — logical space has y=0 at the bottom
        // (same convention as CheckmarkIconBuilder/StarIconBuilder).
        float barHeight = logicalSize * 0.15f;
        float gap       = logicalSize * 0.07f;
        float barWidth  = logicalSize * 0.70f;
        float left      = (logicalSize - barWidth) * 0.5f;
        float startY    = logicalSize * 0.24f;
        float thickness = Mathf.Max(1f, logicalSize * 0.045f);
        float ledSize   = logicalSize * 0.07f;

        for (int bar = 0; bar < 3; bar++)
        {
            float y0 = startY + bar * (barHeight + gap);
            float y1 = y0 + barHeight;
            DrawRectOutline(pixels, size, pixelScale, left, y0, left + barWidth, y1, stroke, thickness);
            float ledX = left + barWidth * 0.12f;
            float ledY = y0 + (barHeight - ledSize) * 0.5f;
            DrawFilledRect(pixels, size, pixelScale, ledX, ledY, ledX + ledSize, ledY + ledSize, stroke);
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f * pixelScale);
        sprite.name = "DeviceIconFallback";

        s_cache[key] = sprite;
        return sprite;
    }

    static void DrawFilledRect(Color32[] pixels, int size, int pixelScale,
                                float lx0, float ly0, float lx1, float ly1, Color32 color)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(lx0 * pixelScale), 0, size - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(lx1 * pixelScale), 0, size - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(ly0 * pixelScale), 0, size - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(ly1 * pixelScale), 0, size - 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                pixels[y * size + x] = color;
    }

    static void DrawRectOutline(Color32[] pixels, int size, int pixelScale,
                                 float lx0, float ly0, float lx1, float ly1, Color32 color, float thickness)
    {
        DrawFilledRect(pixels, size, pixelScale, lx0, ly0, lx1, ly0 + thickness, color);
        DrawFilledRect(pixels, size, pixelScale, lx0, ly1 - thickness, lx1, ly1, color);
        DrawFilledRect(pixels, size, pixelScale, lx0, ly0, lx0 + thickness, ly1, color);
        DrawFilledRect(pixels, size, pixelScale, lx1 - thickness, ly0, lx1, ly1, color);
    }
}
