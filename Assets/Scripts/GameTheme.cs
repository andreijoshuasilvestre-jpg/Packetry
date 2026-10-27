using UnityEngine;

/// <summary>
/// Shared color palette for feedback UI (NotificationManager, etc.), matching
/// the dark navy HUD look already used by NetworkUIManager/TaskUIManager.
/// </summary>
public static class GameTheme
{
    public static readonly Color PanelBg = new Color(0.06f, 0.08f, 0.13f, 0.95f);

    public static readonly Color Info    = new Color(0.35f, 0.75f, 0.95f, 1f);
    public static readonly Color Success = new Color(0.30f, 0.80f, 0.45f, 1f);
    public static readonly Color Warning = new Color(0.95f, 0.70f, 0.30f, 1f);
    public static readonly Color Danger  = new Color(0.90f, 0.30f, 0.30f, 1f);

    // Matches ConstructionRevealEffect's DefaultSparkColor (the device-placement
    // reveal/spark blue) — reused by menu button click FX (MenuButtonFX, UISparks)
    // so it reads as the same "electric construction" language, not a different blue.
    public static readonly Color ElectricBlue = new Color(0.25f, 0.55f, 1f, 1f);

    // Plain categorical hues at normal saturation/value (~75-85% sat, ~80-90%
    // val) — a raw HSV hash reads as pastel at the high value/mid saturation
    // needed to stay legible on a dark scene, and the previous curated
    // industrial palette (steel/rust/olive) leaned too muted/muddy once
    // blended onto a device. This set is meant to read as a clean "that one's
    // red, that one's blue" at a glance instead.
    static readonly Color[] SubnetPalette =
    {
        new Color(0.85f, 0.20f, 0.20f), // red
        new Color(0.20f, 0.45f, 0.85f), // blue
        new Color(0.20f, 0.70f, 0.30f), // green
        new Color(0.90f, 0.50f, 0.10f), // orange
        new Color(0.55f, 0.25f, 0.80f), // purple
        new Color(0.10f, 0.65f, 0.60f), // teal
        new Color(0.85f, 0.25f, 0.55f), // magenta
        new Color(0.80f, 0.65f, 0.10f), // gold
    };

    /// <summary>
    /// Stable color for a subnet, in the order that subnet was first assigned
    /// this level (see SubnetColorRegistry) — the first subnet configured (in
    /// practice, whichever Router interface gets its ip address set first)
    /// gets the player's slot 0 color, the second gets slot 1, and so on,
    /// rather than deriving a color from the address's own number. Devices on
    /// the same subnet visually match since they all resolve to the same
    /// registry entry (DeviceAddress uses this to tint PCs, SubnetVisualizer
    /// for Switches/Cables). Slot colors themselves are player-configurable
    /// (SettingsPanelManager's Subnet Colors carousel, backed by
    /// SaveManager.Settings.subnetColorSlotCount). A subnet is its network
    /// address and mask together (see Subnet), so a mask mismatch shows up as
    /// a color mismatch too.
    /// </summary>
    public static Color SubnetTint(Subnet subnet) =>
        SlotColor(SubnetColorRegistry.SlotFor(subnet));

    /// <summary>The color a given palette slot currently shows — the player's
    /// override if they've set one, else the auto-generated default for that
    /// slot. Also what SettingsPanelManager's carousel renders per swatch.</summary>
    public static Color SlotColor(int index)
    {
        string overrideHex = SaveManager.GetSubnetSlotColorHex(index);
        if (!string.IsNullOrEmpty(overrideHex) && ColorUtility.TryParseHtmlString("#" + overrideHex, out var parsed))
            return parsed;

        if (index < SubnetPalette.Length) return SubnetPalette[index];

        // Beyond the curated base palette (slot count grown past 8 via the
        // carousel's '+' button), generate a deterministic, well-spaced hue
        // rather than repeating/hand-authoring more entries — golden-angle
        // spacing keeps consecutive slots visually distinct indefinitely.
        float hue = (index * 0.61803398875f) % 1f;
        return Color.HSVToRGB(hue, 0.78f, 0.85f);
    }

    // Each level's own background hue, levels 1-8 in order — standalone on
    // purpose, not read from the Subnet Colors palette. It used to reuse
    // subnet slot N-1's color (player overrides included), so recoloring a
    // subnet in Settings also repainted a level's backdrop. These are the
    // hues those default slot colors gave, so every level looks the same as
    // before for a player who never recolored anything.
    static readonly Color[] LevelBackgroundPalette =
    {
        new Color(0.85f, 0.20f, 0.20f), // level 1 — red
        new Color(0.20f, 0.45f, 0.85f), // level 2 — blue
        new Color(0.20f, 0.70f, 0.30f), // level 3 — green
        new Color(0.90f, 0.50f, 0.10f), // level 4 — orange
        new Color(0.55f, 0.25f, 0.80f), // level 5 — purple
        new Color(0.10f, 0.65f, 0.60f), // level 6 — teal
        new Color(0.85f, 0.25f, 0.55f), // level 7 — magenta
        new Color(0.80f, 0.65f, 0.10f), // level 8 — gold
    };

    /// <summary>
    /// A level's ambient gameplay background (see GameBootstrap.
    /// FadeBackgroundToLevelColor) — that level's hue from
    /// LevelBackgroundPalette, darkened/desaturated down to the same low-key
    /// range the game's other dark-navy backdrops (menu canvas, loading veil)
    /// already sit in, instead of the palette's own vivid, UI-badge
    /// brightness — reads as "this level's color" without a saturated wash
    /// clashing with device/HUD art.
    /// </summary>
    public static Color LevelBackgroundColor(int level)
    {
        int index = Mathf.Max(0, level - 1);
        // Past the authored levels (9+, and the dev sandbox), same golden-angle
        // hue spacing as SlotColor's generated slots so levels stay distinct.
        float hue = (index * 0.61803398875f) % 1f;
        if (index < LevelBackgroundPalette.Length)
            Color.RGBToHSV(LevelBackgroundPalette[index], out hue, out _, out _);
        // Bright on purpose — both earlier, darker tones (val 0.125, then
        // 0.34) still read as "basically black" for several hues once
        // multiplied against the background pattern's own dark icon
        // silhouettes, hiding them. A clearly bright, saturated tint keeps
        // the pattern legible regardless of hue.
        //
        // Level 1 specifically requested brighter still — it's the very
        // first level a player ever sees, so it doubles as the first
        // impression of this whole background-color system. Bumped alone
        // rather than raising val for every level, since the shared 0.68
        // already reads correctly for the rest of the palette.
        float val = level == 1 ? 0.82f : 0.68f;
        return Color.HSVToRGB(hue, 0.55f, val);
    }

    // A fixed, curated set of legible preset colors offered by the Subnet
    // Colors picker (SettingsPanelManager) — evenly spaced around the hue
    // wheel at the same saturation/value as the auto-generated palette above,
    // so a player-picked color reads as "one more color in this game's
    // language" rather than an arbitrary clashing hue.
    public static readonly Color[] PresetPickerColors = BuildPresetPickerColors();

    static Color[] BuildPresetPickerColors()
    {
        const int count = 12;
        var colors = new Color[count];
        for (int i = 0; i < count; i++)
            colors[i] = Color.HSVToRGB((float)i / count, 0.78f, 0.85f);
        return colors;
    }
}
