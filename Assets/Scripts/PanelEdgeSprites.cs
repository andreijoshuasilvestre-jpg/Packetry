using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sprites derived from a panel sprite's own alpha silhouette, for
/// PanelArrivalGlow's arrival flash — both trace the panel's exact shape (a
/// PlateSpriteBuilder plate's chamfered corners included) without the caller
/// repeating whatever parameters drew that panel:
///   - Outline: a crisp band of the opaque texels within `thicknessTexels`
///     of the silhouette's edge, solid white (tint via Image.color).
///   - OuterGlow: a soft halo that starts at full strength right at the
///     silhouette's edge and fades to nothing over `radiusTexels` outside it
///     — transparent inside, so it can sit over the panel's own plate
///     without tinting it. Padded by that radius on every side.
///
/// Both keep the source's pixelsPerUnit and 9-slice border (the glow's grown
/// by its padding), so an Image.Type.Sliced Image showing one lines up
/// exactly with one showing the source — at the same rect for the outline,
/// at that rect grown by the padding for the glow.
///
/// Same two-pass chamfer distance transform as SpriteSilhouetteGlow. Cached
/// per source + size. Requires a readable texture (every PlateSpriteBuilder
/// sprite is, and MainMenuUIBaker's persisted copies keep that) — returns
/// null otherwise so callers can skip the layer.
/// </summary>
public static class PanelEdgeSprites
{
    const float AlphaThreshold = 0.5f;
    const float Diagonal = 1.41421356f;
    const float Inf = 1e6f;

    static readonly Dictionary<(Sprite, int), Sprite> outlineCache = new();
    static readonly Dictionary<(Sprite, int), Sprite> glowCache = new();

    public static Sprite Outline(Sprite source, int thicknessTexels)
    {
        var key = (source, thicknessTexels);
        if (outlineCache.TryGetValue(key, out var cached)) return cached;

        Sprite result = null;
        if (TryReadMask(source, 0, out var opaque, out int w, out int h))
        {
            // Inward: each opaque texel's distance to the nearest transparent
            // one. The sprite's own rect edge counts as transparent too — a
            // plate's straight border runs right along it.
            var dist = SeedDistances(opaque, seedOpaque: false);
            ChamferSweep(dist, w, h, outsideIsSeed: true);

            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, (byte)(dist[i] > 0f && dist[i] <= thicknessTexels ? 255 : 0));

            // Point-filtered like the plate it traces, so the line stays as blocky as the border under it.
            result = BuildSprite(pixels, w, h, source, 0, source.texture.filterMode);
        }

        outlineCache[key] = result;
        return result;
    }

    public static Sprite OuterGlow(Sprite source, int radiusTexels)
    {
        var key = (source, radiusTexels);
        if (glowCache.TryGetValue(key, out var cached)) return cached;

        Sprite result = null;
        if (radiusTexels > 0 && TryReadMask(source, radiusTexels, out var opaque, out int w, out int h))
        {
            // Outward: each texel's distance to the nearest opaque one.
            var dist = SeedDistances(opaque, seedOpaque: true);
            ChamferSweep(dist, w, h, outsideIsSeed: false);

            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++)
            {
                float d = dist[i];
                // Squared falloff — brightest hugging the edge, then a long,
                // soft tail, rather than a flat band of one color.
                float a = d <= 0f ? 0f : Mathf.Pow(1f - Mathf.Clamp01(d / radiusTexels), 2f);
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }

            result = BuildSprite(pixels, w, h, source, radiusTexels, FilterMode.Bilinear);
        }

        glowCache[key] = result;
        return result;
    }

    // Source sprite's alpha mask (opaque = true), centered in a buffer grown
    // by `pad` texels on every side.
    static bool TryReadMask(Sprite source, int pad, out bool[] opaque, out int w, out int h)
    {
        opaque = null;
        w = h = 0;
        if (source == null) return false;
        var tex = source.texture;
        if (tex == null || !tex.isReadable) return false;

        var r = source.textureRect;
        int x0 = Mathf.RoundToInt(r.x);
        int y0 = Mathf.RoundToInt(r.y);
        int sw = Mathf.RoundToInt(r.width);
        int sh = Mathf.RoundToInt(r.height);
        int texW = tex.width;
        var src = tex.GetPixels32();

        w = sw + 2 * pad;
        h = sh + 2 * pad;
        opaque = new bool[w * h];
        for (int y = 0; y < sh; y++)
        {
            int srcRow = (y0 + y) * texW + x0;
            int row = (y + pad) * w + pad;
            for (int x = 0; x < sw; x++)
                opaque[row + x] = src[srcRow + x].a / 255f > AlphaThreshold;
        }
        return true;
    }

    // 0 on the seed texels, +inf everywhere else — ChamferSweep then fills in
    // every other texel's distance to the nearest seed.
    static float[] SeedDistances(bool[] opaque, bool seedOpaque)
    {
        var dist = new float[opaque.Length];
        for (int i = 0; i < dist.Length; i++)
            dist[i] = opaque[i] == seedOpaque ? 0f : Inf;
        return dist;
    }

    // Forward (top-left -> bottom-right) then backward sweep with a small
    // kernel — a cheap, good-enough approximation of Euclidean distance.
    // outsideIsSeed: whether texels beyond the buffer count as seeds.
    static void ChamferSweep(float[] dist, int w, int h, bool outsideIsSeed)
    {
        void Relax(int x, int y, int dx, int dy, float d)
        {
            int nx = x + dx, ny = y + dy;
            float neighbour;
            if (nx < 0 || nx >= w || ny < 0 || ny >= h)
            {
                if (!outsideIsSeed) return;
                neighbour = 0f;
            }
            else neighbour = dist[ny * w + nx];

            int i = y * w + x;
            if (neighbour + d < dist[i]) dist[i] = neighbour + d;
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
    }

    static Sprite BuildSprite(Color32[] pixels, int w, int h, Sprite source, int pad, FilterMode filterMode)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode   = TextureWrapMode.Clamp;
        tex.filterMode = filterMode;
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
            source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border + Vector4.one * pad);
    }
}
