using UnityEngine;

/// <summary>
/// One shared soft radial-gradient sprite (white, alpha falls off to the edge),
/// generated once at runtime and tinted per-use as a click/hover glow. Matches
/// how the rest of the project builds its visuals procedurally rather than
/// shipping raster assets (see DeviceModelBuilder). Originally lived inside
/// SidebarIconButtonFX; pulled out so MenuButtonFX can reuse the same glow
/// without duplicating the texture-gen code.
/// </summary>
public static class RadialGlowSprite
{
    static Sprite shared;

    public static Sprite Get()
    {
        if (shared != null) return shared;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.5f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / radius;
                float alpha = Mathf.Clamp01(1f - dist);
                alpha = alpha * alpha; // sharper falloff toward the edge
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        shared = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return shared;
    }
}
