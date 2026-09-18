using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws the "chamfered plate" look used by
/// Assets/Resources/MainMenuUI/BlankButton.png — a pixel-art panel with cut
/// corners and a colored border — as a runtime-generated Sprite instead of a
/// fixed image asset. Exists so the plate's fill/border/trim colors, border
/// thickness, and corner-cut size are just parameters callers pass in (e.g.
/// LevelSelectManager's backdrop, or a future panel elsewhere) rather than
/// something only an external image editor can change.
///
/// Sprites are cached per distinct parameter combination (see Params below)
/// so re-requesting the same look (every rebuild of a scene, e.g.) reuses one
/// texture instead of regenerating it.
/// </summary>
public static class PlateSpriteBuilder
{
    static readonly Dictionary<Params, Sprite> s_cache = new();

    readonly struct Params : IEquatable<Params>
    {
        public readonly Color32 fill, border, trim;
        public readonly int borderThickness, chamfer, pixelScale;

        public Params(Color32 fill, Color32 border, Color32 trim, int borderThickness, int chamfer, int pixelScale)
        {
            this.fill = fill;
            this.border = border;
            this.trim = trim;
            this.borderThickness = borderThickness;
            this.chamfer = chamfer;
            this.pixelScale = pixelScale;
        }

        public bool Equals(Params o) =>
            fill.Equals(o.fill) && border.Equals(o.border) && trim.Equals(o.trim) &&
            borderThickness == o.borderThickness && chamfer == o.chamfer && pixelScale == o.pixelScale;
        public override bool Equals(object obj) => obj is Params p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(fill, border, trim, borderThickness, chamfer, pixelScale);
    }

    /// <summary>
    /// Builds (or reuses a cached) sliced Sprite of the chamfered plate.
    /// </summary>
    /// <param name="fill">Panel interior color.</param>
    /// <param name="border">Outer border color (the BlankButton "blue frame").</param>
    /// <param name="trim">Thin one-pixel accent line between border and fill.</param>
    /// <param name="borderThickness">Border ring width, in logical (pre-upscale) pixels.</param>
    /// <param name="chamfer">Diagonal corner-cut size, in logical pixels — 0 disables the chamfer (plain rounded-free rectangle).</param>
    /// <param name="pixelScale">Upscale factor so the art stays crisp/blocky (point-filtered) at large on-screen sizes.</param>
    public static Sprite GetSprite(Color32 fill, Color32 border, Color32 trim,
        int borderThickness = 3, int chamfer = 6, int pixelScale = 4)
    {
        var key = new Params(fill, border, trim, borderThickness, chamfer, pixelScale);
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        int logicalCorner = borderThickness + Mathf.Max(chamfer, 0);
        int logicalSize = logicalCorner * 2 + 1; // +1 logical px of stretchable middle for 9-slicing
        int size = logicalSize * pixelScale;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "PlateSprite",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        var pixels = new Color32[size * size];
        var clear = new Color32(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            int ly = y / pixelScale;
            // Mild vertical shading (brighter near the top) so the plate
            // reads as lit from above like the hand-painted original,
            // instead of a completely flat fill.
            float shade = Mathf.Lerp(1.12f, 0.92f, (float)ly / (logicalSize - 1));

            for (int x = 0; x < size; x++)
            {
                int lx = x / pixelScale;

                int dx = Mathf.Min(lx, logicalSize - 1 - lx);
                int dy = Mathf.Min(ly, logicalSize - 1 - ly);
                int edgeDist = Mathf.Min(dx, dy);
                int diag = dx + dy;

                // diag (dx+dy) is a Manhattan/L1 distance, but edgeDist (the
                // straight-edge metric below) is L-infinity — a band of the
                // same numeric width reads ~30% THINNER along the 45-degree
                // cut than along a straight edge (a step of 1 perpendicular
                // unit along the diagonal changes diag by sqrt(2), not 1).
                // Widen the diagonal band by sqrt(2) so its true (Euclidean,
                // perpendicular-to-the-cut) thickness matches the straight
                // border instead of looking thinner/inconsistent.
                int diagBorderWidth = Mathf.RoundToInt(borderThickness * 1.41421356f);

                Color32 c;
                if (chamfer > 0 && diag < chamfer - diagBorderWidth)
                {
                    c = clear; // fully outside the chamfered corner
                }
                else if (chamfer > 0 && diag < chamfer)
                {
                    c = border; // diagonal border stripe tracing the cut
                }
                else if (edgeDist < borderThickness)
                {
                    c = border;
                }
                else if (edgeDist == borderThickness)
                {
                    c = trim;
                }
                else
                {
                    c = Shaded(fill, shade);
                }

                pixels[y * size + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        float border9 = logicalCorner * pixelScale;
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f * pixelScale, 0, SpriteMeshType.FullRect, new Vector4(border9, border9, border9, border9));
        sprite.name = "PlateSprite";

        s_cache[key] = sprite;
        return sprite;
    }

    static Color32 Shaded(Color32 c, float mul) => new Color32(
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * mul), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * mul), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * mul), 0, 255),
        c.a);
}
