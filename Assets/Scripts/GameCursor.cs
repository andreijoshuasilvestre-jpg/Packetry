using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies a custom in-game cursor in place of the OS default, and swaps its
/// artwork to match whichever toggle-style interaction mode is active (cable
/// connect, device drag, remove tools, etc.) so the cursor itself signals
/// what mode the player is in.
///
/// Per-mode art lives in Assets/Resources/Cursors/*.png (transparent, glyph-
/// only — the sidebar's own UIIcons/*.png can't be reused here since those
/// bake in a full button plate + label). Modes without dedicated art
/// (idle default, table place/drag, remove-table) fall back to a procedural
/// colour-ring placeholder tinted to match that action's sidebar colour —
/// drop a matching Cursors/*.png in later and add it to ResourcePaths to
/// replace the fallback.
/// </summary>
public static class GameCursor
{
    private enum Slot { Default, PC, Switch, Router, Cable, RemoveCable, RemoveDevice, RemoveTable, Table, Ping, SendPacket }

    private const int ProceduralSize = 24;
    private const int CursorSize = 160;
    private const string ResourceFolder = "Cursors/";

    private static readonly Dictionary<Slot, string> ResourcePaths = new Dictionary<Slot, string>
    {
        { Slot.PC,           ResourceFolder + "PC_Cursor" },
        { Slot.Switch,       ResourceFolder + "Switch_Cursor" },
        { Slot.Router,       ResourceFolder + "Router_Cursor" },
        { Slot.Cable,        ResourceFolder + "Cable_Cursor" },
        { Slot.RemoveCable,  ResourceFolder + "RemoveCable_Cursor" },
        { Slot.RemoveDevice, ResourceFolder + "RemoveDevice_Cursor" },
        { Slot.Ping,         ResourceFolder + "Ping_Cursor" },
        { Slot.SendPacket,   ResourceFolder + "SendPacket_Cursor" },
    };

    // Fallback tint for slots with no dedicated cursor art yet — matches
    // NetworkUIManager's Icon* click-glow colours for the same action.
    private static readonly Dictionary<Slot, Color> ProceduralColors = new Dictionary<Slot, Color>
    {
        { Slot.Default,     Color.white },
        { Slot.Table,       new Color(0.80f, 0.65f, 0.40f, 1f) },
        { Slot.RemoveTable, new Color(0.90f, 0.62f, 0.30f, 1f) },
    };

    private static readonly Dictionary<Slot, Texture2D> resourceCache = new Dictionary<Slot, Texture2D>();
    private static readonly Dictionary<Color, Texture2D> proceduralCache = new Dictionary<Color, Texture2D>();
    private static Slot? appliedSlot;

    public static void Apply() => ApplySlot(Slot.Default);

    /// <summary>
    /// Call once a frame with the same toggle-mode flags NetworkUIManager
    /// already polls to light up sidebar buttons. Mirrors InteractionMode's
    /// mutual exclusion — only one of these should ever be true at once, but
    /// the if/else order still breaks ties sensibly if that ever changes.
    /// </summary>
    public static void Refresh(
        bool connectingCable, bool removingCable, bool removingDevice, bool removingTable,
        bool placingOrDraggingTable, bool pinging, bool sendingPacket,
        bool draggingDevice, DeviceType? draggingDeviceType)
    {
        Slot slot;
        if (connectingCable) slot = Slot.Cable;
        else if (removingCable) slot = Slot.RemoveCable;
        else if (removingDevice) slot = Slot.RemoveDevice;
        else if (removingTable) slot = Slot.RemoveTable;
        else if (placingOrDraggingTable) slot = Slot.Table;
        else if (pinging) slot = Slot.Ping;
        else if (sendingPacket) slot = Slot.SendPacket;
        else if (draggingDevice) slot = SlotForDevice(draggingDeviceType);
        else slot = Slot.Default;

        ApplySlot(slot);
    }

    private static Slot SlotForDevice(DeviceType? type)
    {
        switch (type)
        {
            case DeviceType.PC: return Slot.PC;
            case DeviceType.Switch: return Slot.Switch;
            case DeviceType.Router: return Slot.Router;
            default: return Slot.Default;
        }
    }

    private static void ApplySlot(Slot slot)
    {
        if (appliedSlot.HasValue && appliedSlot.Value == slot) return;

        if (ResourcePaths.TryGetValue(slot, out var path))
        {
            if (!resourceCache.TryGetValue(slot, out var tex) || tex == null)
            {
                var loaded = Resources.Load<Texture2D>(path);
                tex = loaded != null ? FitToSquare(loaded) : null;
                resourceCache[slot] = tex;
            }

            if (tex != null)
            {
                appliedSlot = slot;
                Vector2 hotspot = new Vector2(tex.width / 2f, tex.height / 2f);
                Cursor.SetCursor(tex, hotspot, CursorMode.Auto);
                return;
            }
        }

        appliedSlot = slot;
        Color color = ProceduralColors.TryGetValue(slot, out var c) ? c : Color.white;
        ApplyProcedural(color);
    }

    // Scales the art so its long edge fills a CursorSize square, centred on a
    // transparent canvas — Windows stretches a non-square hardware cursor to
    // fill a square, which would otherwise squeeze wide art like Switch_Cursor.
    // Source PNGs are imported at 256px so this only ever scales down.
    private static Texture2D FitToSquare(Texture2D src)
    {
        float scale = (float)CursorSize / Mathf.Max(src.width, src.height);
        int w = Mathf.RoundToInt(src.width * scale);
        int h = Mathf.RoundToInt(src.height * scale);
        int ox = (CursorSize - w) / 2;
        int oy = (CursorSize - h) / 2;

        var pixels = new Color[CursorSize * CursorSize];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[(oy + y) * CursorSize + ox + x] = src.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);

        var dst = new Texture2D(CursorSize, CursorSize, TextureFormat.RGBA32, false);
        dst.filterMode = FilterMode.Bilinear;
        dst.wrapMode = TextureWrapMode.Clamp;
        dst.SetPixels(pixels);
        dst.Apply();
        return dst;
    }

    private static void ApplyProcedural(Color fill)
    {
        if (!proceduralCache.TryGetValue(fill, out var tex))
        {
            tex = BuildProceduralTexture(fill);
            proceduralCache[fill] = tex;
        }

        Vector2 hotspot = new Vector2(ProceduralSize / 2f, ProceduralSize / 2f);
        Cursor.SetCursor(tex, hotspot, CursorMode.Auto);
    }

    private static Texture2D BuildProceduralTexture(Color fill)
    {
        var tex = new Texture2D(ProceduralSize, ProceduralSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color outline = new Color(0.05f, 0.05f, 0.05f, 1f);

        Vector2 center = new Vector2(ProceduralSize / 2f, ProceduralSize / 2f);
        float outerRadius = ProceduralSize / 2f - 1f;
        float innerRadius = outerRadius - 2f;

        for (int y = 0; y < ProceduralSize; y++)
        {
            for (int x = 0; x < ProceduralSize; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                Color pixel;
                if (dist <= innerRadius)
                    pixel = fill;
                else if (dist <= outerRadius)
                    pixel = outline;
                else
                    pixel = clear;

                tex.SetPixel(x, y, pixel);
            }
        }

        tex.Apply();
        return tex;
    }
}
