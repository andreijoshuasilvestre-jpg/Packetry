using System;
using UnityEngine;

/// <summary>
/// Shared rasterizer behind the small procedural icon glyphs
/// (CheckmarkIconBuilder, CrossIconBuilder, GridGlobeIconBuilder,
/// FolderIconBuilder, StarIconBuilder): a shape is sampled ONCE per logical
/// grid cell and each cell is then blown up into a solid pixelScale x
/// pixelScale block — genuine pixel art, the same thing PingSelectBadge's
/// radial frames and the PC monitors already do.
///
/// The line-art builders used to evaluate their shape per output texel
/// instead ((x + 0.5) / pixelScale). Point filtering only kept that crisp at
/// the texel level, so every diagonal or curved edge came out as a smooth
/// pixelScale-times-finer line — a tick or an X that read as vector art next
/// to the chunky pixel art around it.
/// </summary>
public static class PixelGlyph
{
    /// <summary>
    /// Coverage mask, one bool per logical cell, row 0 at the bottom
    /// (Texture2D's own row order). <paramref name="covers"/> is tested at
    /// each cell's center, in logical-pixel space (y up).
    /// </summary>
    public static bool[] Rasterize(int logicalSize, Func<Vector2, bool> covers)
    {
        var mask = new bool[logicalSize * logicalSize];
        for (int y = 0; y < logicalSize; y++)
            for (int x = 0; x < logicalSize; x++)
                mask[y * logicalSize + x] = covers(new Vector2(x + 0.5f, y + 0.5f));
        return mask;
    }

    /// <summary>
    /// Embeds a mask in a grid <paramref name="margin"/> cells larger on
    /// every side — room for Shaded()'s outline ring around a shape that
    /// otherwise runs right up to its own grid's edge.
    /// </summary>
    public static bool[] Pad(bool[] mask, int logicalSize, int margin)
    {
        int padded = logicalSize + margin * 2;
        var result = new bool[padded * padded];
        for (int y = 0; y < logicalSize; y++)
            for (int x = 0; x < logicalSize; x++)
                result[(y + margin) * padded + x + margin] = mask[y * logicalSize + x];
        return result;
    }

    /// <summary>One flat color for every covered cell, clear everywhere else.</summary>
    public static Color32[] Flat(bool[] mask, Color32 fill)
    {
        var cells = new Color32[mask.Length];
        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < mask.Length; i++) cells[i] = mask[i] ? fill : clear;
        return cells;
    }

    /// <summary>
    /// Shaded()'s look for a glyph that gets TINTED at runtime (the guidance
    /// markers — see MarkerGlyphs): near-white fill with a white top-left
    /// highlight and a light-grey bottom-right shadow, so any tint multiplied
    /// in keeps the shading, plus a pure-black 1-cell outline (8-neighbour, so
    /// diagonal edges close up) that stays black under every tint. Needs a
    /// cell of margin like Shaded (see Pad).
    /// </summary>
    public static Color32[] TintableOutlined(bool[] mask, int logicalSize)
    {
        bool In(int x, int y) => x >= 0 && x < logicalSize && y >= 0 && y < logicalSize && mask[y * logicalSize + x];

        var fill      = new Color32(236, 236, 236, 255);
        var highlight = new Color32(255, 255, 255, 255);
        var shadow    = new Color32(186, 186, 186, 255);
        var outline   = new Color32(0, 0, 0, 255);

        var cells = new Color32[logicalSize * logicalSize];
        for (int y = 0; y < logicalSize; y++)
        {
            for (int x = 0; x < logicalSize; x++)
            {
                Color32 c = new Color32(0, 0, 0, 0);
                if (In(x, y))
                {
                    bool edgeLeft = !In(x - 1, y), edgeRight = !In(x + 1, y);
                    bool edgeUp   = !In(x, y + 1), edgeDown  = !In(x, y - 1);

                    // Same lighting and one-cell-sliver rule as Shaded.
                    if (edgeLeft && edgeRight)      c = fill;
                    else if (edgeRight || edgeDown) c = shadow;
                    else if (edgeLeft || edgeUp)    c = highlight;
                    else                            c = fill;
                }
                else
                {
                    for (int dy = -1; dy <= 1 && c.a == 0; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((dx != 0 || dy != 0) && In(x + dx, y + dy)) { c = outline; break; }
                }
                cells[y * logicalSize + x] = c;
            }
        }
        return cells;
    }

    /// <summary>
    /// StarIconBuilder's look: a 1-cell dark outline around the shape, plus
    /// top-left highlight / bottom-right shadow cells along its edges, all
    /// derived from the one fill color. The outline lands on uncovered cells
    /// next to the shape, so leave at least a cell of margin (see Pad).
    /// </summary>
    public static Color32[] Shaded(bool[] mask, int logicalSize, Color32 fill)
    {
        bool In(int x, int y) => x >= 0 && x < logicalSize && y >= 0 && y < logicalSize && mask[y * logicalSize + x];

        Color32 highlight = Color32.Lerp(fill, new Color32(255, 255, 255, 255), 0.45f);
        Color32 shadow    = Color32.Lerp(fill, new Color32(0, 0, 0, 255), 0.2f);
        Color32 outline   = Color32.Lerp(fill, new Color32(0, 0, 0, 255), 0.72f);
        highlight.a = shadow.a = outline.a = fill.a;

        var cells = new Color32[logicalSize * logicalSize];
        for (int y = 0; y < logicalSize; y++)
        {
            for (int x = 0; x < logicalSize; x++)
            {
                Color32 c = new Color32(0, 0, 0, 0);
                if (In(x, y))
                {
                    bool edgeLeft = !In(x - 1, y), edgeRight = !In(x + 1, y);
                    bool edgeUp   = !In(x, y + 1), edgeDown  = !In(x, y - 1);

                    // Light from the top-left: edges facing down/right are
                    // shaded, edges facing up/left catch the highlight. A
                    // one-cell-wide sliver (a star's top point) is both a
                    // left and a right edge — it keeps the plain fill, or it
                    // would read as a dim stub.
                    if (edgeLeft && edgeRight)      c = fill;
                    else if (edgeRight || edgeDown) c = shadow;
                    else if (edgeLeft || edgeUp)    c = highlight;
                    else                            c = fill;
                }
                else if (In(x + 1, y) || In(x - 1, y) || In(x, y + 1) || In(x, y - 1))
                {
                    c = outline;
                }
                cells[y * logicalSize + x] = c;
            }
        }
        return cells;
    }

    /// <summary>
    /// The shape as a hole dug into whatever surface it sits on, lit from the
    /// top-left like Shaded(): a near-black cavity whose bottom/right inner
    /// walls catch a little of the light while its top/left walls sit in the
    /// lip's shadow, ringed by a dark cut edge above/left and a bright lip
    /// below/right. The ring is translucent so it picks up the surface's own
    /// color rather than painting a fixed one. Needs the same one-cell margin
    /// as Shaded (see Pad).
    /// </summary>
    public static Color32[] Engraved(bool[] mask, int logicalSize, Color32 hole)
    {
        bool In(int x, int y) => x >= 0 && x < logicalSize && y >= 0 && y < logicalSize && mask[y * logicalSize + x];

        Color32 lipShadow = Color32.Lerp(hole, new Color32(0, 0, 0, 255), 0.7f);
        Color32 litWall   = Color32.Lerp(hole, new Color32(255, 255, 255, 255), 0.15f);
        lipShadow.a = litWall.a = hole.a;
        var rimDark  = new Color32(0, 0, 0, 90);
        var rimLight = new Color32(255, 255, 255, 150);

        var cells = new Color32[logicalSize * logicalSize];
        for (int y = 0; y < logicalSize; y++)
        {
            for (int x = 0; x < logicalSize; x++)
            {
                Color32 c = new Color32(0, 0, 0, 0);
                if (In(x, y))
                {
                    bool edgeLeft = !In(x - 1, y), edgeRight = !In(x + 1, y);
                    bool edgeUp   = !In(x, y + 1), edgeDown  = !In(x, y - 1);

                    // Same one-cell-sliver exception as Shaded — the top
                    // point stays plain hole instead of a lone lit/shadow cell.
                    if (edgeLeft && edgeRight)      c = hole;
                    else if (edgeRight || edgeDown) c = litWall;
                    else if (edgeLeft || edgeUp)    c = lipShadow;
                    else                            c = hole;
                }
                else if (In(x + 1, y) || In(x, y - 1)) c = rimDark;  // above/left of the hole
                else if (In(x - 1, y) || In(x, y + 1)) c = rimLight; // below/right of the hole
                cells[y * logicalSize + x] = c;
            }
        }
        return cells;
    }

    /// <summary>
    /// Upscales one-color-per-cell art into a point-filtered Sprite, each
    /// cell a solid <paramref name="pixelScale"/>-texel block. Pixels-per-unit
    /// is 100 x pixelScale, so one logical cell is always 0.01 world units
    /// regardless of pixelScale — what every builder here has always used.
    /// </summary>
    public static Sprite ToSprite(Color32[] cells, int logicalSize, int pixelScale, string name)
    {
        int size = logicalSize * pixelScale;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = cells[(y / pixelScale) * logicalSize + x / pixelScale];

        tex.SetPixels32(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f * pixelScale);
        sprite.name = name;
        return sprite;
    }

    public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f);
        t = Mathf.Clamp01(t);
        Vector2 closest = a + ab * t;
        return Vector2.Distance(p, closest);
    }
}
