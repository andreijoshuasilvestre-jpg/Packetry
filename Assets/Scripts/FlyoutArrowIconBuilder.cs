using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws the pixel-art arrowhead on CompoundSidebarGroup's
/// flyout tab (see FlyoutArrowTab), in the sidebar buttons' own material
/// (Assets/Resources/UIIcons/*_Icon.png): a silver body lit from the top-left,
/// a one-cell blue frame and a near-black outer edge. The lit variant mirrors
/// the buttons' "_Clicked" hover art (bright blue body, brighter frame), so
/// the arrow lights up the same way a button does. Sampled once per logical
/// cell and upscaled through PixelGlyph like the other procedural glyphs.
/// </summary>
public static class FlyoutArrowIconBuilder
{
    // Arrowhead body in logical cells, frame and outline not included. Width
    // is (height + 1) / 2 so each column steps in by exactly one cell top and
    // bottom: a clean 45° pixel diagonal ending in a single-cell tip.
    const int BodyHeight = 9;
    const int BodyWidth  = (BodyHeight + 1) / 2;
    const int Rings      = 2; // blue frame + dark outline

    /// <summary>Cells per side of the (square) sprite.</summary>
    public const int GridSize = BodyHeight + Rings * 2;

    /// <summary>Cells the framed arrow actually spans horizontally. It's
    /// narrower than it is tall, so it sits centered in the square sprite
    /// with clear columns either side. The difference to GridSize is even, so
    /// it centers on whole cells.</summary>
    public const int FramedWidth = BodyWidth + Rings * 2;

    const int TexelsPerCell = 4;

    readonly struct Palette
    {
        public readonly Color32 fill, highlight, shadow, frame, outline;
        public Palette(Color32 fill, Color32 highlight, Color32 shadow, Color32 frame, Color32 outline)
        {
            this.fill = fill; this.highlight = highlight; this.shadow = shadow;
            this.frame = frame; this.outline = outline;
        }
    }

    // Sampled from PC_Icon.png / PC_Icon_Clicked.png's plate and border.
    static readonly Palette Idle = new Palette(
        new Color32(190, 190, 194, 255), new Color32(230, 230, 234, 255), new Color32(124, 126, 132, 255),
        new Color32(20, 85, 139, 255), new Color32(4, 18, 44, 255));
    static readonly Palette Lit = new Palette(
        new Color32(96, 156, 228, 255), new Color32(192, 220, 252, 255), new Color32(30, 100, 190, 255),
        new Color32(6, 111, 198, 255), new Color32(0, 37, 104, 255));

    static readonly Dictionary<(bool right, bool lit), Sprite> s_cache = new();

    /// <summary>Builds (or reuses a cached) arrowhead Sprite pointing right
    /// or left. The left one is the mirrored shape re-lit, not a flipped
    /// texture, so its highlight still sits top-left.</summary>
    public static Sprite GetSprite(bool pointRight, bool lit)
    {
        var key = (pointRight, lit);
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var sprite = PixelGlyph.ToSprite(Paint(BuildBody(pointRight), lit ? Lit : Idle), GridSize, TexelsPerCell,
            "FlyoutArrow" + (pointRight ? "Right" : "Left") + (lit ? "Lit" : ""));
        s_cache[key] = sprite;
        return sprite;
    }

    static bool[] BuildBody(bool pointRight)
    {
        var mask = new bool[GridSize * GridSize];
        int x0 = (GridSize - FramedWidth) / 2 + Rings;
        int y0 = Rings;
        int mid = (BodyHeight - 1) / 2;

        for (int c = 0; c < BodyWidth; c++)
        {
            int halfSpan = BodyWidth - 1 - c; // full height at the base, one cell at the tip
            int x = x0 + (pointRight ? c : BodyWidth - 1 - c);
            for (int r = mid - halfSpan; r <= mid + halfSpan; r++)
                mask[(y0 + r) * GridSize + x] = true;
        }
        return mask;
    }

    static Color32[] Paint(bool[] body, Palette p)
    {
        int g = GridSize;
        bool In(bool[] m, int x, int y) => x >= 0 && x < g && y >= 0 && y < g && m[y * g + x];
        bool Touches(bool[] m, int x, int y) => In(m, x + 1, y) || In(m, x - 1, y) || In(m, x, y + 1) || In(m, x, y - 1);

        // Rings are 4-neighbour, like PixelGlyph.Shaded's outline, so each
        // diagonal is a single-cell staircase.
        var framed = new bool[g * g];
        var cells = new Color32[g * g];
        for (int y = 0; y < g; y++)
            for (int x = 0; x < g; x++)
            {
                int i = y * g + x;
                if (body[i]) { framed[i] = true; continue; }
                if (Touches(body, x, y)) { framed[i] = true; cells[i] = p.frame; }
            }

        for (int y = 0; y < g; y++)
            for (int x = 0; x < g; x++)
            {
                int i = y * g + x;
                if (body[i])
                {
                    bool up = !In(body, x, y + 1), down = !In(body, x, y - 1);
                    bool left = !In(body, x - 1, y), right = !In(body, x + 1, y);

                    // Light from the top-left, but vertical faces win: an
                    // arrowhead's diagonals are open on a side AND above/below,
                    // and PixelGlyph.Shaded's side-first rule would shade its
                    // whole upper diagonal as if it faced away from the light.
                    if (up && !down)         cells[i] = p.highlight;
                    else if (down && !up)    cells[i] = p.shadow;
                    else if (left && !right) cells[i] = p.highlight;
                    else if (right && !left) cells[i] = p.shadow;
                    else                     cells[i] = p.fill;
                }
                else if (!framed[i] && Touches(framed, x, y))
                {
                    cells[i] = p.outline;
                }
            }
        return cells;
    }
}
