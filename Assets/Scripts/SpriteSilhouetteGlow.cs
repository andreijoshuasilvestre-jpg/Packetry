using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bakes a soft outward glow that hugs a sprite's own alpha silhouette
/// pixel-for-pixel — via a fast two-pass chamfer distance transform, not an
/// analytic shape — instead of approximating the button with a generic
/// rounded rectangle (see RoundedGlowOutlineSprite). Used by the sidebar
/// click-glow so the halo traces the button's actual edges (chamfered
/// corners, the narrower label stem beneath the plate, etc.) rather than a
/// rectangle floating past them. The baked texture is full opacity anywhere
/// inside the silhouette (the icon itself draws over that region on top
/// anyway) and fades to 0 over GlowRadius pixels outside it — displayed at
/// the same rect as the icon it was baked from, so only that outward rim
/// peeks out past the icon's own opaque edges.
///
/// Cached per source sprite. Requires the sprite's texture to be readable
/// (see UIIconImporter) — returns null otherwise so callers can fall back to
/// RoundedGlowOutlineSprite instead of drawing a blank/solid quad.
/// </summary>
public static class SpriteSilhouetteGlow
{
    const float AlphaThreshold = 0.5f;
    const float GlowRadius = 16f; // pixels, at the source icon's own resolution — wider, softer spread than a tight rim
    const float Diagonal = 1.41421356f;

    static readonly Dictionary<Sprite, Sprite> cache = new();

    public static Sprite Get(Sprite source)
    {
        if (source == null) return null;
        if (cache.TryGetValue(source, out var cached)) return cached;

        var tex = source.texture;
        if (tex == null || !tex.isReadable)
        {
            cache[source] = null;
            return null;
        }

        var r = source.textureRect;
        int x0 = Mathf.RoundToInt(r.x);
        int y0 = Mathf.RoundToInt(r.y);
        int w  = Mathf.RoundToInt(r.width);
        int h  = Mathf.RoundToInt(r.height);
        int texW = tex.width;

        var srcPixels = tex.GetPixels32();

        // Chamfer distance transform: dist[] starts at 0 inside the silhouette,
        // +inf outside, then two small-kernel sweeps (forward top-left->bottom-
        // right, backward the reverse) propagate the nearest-inside distance
        // outward — a good, cheap approximation of true Euclidean distance for
        // a soft few-pixel glow.
        const float Inf = 1e6f;
        var dist = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            int srcRow = (y0 + y) * texW + x0;
            int row = y * w;
            for (int x = 0; x < w; x++)
                dist[row + x] = srcPixels[srcRow + x].a / 255f > AlphaThreshold ? 0f : Inf;
        }

        void Relax(int x, int y, int dx, int dy, float d)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return;
            float cand = dist[ny * w + nx] + d;
            int i = y * w + x;
            if (cand < dist[i]) dist[i] = cand;
        }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (dist[y * w + x] == 0f) continue;
                Relax(x, y, -1, 0, 1f);
                Relax(x, y, 0, -1, 1f);
                Relax(x, y, -1, -1, Diagonal);
                Relax(x, y, 1, -1, Diagonal);
            }
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                if (dist[y * w + x] == 0f) continue;
                Relax(x, y, 1, 0, 1f);
                Relax(x, y, 0, 1, 1f);
                Relax(x, y, 1, 1, Diagonal);
                Relax(x, y, -1, 1, Diagonal);
            }

        var outPixels = new Color32[w * h];
        for (int i = 0; i < outPixels.Length; i++)
        {
            float d = dist[i];
            // Linear falloff (not squared) — a gradual, soft haze fading out to
            // GlowRadius rather than a sharp, punchy rim hugging the edge. The
            // interior (d<=0) stays fully opaque, but that's only ever visible
            // in this outward bleed once the icon itself covers the rest.
            float a = d <= 0f ? 1f : 1f - Mathf.Clamp01(d / GlowRadius);
            outPixels[i] = new Color(1f, 1f, 1f, a);
        }

        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        outTex.wrapMode   = TextureWrapMode.Clamp;
        outTex.filterMode = FilterMode.Bilinear;
        outTex.SetPixels32(outPixels);
        outTex.Apply(false, true);

        var sprite = Sprite.Create(outTex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
        cache[source] = sprite;
        return sprite;
    }
}
