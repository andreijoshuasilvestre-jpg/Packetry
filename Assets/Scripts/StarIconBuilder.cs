using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally draws a small pixel-art 5-point star icon — same runtime-
/// generated, point-filtered, upscaled-blocky approach as CheckmarkIconBuilder/
/// PlateSpriteBuilder (see those classes). Exists so LevelCompletePanel's star
/// rating row doesn't depend on a "★" glyph actually existing in whatever font
/// is assigned — TMP's fallback for a missing glyph renders as an oversized,
/// badly-positioned notdef box (reported as "squares instead of stars, way
/// off from the panel"), not a scaled-down star, so a bitmap glyph can't fix
/// this no matter which font is picked. A dedicated Image + baked sprite
/// sidesteps the whole font-coverage question.
///
/// The star is a hand-placed pixel mask (see Pattern) on a coarse GridSize x
/// GridSize grid, and each cell is then blown up into a solid pixelScale
/// block, so it reads as genuine pixel art — a 1px dark outline plus
/// top-left highlight / bottom-right shadow cells, like the PixeloidMono font
/// it sits next to. Draw it at a whole multiple of GridSize on screen (see
/// DisplaySize) so every cell stays the same width.
///
/// Fill color is baked into the sprite (not tinted via Image.color) so a
/// filled and an empty star are two distinct cached sprites, same convention
/// CheckmarkIconBuilder uses for its stroke color; the outline/highlight/
/// shadow shades are all derived from that one fill.
/// </summary>
public static class StarIconBuilder
{
    // Shared by every star rating display (LevelCompletePanel's reveal row,
    // LevelSelectManager's per-card rows and total) so earned/unearned read
    // the same everywhere.
    public static readonly Color EarnedColor   = GameTheme.Warning;
    public static readonly Color UnearnedColor = new Color(0.35f, 0.36f, 0.40f, 1f);

    // Near-black cavity of GetEngravedSprite's empty slot.
    static readonly Color32 HoleColor = new Color32(14, 15, 19, 255);

    // Star silhouette, top row first. Hand-placed rather than sampled from a
    // star polygon: at 11 cells a sampled regular star drops the two
    // diagonal leg tips (they fall between cell centers), leaving stubby
    // legs under a full-length top point. Here every point tapers the same
    // 1 -> 2 -> 3 cells from its tip (the top point runs straight up, so
    // its 1-cell sliver is one row longer to read as long as a diagonal),
    // and the tips sit where a regular star's would on this grid.
    static readonly string[] Pattern =
    {
        ".....#.....",
        ".....#.....",
        "....###....",
        "....###....",
        "###########",
        ".#########.",
        "..#######..",
        "...#####...",
        "..###.###..",
        "..##...##..",
        "..#.....#..",
    };

    const int PatternSize = 11;

    /// <summary>Star pixels per side: the pattern plus a 1-cell margin on
    /// every side for the outline ring (see PixelGlyph.Pad). Odd, so the
    /// upright star has a single center column.</summary>
    public const int GridSize = PatternSize + 2;

    /// <summary>On-screen size (UI units) that gives every star pixel an even
    /// 3x3 block — about the block size of the PixeloidMono labels beside it.</summary>
    public const float DisplaySize = GridSize * 3;

    static readonly Dictionary<Params, Sprite> s_cache = new();

    readonly struct Params : IEquatable<Params>
    {
        public readonly Color32 fill;
        public readonly int pixelScale;
        public readonly bool engraved;

        public Params(Color32 fill, int pixelScale, bool engraved)
        {
            this.fill = fill;
            this.pixelScale = pixelScale;
            this.engraved = engraved;
        }

        public bool Equals(Params o) => fill.Equals(o.fill) && pixelScale == o.pixelScale && engraved == o.engraved;
        public override bool Equals(object obj) => obj is Params p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(fill, pixelScale, engraved);
    }

    /// <summary>
    /// Builds (or reuses a cached) upright 5-point pixel-art star Sprite,
    /// point-filtered, with each grid cell upscaled to a
    /// <paramref name="pixelScale"/>-texel block.
    /// </summary>
    public static Sprite GetSprite(Color32 fill, int pixelScale = 6) => Get(new Params(fill, pixelScale, engraved: false));

    /// <summary>
    /// An empty star slot drawn as a star-shaped hole dug into the surface
    /// under it (see PixelGlyph.Engraved) — same silhouette and grid as
    /// GetSprite, so an earned star fills its hole exactly.
    /// </summary>
    public static Sprite GetEngravedSprite(int pixelScale = 6) => Get(new Params(HoleColor, pixelScale, engraved: true));

    static Sprite Get(Params key)
    {
        if (s_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        // Outline/highlight/shadow cells — see PixelGlyph.Shaded (this
        // star's look, since shared with CheckmarkIconBuilder's shaded tick).
        bool[] mask = RasterizeStar();
        Color32[] cells = key.engraved
            ? PixelGlyph.Engraved(mask, GridSize, key.fill)
            : PixelGlyph.Shaded(mask, GridSize, key.fill);

        var sprite = PixelGlyph.ToSprite(cells, GridSize, key.pixelScale, key.engraved ? "StarHole" : "StarIcon");
        s_cache[key] = sprite;
        return sprite;
    }

    // Pattern -> coverage mask (row 0 at the bottom, like Texture2D), padded
    // out to GridSize so the outline ring has room.
    static bool[] RasterizeStar()
    {
        var mask = new bool[PatternSize * PatternSize];
        for (int row = 0; row < PatternSize; row++)
            for (int x = 0; x < PatternSize; x++)
                mask[(PatternSize - 1 - row) * PatternSize + x] = Pattern[row][x] == '#';
        return PixelGlyph.Pad(mask, PatternSize, 1);
    }
}
