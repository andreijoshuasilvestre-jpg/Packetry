using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Computes (and caches) the tight bounding box of a sprite's non-transparent
/// pixels, normalized to [0,1] within the sprite's own rect — (0,0) is the
/// sprite's bottom-left, (1,1) its top-right. Used to size FX (the sidebar
/// click-glow ring) to a button's actual visible artwork instead of the full
/// PNG canvas, since these icons bake in a transparent margin around the
/// plate. Requires the sprite's texture to have Read/Write enabled (see
/// UIIconImporter) — falls back to the full [0,1] rect (i.e. "no trim")
/// otherwise, so this degrades safely rather than throwing.
/// </summary>
public static class SpriteContentBounds
{
    const float AlphaThreshold = 0.02f;
    static readonly Dictionary<Sprite, Rect> cache = new();

    public static Rect Get(Sprite sprite)
    {
        if (sprite == null) return new Rect(0f, 0f, 1f, 1f);
        if (cache.TryGetValue(sprite, out var cached)) return cached;

        Rect result = new Rect(0f, 0f, 1f, 1f);
        var tex = sprite.texture;
        if (tex != null && tex.isReadable)
        {
            var r = sprite.textureRect;
            int x0 = Mathf.RoundToInt(r.x);
            int y0 = Mathf.RoundToInt(r.y);
            int w  = Mathf.RoundToInt(r.width);
            int h  = Mathf.RoundToInt(r.height);
            int texW = tex.width;

            var pixels = tex.GetPixels32();
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = (y0 + y) * texW + x0;
                for (int x = 0; x < w; x++)
                {
                    if (pixels[row + x].a / 255f > AlphaThreshold)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX >= minX && maxY >= minY)
            {
                result = new Rect(
                    (float)minX / w, (float)minY / h,
                    (float)(maxX - minX + 1) / w, (float)(maxY - minY + 1) / h);
            }
        }

        cache[sprite] = result;
        return result;
    }

    // Rows whose right extent reaches within this fraction of the widest
    // row's count as part of the plate (tolerates the chamfered corners).
    const float PlateRowWidthTolerance = 0.04f;
    static readonly Dictionary<Sprite, Rect> plateCache = new();

    /// <summary>
    /// Like Get(), but only the sidebar icon's main plate — the widest
    /// contiguous band of rows — excluding the narrower label stem baked in
    /// beneath it. Same normalized space and same readable-texture fallback
    /// (falls back to Get()'s full-content box).
    /// </summary>
    public static Rect GetPlate(Sprite sprite)
    {
        if (sprite == null) return new Rect(0f, 0f, 1f, 1f);
        if (plateCache.TryGetValue(sprite, out var cached)) return cached;

        Rect result = Get(sprite);
        var tex = sprite.texture;
        if (tex != null && tex.isReadable)
        {
            var r = sprite.textureRect;
            int x0 = Mathf.RoundToInt(r.x);
            int y0 = Mathf.RoundToInt(r.y);
            int w  = Mathf.RoundToInt(r.width);
            int h  = Mathf.RoundToInt(r.height);
            int texW = tex.width;

            var pixels = tex.GetPixels32();
            var rowMaxX = new int[h];
            var rowMinX = new int[h];
            int widest = -1;
            for (int y = 0; y < h; y++)
            {
                int row = (y0 + y) * texW + x0;
                rowMinX[y] = w;
                rowMaxX[y] = -1;
                for (int x = 0; x < w; x++)
                {
                    if (pixels[row + x].a / 255f > AlphaThreshold)
                    {
                        if (x < rowMinX[y]) rowMinX[y] = x;
                        rowMaxX[y] = x;
                    }
                }
                if (rowMaxX[y] > widest) widest = rowMaxX[y];
            }

            if (widest >= 0)
            {
                int threshold = Mathf.FloorToInt(widest - w * PlateRowWidthTolerance);
                // Longest contiguous run of rows reaching the plate's width.
                int bestStart = -1, bestLen = 0, runStart = -1;
                for (int y = 0; y <= h; y++)
                {
                    bool wide = y < h && rowMaxX[y] >= threshold;
                    if (wide && runStart < 0) runStart = y;
                    if (!wide && runStart >= 0)
                    {
                        if (y - runStart > bestLen) { bestLen = y - runStart; bestStart = runStart; }
                        runStart = -1;
                    }
                }

                if (bestLen > 0)
                {
                    int minX = w;
                    for (int y = bestStart; y < bestStart + bestLen; y++)
                        if (rowMinX[y] < minX) minX = rowMinX[y];
                    result = new Rect(
                        (float)minX / w, (float)bestStart / h,
                        (float)(widest - minX + 1) / w, (float)bestLen / h);
                }
            }
        }

        plateCache[sprite] = result;
        return result;
    }
}
