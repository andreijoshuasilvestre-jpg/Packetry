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
    /// Stable color for a subnet, derived from its network address — same
    /// subnet always hashes to the same palette slot, so devices on the same
    /// subnet visually match (DeviceAddress uses this to tint PCs). The slot
    /// count is player-configurable (SettingsPanelManager's Subnet Colors
    /// carousel, backed by SaveManager.Settings.subnetColorSlotCount, default
    /// 5) — more slots means fewer different subnets ever collide onto the
    /// same color.
    /// </summary>
    public static Color SubnetTint(uint networkAddress)
    {
        int slotCount = Mathf.Max(1, SaveManager.Settings.subnetColorSlotCount);
        uint mixed = MixBits(networkAddress);
        int index = (int)(mixed % (uint)slotCount);
        return SlotColor(index);
    }

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

    // A network address is always a multiple of its subnet size (e.g. every
    // /24 ends in .0 — a multiple of 256), so its low bits are always zero and
    // a plain "value % paletteLength" only ever looks at those dead bits,
    // mapping every /24 to the same entry. This avalanche mix (MurmurHash3's
    // finalizer) spreads all four octets across the result first.
    static uint MixBits(uint x)
    {
        x ^= x >> 16;
        x *= 0x85ebca6bu;
        x ^= x >> 13;
        x *= 0xc2b2ae35u;
        x ^= x >> 16;
        return x;
    }
}
