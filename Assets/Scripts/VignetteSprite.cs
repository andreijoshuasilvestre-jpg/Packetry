using UnityEngine;

/// <summary>
/// Shared soft square gradient sprite — transparent through most of the
/// middle, ramping up to opaque black only right at the edge — generated
/// once at runtime, same baked-once/tint-per-use approach as
/// RadialGlowSprite, just inverted (darkens outward instead of glowing).
/// Stretched non-uniformly across a full-screen Image (Image.Type.Simple)
/// reads as an elliptical vignette that matches the screen's own aspect
/// ratio, rather than a perfect circle — see
/// MainMenuCanvasBuilder.BuildVignette.
/// </summary>
public static class VignetteSprite
{
    static Sprite shared;

    public static Sprite Get()
    {
        if (shared != null) return shared;

        const int size = 128;
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
                // Fully transparent through the inner ~45% radius, then ramps
                // to fully opaque by the edge — keeps the middle of the
                // screen (where menu content sits) completely untouched.
                float alpha = Mathf.Clamp01((dist - 0.45f) / 0.55f);
                pixels[y * size + x] = new Color(0f, 0f, 0f, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        shared = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return shared;
    }
}
