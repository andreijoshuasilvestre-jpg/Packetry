using UnityEngine;

/// <summary>What a guidance marker's mouse is showing the player to do at its
/// spot — which button lights up and how it moves (see MarkerGlyphs.GesturePress).</summary>
public enum MouseGesture
{
    Click,      // a quick left-click — the default
    RightClick, // a quick right-click (open a device's CLI / address popup)
    Hold,       // press and keep holding — where a drag starts
    Release,    // held, then let go — where a drag ends
}

/// <summary>
/// A built mouse glyph: the outlined, tintable body plus one flat overlay per
/// button, all on the same padded grid so they stack exactly — the marker
/// tints the body its own color and lights the gesture's button separately.
/// </summary>
public sealed class MouseGlyph
{
    public Sprite Body, LeftButton, RightButton;
    public int Cells;    // glyph height in cells, outline excluded
    public float Aspect; // glyph width / height, outline excluded

    public float Span => MarkerGlyphs.Span(Cells);
    public Sprite Button(MouseGesture g) => g == MouseGesture.RightClick ? RightButton : LeftButton;
}

/// <summary>
/// Pixel-art art for the guidance markers — WorldPulseMarker's mouse and
/// pulse rings, UIPulseMarker's mouse, DragPathGuide's chevrons — rasterized
/// once per logical cell by PixelGlyph (the same pipeline as the ping
/// checkmark/cross and PingSelectBadge), so they read as the same chunky
/// pixel art as the rest of the game instead of smooth vector shapes.
///
/// Every glyph is near-white with a pure-black 1-cell outline
/// (PixelGlyph.TintableOutlined): the markers are tinted at runtime (marker
/// yellow, table-corner cyan, the blueprint-blue spawn sweep) by multiplying
/// in a color, which leaves the outline black under every tint — what keeps
/// them readable against a bright floor, wall or panel.
///
/// Cell sizes are matched to their surroundings: the world mouse uses the
/// ping badges' own world-space cell size (PingSelectBadge.CellWorldSize),
/// and the screen-space glyphs sit at roughly 3 canvas units per cell, the
/// same density as the HUD's plates.
/// </summary>
public static class MarkerGlyphs
{
    // Glyph sizes in logical cells, NOT counting the 1-cell outline ring
    // around them. Odd sizes give the arrow a single-cell tip and the mouse a
    // single center column for the line between its buttons.
    public const int WorldMouseCells = 43;  // tall × ~0.021 world units at WorldPulseMarker's 0.9 mouseSize ≈ PingSelectBadge.CellWorldSize
    public const int UiMouseCells    = 13;  // tall × ~3 canvas units at UIPulseMarker's 40-unit mouse
    public const int UiArrowCells    = 11;  // × ~3 canvas units — DragPathGuide's scroll hint
    public const int ChevronCells    = 10;
    public const int RingCells       = 48;  // the whole ring texture (its band sits well inside, outline included)

    const int TexelsPerCell = 4; // point-filtered upscale, same idea as the ping glyphs' pixelScale

    /// <summary>Fraction of an outlined glyph's texture the glyph itself fills —
    /// the rest is the outline ring. Callers draw the quad/Image 1/Span larger
    /// so the glyph lands at its intended size; it's centered, so a tip stays
    /// glyphSize/2 from the center.</summary>
    public static float Span(int glyphCells) => glyphCells / (float)(glyphCells + 2);

    static Sprite s_uiArrow, s_chevron, s_ring;
    static MouseGlyph s_worldMouse, s_uiMouse;

    // The small one is drawn wider and boxier — at 13 cells the world mouse's
    // rounder profile pinches its buttons down to a couple of cells each.
    public static MouseGlyph WorldMouse
    {
        get
        {
            if (s_worldMouse == null || s_worldMouse.Body == null)
                s_worldMouse = BuildMouse(WorldMouseCells, halfWidthFrac: 0.31f, roundness: 2.6f, "WorldMouseMarker");
            return s_worldMouse;
        }
    }

    public static MouseGlyph UiMouse
    {
        get
        {
            if (s_uiMouse == null || s_uiMouse.Body == null)
                s_uiMouse = BuildMouse(UiMouseCells, halfWidthFrac: 0.34f, roundness: 3f, "UIMouseMarker");
            return s_uiMouse;
        }
    }

    // ── Mouse gesture timing ─────────────────────────────────────────────────
    // One press per cycle, and a cycle is exactly the gap between two of a
    // marker's pulse rings (ring loop ÷ ring count), with u = 0 the moment a
    // fresh ring leaves — so every click visibly sends out its own ripple.

    /// <summary>How far the gesture's button is pressed (0..1) at cycle phase
    /// u (0..1, see above). Click/RightClick tap it right as the ripple leaves;
    /// Hold keeps it down; Release lets go as the ripple leaves, then presses
    /// again — mostly held, where Click is mostly up.</summary>
    public static float GesturePress(MouseGesture gesture, float u)
    {
        switch (gesture)
        {
            case MouseGesture.Hold:
                return 1f;
            case MouseGesture.Release:
                return u < 0.3f ? 0f : Mathf.Clamp01((u - 0.3f) / 0.08f);
            default:
                return u < 0.2f ? 1f : 1f - Mathf.Clamp01((u - 0.2f) / 0.06f);
        }
    }

    public static Sprite UiArrow
    {
        get
        {
            if (s_uiArrow == null) s_uiArrow = BuildArrow(UiArrowCells, "UIArrowMarker");
            return s_uiArrow;
        }
    }

    public static Sprite Chevron
    {
        get
        {
            if (s_chevron == null) s_chevron = BuildChevron();
            return s_chevron;
        }
    }

    public static Texture2D RingTexture
    {
        get
        {
            if (s_ring == null) s_ring = BuildRing();
            return s_ring.texture;
        }
    }

    // Downward-pointing arrowhead: half-width tapers linearly from a single
    // cell at the tip (bottom row) to ~2/3 of the glyph width across the top.
    static Sprite BuildArrow(int g, string name)
    {
        bool[] mask = PixelGlyph.Rasterize(g, p =>
        {
            float halfWidth = Mathf.Lerp(0.5f, 0.34f * g, p.y / g);
            return Mathf.Abs(p.x - g * 0.5f) <= halfWidth;
        });
        return Outlined(mask, g, name);
    }

    // A computer mouse seen from above, buttons up: a rounded body (a
    // superellipse — higher roundness is boxier) filling the grid's height,
    // with a 1-cell row cut across it ~56% of the way up and a 1-cell column
    // cut down the middle above that. The cuts are left uncovered, so
    // TintableOutlined fills them with its black outline — the lines between
    // the buttons and the body. Each button's cells also become a flat white
    // overlay on the same padded grid, for the marker to light up.
    static MouseGlyph BuildMouse(int g, float halfWidthFrac, float roundness, string name)
    {
        float cx = g * 0.5f, cy = g * 0.5f;
        float a = g * halfWidthFrac, b = g * 0.5f - 0.25f;
        int dividerRow = Mathf.RoundToInt(g * 0.56f);
        int centerCol = g / 2;

        var body  = new bool[g * g];
        var left  = new bool[g * g];
        var right = new bool[g * g];
        int minX = g, maxX = -1;
        for (int y = 0; y < g; y++)
        {
            for (int x = 0; x < g; x++)
            {
                float nx = Mathf.Abs(x + 0.5f - cx) / a, ny = Mathf.Abs(y + 0.5f - cy) / b;
                if (Mathf.Pow(nx, roundness) + Mathf.Pow(ny, roundness) > 1f) continue;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);

                bool cut = y == dividerRow || (x == centerCol && y > dividerRow);
                if (cut) continue;

                int i = y * g + x;
                body[i] = true;
                if (y > dividerRow) { if (x < centerCol) left[i] = true; else right[i] = true; }
            }
        }

        int padded = g + 2;
        var white = new Color32(255, 255, 255, 255);
        return new MouseGlyph
        {
            Body        = Outlined(body, g, name),
            LeftButton  = PixelGlyph.ToSprite(PixelGlyph.Flat(PixelGlyph.Pad(left, g, 1), white), padded, TexelsPerCell, name + "_Left"),
            RightButton = PixelGlyph.ToSprite(PixelGlyph.Flat(PixelGlyph.Pad(right, g, 1), white), padded, TexelsPerCell, name + "_Right"),
            Cells       = g,
            Aspect      = (maxX - minX + 1) / (float)g,
        };
    }

    // A hollow "V" pointing DOWN, same orientation convention as the arrow —
    // DragPathGuide rotates it along the trail.
    static Sprite BuildChevron()
    {
        int g = ChevronCells;
        var tip  = new Vector2(g * 0.5f, 2f);
        var armL = new Vector2(1.3f, g - 1.6f);
        var armR = new Vector2(g - 1.3f, g - 1.6f);
        bool[] mask = PixelGlyph.Rasterize(g, p =>
            Mathf.Min(PixelGlyph.DistToSegment(p, tip, armL), PixelGlyph.DistToSegment(p, tip, armR)) <= 1.05f);
        return Outlined(mask, g, "DragGuideChevron");
    }

    // The expanding pulse ring: a 3-cell band, outlined on both edges.
    static Sprite BuildRing()
    {
        int g = RingCells;
        var center = new Vector2(g * 0.5f, g * 0.5f);
        float radius = g * 0.39f;
        bool[] mask = PixelGlyph.Rasterize(g, p => Mathf.Abs(Vector2.Distance(p, center) - radius) <= 1.5f);
        return PixelGlyph.ToSprite(PixelGlyph.TintableOutlined(mask, g), g, TexelsPerCell, "WorldPulseRing");
    }

    static Sprite Outlined(bool[] mask, int g, string name)
    {
        int padded = g + 2;
        var cells = PixelGlyph.TintableOutlined(PixelGlyph.Pad(mask, g, 1), padded);
        return PixelGlyph.ToSprite(cells, padded, TexelsPerCell, name);
    }
}
