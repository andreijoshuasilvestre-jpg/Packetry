using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws a small pixel-art folder icon — a flat-filled
/// silhouette (body rectangle plus a smaller tab unioned onto its top-left
/// shoulder) rather than just an outline, since an outline's thin tab corner
/// is easy to lose entirely at the small on-screen size a packet glyph
/// renders at. Same runtime-generated, point-filtered, upscaled-blocky
/// approach as CheckmarkIconBuilder. Used by PacketVisual as the glyph for
/// FileTransfer packets in flight.
/// </summary>
public static class FolderIconBuilder
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
    /// Builds (or reuses a cached) folder Sprite: a wide body rectangle with a
    /// narrower tab rectangle unioned onto its top-left corner.
    /// </summary>
    public static Sprite GetSprite(Color32 fill, int logicalSize = 14, int pixelScale = 8)
    {
        var key = new Params(fill, logicalSize, pixelScale);
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        int size = logicalSize * pixelScale;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "FolderIcon",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        // Logical-pixel space, y=0 at the bottom: a wide flat body plus a
        // narrower tab sitting on its top-left shoulder.
        float bodyX0 = logicalSize * 0.08f, bodyX1 = logicalSize * 0.92f;
        float bodyY0 = logicalSize * 0.14f, bodyY1 = logicalSize * 0.66f;
        float tabX0  = logicalSize * 0.08f, tabX1  = logicalSize * 0.52f;
        float tabY0  = logicalSize * 0.66f, tabY1  = logicalSize * 0.80f;

        var pixels = new Color32[size * size];
        var clear = new Color32(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            float ly = (y + 0.5f) / pixelScale;
            for (int x = 0; x < size; x++)
            {
                float lx = (x + 0.5f) / pixelScale;
                bool inBody = lx >= bodyX0 && lx <= bodyX1 && ly >= bodyY0 && ly <= bodyY1;
                bool inTab  = lx >= tabX0  && lx <= tabX1  && ly >= tabY0  && ly <= tabY1;
                pixels[y * size + x] = (inBody || inTab) ? fill : clear;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f * pixelScale);
        sprite.name = "FolderIcon";

        s_cache[key] = sprite;
        return sprite;
    }
}
