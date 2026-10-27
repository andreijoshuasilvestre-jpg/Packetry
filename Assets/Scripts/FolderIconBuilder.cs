using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws a small pixel-art folder icon — a flat-filled
/// silhouette (body rectangle plus a smaller tab unioned onto its top-left
/// shoulder) rather than just an outline, since an outline's thin tab corner
/// is easy to lose entirely at the small on-screen size a packet glyph
/// renders at. Sampled once per logical cell and upscaled into solid
/// blocks, same as CheckmarkIconBuilder (see PixelGlyph). Used by PacketVisual as the glyph for
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

        // Logical-pixel space, y=0 at the bottom: a wide flat body plus a
        // narrower tab sitting on its top-left shoulder.
        float bodyX0 = logicalSize * 0.08f, bodyX1 = logicalSize * 0.92f;
        float bodyY0 = logicalSize * 0.14f, bodyY1 = logicalSize * 0.66f;
        float tabX0  = logicalSize * 0.08f, tabX1  = logicalSize * 0.52f;
        float tabY0  = logicalSize * 0.66f, tabY1  = logicalSize * 0.80f;

        bool[] mask = PixelGlyph.Rasterize(logicalSize, p =>
        {
            bool inBody = p.x >= bodyX0 && p.x <= bodyX1 && p.y >= bodyY0 && p.y <= bodyY1;
            bool inTab  = p.x >= tabX0  && p.x <= tabX1  && p.y >= tabY0  && p.y <= tabY1;
            return inBody || inTab;
        });

        var sprite = PixelGlyph.ToSprite(PixelGlyph.Flat(mask, fill), logicalSize, pixelScale, "FolderIcon");
        s_cache[key] = sprite;
        return sprite;
    }
}
