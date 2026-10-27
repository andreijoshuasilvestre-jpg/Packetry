using UnityEngine;

/// <summary>
/// Mesh-renderer equivalent of ScrollingBackground, for the gameplay
/// background quad (see GameBootstrap.SetupBackgroundQuad) — a Screen
/// Space - Camera Canvas/RawImage never actually draws anything on that
/// camera in this project's URP setup (confirmed: even a flat-colored
/// UI Image renders nothing there, while ordinary 3D geometry through the
/// same camera always renders fine), so the pattern is drawn as a plain
/// textured quad instead of a UI graphic.
///
/// Scrolls off ScrollingBackground.SharedOffset, the same clock the menu's
/// layers use, so the first gameplay frame already shows the pattern exactly
/// where the menu (and LoadingScreenManager's bridge copy) left it.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class ScrollingQuadBackground : MonoBehaviour
{
    [SerializeField] Vector2 scrollSpeed = ScrollingBackground.DefaultScrollSpeed;

    Renderer rend;
    Vector2 originOffset;

    /// <summary>Constant UV shift that puts the screen's bottom-left corner
    /// (not the oversized quad's own, off-screen one) on the pattern's grid
    /// origin — see GameBootstrap.SetupBackgroundQuad. Applied right away:
    /// a scene activated mid-frame can render once before this component's
    /// first Update.</summary>
    public void SetOriginOffset(Vector2 offset)
    {
        originOffset = offset;
        ApplyOffset();
    }

    void Awake() => rend = GetComponent<Renderer>();

    void Update() => ApplyOffset();

    void ApplyOffset()
    {
        rend.material.mainTextureOffset = ScrollingBackground.SharedOffset(scrollSpeed) + originOffset;
    }
}
