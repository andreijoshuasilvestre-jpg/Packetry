using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Subtle full-screen CRT-style overlay — faint horizontal scanlines plus a
/// soft corner vignette (see Assets/Shaders/CRTOverlay.shader) — rendered as
/// the topmost, non-interactive layer of a Screen Space - Overlay canvas.
///
/// Experimental "try it and see" polish pass: call Build() last, after every
/// other child of the canvas has been added (it snaps itself to the last
/// sibling slot, but that only puts it above whatever already exists at
/// that point — content added afterward would render over it). If this
/// doesn't earn its keep, deleting the Build() call sites is the whole
/// rollback; nothing else depends on this class.
/// </summary>
public static class CRTOverlay
{
    const string ShaderName = "UI/CRTOverlay";
    static Material sharedMaterial;

    static Material GetMaterial()
    {
        if (sharedMaterial != null) return sharedMaterial;
        var shader = Shader.Find(ShaderName);
        if (shader == null) return null; // stripped from a build, or shader failed to compile — skip the effect rather than error

        sharedMaterial = new Material(shader);
        sharedMaterial.SetFloat("_ScanlineIntensity", 0.05f);
        sharedMaterial.SetFloat("_ScanlineCount", 340f);
        sharedMaterial.SetFloat("_VignetteIntensity", 0.28f);
        sharedMaterial.SetFloat("_VignetteInner", 0.35f);
        sharedMaterial.SetFloat("_VignetteOuter", 0.9f);
        return sharedMaterial;
    }

    /// <summary>
    /// Builds the full-screen overlay as the last sibling under
    /// <paramref name="parent"/> (a Canvas's root RectTransform) so it
    /// paints over everything already built under it. Returns null (a
    /// harmless no-op) if the shader isn't available.
    /// </summary>
    public static CRTOverlayEffect Build(Transform parent)
    {
        var material = GetMaterial();
        if (material == null) return null;

        var go = new GameObject("CRTOverlay", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.SetAsLastSibling();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.material = material; // shared instance across every CRTOverlay in the game — the shader ignores _MainTex/sprite content entirely, so there's nothing per-instance to configure beyond the RectTransform above

        return go.AddComponent<CRTOverlayEffect>();
    }
}

/// <summary>
/// Drives a slow scanline roll on a CRTOverlay's shared material — kept
/// deliberately slow enough to read as "the screen is alive" rather than a
/// visibly moving pattern, which is the difference between this reading as
/// CRT texture vs. a distracting animated overlay.
/// </summary>
public class CRTOverlayEffect : MonoBehaviour
{
    [SerializeField] float rollSpeed = 0.015f; // UV units/sec
    Material material;

    void Awake()
    {
        material = GetComponent<Image>().material;
    }

    void Update()
    {
        if (material == null) return;
        material.SetFloat("_ScanOffset", (Time.unscaledTime * rollSpeed) % 1f);
    }
}
