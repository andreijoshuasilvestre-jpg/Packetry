using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared "technical drawing" hologram look — a grid-lined, blueprint-palette
/// transparent (or opaque) material — used by every placement preview in the
/// game so they all read as the same material instead of each interaction
/// mode (drag ghost, table preview, ...) inventing its own.
///
/// Two render-pipeline quirks drove the implementation below (verified live
/// in-editor rather than assumed):
///  - "Universal Render Pipeline/Particles/Unlit" defaults to an Opaque
///    surface; alpha is silently ignored unless _Surface/_Blend/_SrcBlend/
///    _DstBlend/_ZWrite/renderQueue and the _SURFACE_TYPE_TRANSPARENT keyword
///    are all set explicitly to the Transparent configuration.
///  - That same shader does NOT honor _BaseMap_ST tiling (mainTextureScale is
///    a no-op on it) — so instead of tiling one texture, each part gets a
///    grid texture baked with exactly the right number of cells for its own
///    scale, cached per (cellsX, cellsY) pair.
/// </summary>
public static class HologramMaterial
{
    const int PxPerCell = 8;

    // White line / blue fill — same blueprint palette as
    // BlueprintConstruction.shader's _LineColor/_FillColor, so every
    // hologram in the game reads as the same "technical drawing" material by
    // default. Callers can override the line/fill color (see CreateInstance)
    // for previews that need to stand out from that shared default — e.g.
    // TableManager uses a green line color since a table's flat footprint
    // reads poorly against the ground in the default white/blue.
    static readonly Color32 LineColor = new Color32(242, 247, 255, 220);
    static readonly Color32 FillColor = new Color32(10, 41, 191, 55);

    // Pushes a hologram's rendered brightness above 1.0 so URP's Bloom (see
    // DefaultVolumeProfile.asset, wired up by GameBootstrap.SetupCamera)
    // actually picks it up — the same idea as ConstructionRevealEffect's own
    // Line/FillGlowMultiplier, so the drag ghost, the table preview, and the
    // one-shot construction reveal all read as one consistent glowing
    // "material" instead of only the reveal effect doing it. Applied as one
    // uniform multiplier to whatever tint a caller passes, rather than baked
    // separately per line/fill color: the near-white, near-opaque grid LINES
    // already sit close to 1.0 and cross Bloom's 0.9 threshold once boosted,
    // while the darker, much more transparent fill mostly doesn't survive
    // alpha-blending against the (dark) background at the same threshold —
    // so the one multiplier already reads as "the lines glow, the fill stays
    // a dim wash" without any extra bookkeeping.
    public const float GlowMultiplier = 2.2f;

    // Breathing alpha so a hologram reads as a projected, living volume
    // instead of a static flat texture — the one implementation every caller
    // (drag ghost, table preview) shares instead of each animating its own
    // slightly-different sine wave.
    public static float Pulse(float speed = 2.4f, float min = 0.72f, float max = 0.95f)
        => Mathf.Lerp(min, max, (Mathf.Sin(Time.time * speed) + 1f) * 0.5f);

    // The one place every caller should set a hologram instance's tint —
    // folds in the glow boost so it can't drift between call sites (or get
    // forgotten by a new one).
    public static void ApplyTint(Material instance, Color tint)
    {
        if (instance == null) return;
        instance.color = new Color(tint.r * GlowMultiplier, tint.g * GlowMultiplier, tint.b * GlowMultiplier, tint.a);
    }

    static Material s_transparentTemplate;
    static Material s_opaqueTemplate;
    // Keyed on color too (not just cell count) — different callers can now
    // request different line/fill colors for the same cell dimensions.
    static readonly Dictionary<(int, int, uint, uint), Texture2D> s_gridTextureCache = new();

    static uint PackColor(Color32 c) => ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | c.a;

    static Material TransparentTemplate()
    {
        if (s_transparentTemplate != null) return s_transparentTemplate;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader) { name = "HologramTemplate" };
        mat.SetFloat("_Surface", 1f); // Transparent
        mat.SetFloat("_Blend", 0f);   // Alpha blend
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        s_transparentTemplate = mat;
        return mat;
    }

    // Same grid-lined look, but opaque — used when the previewed object
    // shouldn't read as "dissolving" (e.g. repositioning an already-placed
    // device).
    static Material OpaqueTemplate()
    {
        if (s_opaqueTemplate != null) return s_opaqueTemplate;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader) { name = "HologramSolidTemplate" };
        mat.SetFloat("_Surface", 0f); // Opaque
        mat.SetFloat("_ZWrite", 1f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        s_opaqueTemplate = mat;
        return mat;
    }

    static Texture2D GetGridTexture(int cellsX, int cellsY, Color32 lineColor, Color32 fillColor)
    {
        cellsX = Mathf.Clamp(cellsX, 1, 24);
        cellsY = Mathf.Clamp(cellsY, 1, 24);
        var key = (cellsX, cellsY, PackColor(lineColor), PackColor(fillColor));
        if (s_gridTextureCache.TryGetValue(key, out Texture2D cached)) return cached;

        int w = cellsX * PxPerCell, h = cellsY * PxPerCell;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = $"HologramGrid_{cellsX}x{cellsY}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool onLine = (x % PxPerCell) < 1 || (y % PxPerCell) < 1;
                pixels[y * w + x] = onLine ? lineColor : fillColor;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        s_gridTextureCache[key] = tex;
        return tex;
    }

    // Re-fits an existing hologram instance's grid texture to a new
    // world-space scale, without touching its tint — cheap enough to call
    // every frame while a preview is being resized (the texture itself is
    // cache-hit after the first frame at any given cell count). Omit
    // lineColor/fillColor to keep the shared default blueprint palette.
    public static void UpdateGridTexture(Material instance, Vector3 worldScale, float gridCellSize,
        Color32? lineColor = null, Color32? fillColor = null)
    {
        int cellsX = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(worldScale.x, worldScale.z) / gridCellSize));
        int cellsY = Mathf.Max(1, Mathf.RoundToInt(worldScale.y / gridCellSize));
        instance.mainTexture = GetGridTexture(cellsX, cellsY, lineColor ?? LineColor, fillColor ?? FillColor);
    }

    // Builds one grid-lined hologram material instance sized for a part with
    // the given world-space scale (grid density follows scale so the line
    // spacing reads as a constant world-space cell size across differently
    // sized parts).
    public static Material CreateInstance(Vector3 worldScale, float gridCellSize, bool transparent, Color tint,
        Color32? lineColor = null, Color32? fillColor = null)
    {
        Material template = transparent ? TransparentTemplate() : OpaqueTemplate();
        var instance = new Material(template) { name = transparent ? "HologramInstance" : "HologramSolidInstance" };
        UpdateGridTexture(instance, worldScale, gridCellSize, lineColor, fillColor);
        ApplyTint(instance, tint);
        return instance;
    }
}
