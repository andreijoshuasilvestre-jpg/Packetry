using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Flat, Packet-Tracer-style device symbols for NetworkDiagramPanel's 2D
/// view — a monitor for a PC, a flat box with crossing arrows for a Switch,
/// a disc crossed by an X for a Router — in each device's sidebar color,
/// sampled once per logical cell through PixelGlyph like the game's other
/// procedural icons.
/// </summary>
public static class DiagramIconBuilder
{
    const int Size = 20;
    const int TexelsPerCell = 4;

    static readonly Dictionary<DeviceType, Sprite> s_cache = new();

    public static Color32 ColorFor(DeviceType type) => type switch
    {
        DeviceType.PC     => new Color32(90, 190, 242, 255),
        DeviceType.Switch => new Color32(102, 217, 140, 255),
        _                 => new Color32(242, 178, 77, 255),
    };

    public static Sprite GetSprite(DeviceType type)
    {
        if (s_cache.TryGetValue(type, out Sprite cached) && cached != null) return cached;

        System.Func<Vector2, bool> covers = type switch
        {
            DeviceType.PC     => Pc,
            DeviceType.Switch => Switch,
            _                 => Router,
        };
        bool[] mask = PixelGlyph.Pad(PixelGlyph.Rasterize(Size, covers), Size, 1);
        var sprite = PixelGlyph.ToSprite(PixelGlyph.Shaded(mask, Size + 2, ColorFor(type)), Size + 2, TexelsPerCell,
            "Diagram" + type);
        s_cache[type] = sprite;
        return sprite;
    }

    // Monitor with a dark screen, neck and foot.
    static bool Pc(Vector2 p)
    {
        bool frame  = p.x >= 2f && p.x <= 18f && p.y >= 7f && p.y <= 18f;
        bool screen = p.x >= 4f && p.x <= 16f && p.y >= 9f && p.y <= 16f;
        bool neck   = p.x >= 8.5f && p.x <= 11.5f && p.y >= 4f && p.y < 7f;
        bool foot   = p.x >= 5f && p.x <= 15f && p.y >= 2f && p.y < 4f;
        return (frame && !screen) || neck || foot;
    }

    // A flat box with two opposed arrows cut through it.
    static bool Switch(Vector2 p)
    {
        bool box = p.x >= 1f && p.x <= 19f && p.y >= 4f && p.y <= 16f;
        if (!box) return false;
        if (p.y < 5f || p.y > 15f) return true; // keep the top/bottom edges solid
        bool upperShaft = p.y >= 11f && p.y <= 12.6f && p.x >= 4f && p.x <= 12.5f;
        bool upperHead  = InTriangle(p, new Vector2(16.6f, 11.8f), new Vector2(12f, 14.3f), new Vector2(12f, 9.3f));
        bool lowerShaft = p.y >= 7.4f && p.y <= 9f && p.x >= 7.5f && p.x <= 16f;
        bool lowerHead  = InTriangle(p, new Vector2(3.4f, 8.2f), new Vector2(8f, 10.7f), new Vector2(8f, 5.7f));
        // Arrows are holes in the box, so they read on any fill.
        return !(upperShaft || upperHead || lowerShaft || lowerHead);
    }

    // A disc crossed by an X — the classic router symbol's crossing arrows,
    // pared down to what still reads at this size.
    static bool Router(Vector2 p)
    {
        Vector2 d = p - new Vector2(10f, 10f);
        if (d.magnitude > 8.8f) return false;
        bool diagonalA = Mathf.Abs(d.x - d.y) * 0.7071f <= 0.8f && d.magnitude <= 6.4f;
        bool diagonalB = Mathf.Abs(d.x + d.y) * 0.7071f <= 0.8f && d.magnitude <= 6.4f;
        return !(diagonalA || diagonalB); // grooves, like the switch's arrows
    }

    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }
}
