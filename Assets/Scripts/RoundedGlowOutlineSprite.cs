using UnityEngine;

/// <summary>
/// A soft-edged ring that traces a rounded rectangle, baked once as a
/// 9-sliceable sprite (see border in Sprite.Create below). Used as the
/// sidebar click glow's outer layer instead of RadialGlowSprite's plain
/// circle: a circle stretched to a button's own (non-square) width/height
/// reads as a squashed oval sitting behind the artwork, not a light hugging
/// its shape. Because this is sliced, the corner radius and ring thickness
/// stay fixed in pixels no matter how the button is stretched, so the ring
/// keeps tracing the button's actual rounded-rect silhouette at any size —
/// see SidebarIconButtonFX/NetworkUIManager.CreateSidebarButton.
/// </summary>
public static class RoundedGlowOutlineSprite
{
    static Sprite shared;

    // Corner radius and ring thickness are both fixed pixel distances,
    // measured in the source texture; Border must cover both so a 9-sliced
    // stretch never has to squash a corner into the flat edge region.
    const int TextureSize   = 128;
    const float CornerRadius = 16f;
    const float RingBlur     = 12f; // half-width of the soft falloff on each side of the true edge
    const int Border         = 40;  // >= CornerRadius + RingBlur, with margin

    public static Sprite Get()
    {
        if (shared != null) return shared;

        var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float half = TextureSize * 0.5f;
        // Half-extents of the rounded rect's true boundary (in pixels from
        // the texture's center) — kept a little inside the texture edge so
        // the outward half of the ring's blur has room to fully fade to 0.
        float rectHalf = half - RingBlur - 2f;

        var pixels = new Color32[TextureSize * TextureSize];
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                Vector2 p = new Vector2(x - half + 0.5f, y - half + 0.5f);

                // Signed distance to a rounded rectangle: negative inside,
                // 0 exactly on the boundary, positive outside.
                Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - new Vector2(rectHalf - CornerRadius, rectHalf - CornerRadius);
                float dist = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                             + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - CornerRadius;

                // A ring, not a filled blob: brightest exactly on the
                // boundary, fading to 0 over RingBlur pixels on either side.
                float alpha = 1f - Mathf.Clamp01(Mathf.Abs(dist) / RingBlur);
                alpha = alpha * alpha; // sharper falloff, less "hazy square"

                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        var border = new Vector4(Border, Border, Border, Border);
        shared = Sprite.Create(tex, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        return shared;
    }
}
