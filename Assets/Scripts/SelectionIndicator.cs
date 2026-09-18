using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World-space "this is selected" indicator — replaces the old flat
/// material-tint highlight (DragDropManager/TableManager's ApplyHighlightTint,
/// CableRemoverManager's line-color swap) with three parts:
///
///  - A true mesh-silhouette outline: every one of the target's own renderers
///    gets a duplicate "shell" copy of its actual mesh (see OutlineShell.shader
///    — an inverted-hull rim, inflated along each vertex's own normal and
///    drawn back-face-only so only a thin edge peeks out around the real
///    geometry). Parented onto each source renderer at local identity, so a
///    multi-part device/table (body + ports, tabletop + 4 legs, ...) traces
///    its actual composite silhouette — not one bounding box guessed around
///    the whole thing — and the outline tracks that part's own live
///    position/rotation/scale for free (e.g. the free-rotate a still-selected
///    device can go through) with no per-frame sync needed.
///  - A small arrow hovering above the target, bobbing gently, billboarded to
///    the camera — disambiguates exactly which object is selected the same
///    way WorldPulseMarker's ping arrow does, just steady instead of a
///    one-shot ping.
///  - An optional breathing ring on the ground at the target's base — skipped
///    for a cable (see ShowOnCable), which has no footprint to ring.
///
/// A cable has no mesh renderers to shell, so ShowOnCable takes a different
/// path: a wider duplicate LineRenderer tracing the same live curve (a
/// cable's points move every frame from its own sway/reveal, so this one
/// DOES need continuous per-frame resync, unlike the mesh shells above).
///
/// ShowDragging is the same three-part treatment for a device/table that's
/// actively being repositioned rather than just sitting selected — the
/// arrow/ring anchor re-tracks the target's live bounds every frame instead
/// of a fixed point, and the arrow additionally breathes in ALPHA (on top of
/// its usual size pulse) so "you're moving this" never looks identical to
/// "this is selected" at a glance.
///
/// One instance is owned per caller (DragDropManager, TableManager,
/// CableRemoverManager each build their own via AddComponent, same
/// one-owner-per-purpose convention WorldPulseMarker's own callers use)
/// rather than a shared singleton — InteractionMode's mutual-exclusion chain
/// already guarantees at most one of those three is ever showing at once.
/// </summary>
public class SelectionIndicator : MonoBehaviour
{
    [Header("Outline shell")]
    [SerializeField] private float thickness = 0.012f; // must match OutlineShell.shader's _Thickness default

    [Header("Arrow")]
    [SerializeField] private float arrowSize = 0.42f;
    [SerializeField] private float hoverHeight = 0.24f; // clearance above the anchor, to the arrow's tip
    [SerializeField] private float bobHeight = 0.05f;
    [SerializeField] private float pulseSpeed = 2.4f;
    [SerializeField] private float minScale = 0.92f, maxScale = 1.08f;

    [Header("Ground ring")]
    [SerializeField] private float ringPadding = 0.35f; // beyond the target's own XZ footprint
    [SerializeField] private float ringMinAlpha = 0.3f, ringMaxAlpha = 0.65f;
    [SerializeField] private float ringYOffset = 0.015f; // lifted clear of the surface to avoid z-fighting

    [Header("Cable outline")]
    [SerializeField] private float cableOutlineWidthMultiplier = 1.9f;

    [Header("Drag arrow fade")]
    // While repositioning an already-placed device/table (see ShowDragging),
    // the arrow's ALPHA breathes on top of its usual size pulse — a
    // distinctly "in motion" read, separate from the steady (alpha-constant)
    // pulse it shows while just selected — so the two states never look
    // identical. minAlpha stays well above 0 ("small") so it never fully
    // disappears (which would read as a glitch, not a fade); the slower
    // speed than the size-pulse keeps the sine easing itself visibly smooth
    // rather than a flicker.
    [SerializeField] private float dragFadeMinAlpha = 0.4f;
    [SerializeField] private float dragFadeSpeed = 1.5f;

    [Header("Spawn")]
    [SerializeField] private float popDuration = 0.16f; // quick ease-in scale/fade so the indicator doesn't just snap on

    // Same bloom-boosting trick every other glowing marker in the game uses
    // (WorldPulseMarker, HologramMaterial itself) — pushes rendered brightness
    // above 1.0 so URP's Bloom volume override picks it up as a genuine glow.
    const float GlowMultiplier = HologramMaterial.GlowMultiplier;

    private static Material s_outlineShellMat;
    private static Texture2D s_arrowTexture;
    private static Texture2D s_ringTexture;
    private static MaterialPropertyBlock s_shellBlock;

    private Color color = new Color(0.2f, 0.9f, 1f);
    private Camera cam;

    private Transform arrowQuad;
    private Renderer arrowRend;
    private MaterialPropertyBlock arrowBlock;

    private Transform ringQuad;
    private Renderer ringRend;
    private MaterialPropertyBlock ringBlock;
    private bool ringActive;
    private float ringDiameter;

    private readonly List<GameObject> shells = new List<GameObject>();
    private LineRenderer cableOutlineLine;
    private Cable trackedCable;

    // Set by ShowDragging (repositioning an already-placed device/table) —
    // unlike ShowOnMesh's fixed anchor, a dragged object moves every frame,
    // so while this is non-null LateUpdate recomputes the arrow/ring anchor
    // from its live renderer bounds instead of the one-time snapshot
    // ShowOnMesh's caller passed in. draggingRenderers is cached once (not
    // re-fetched via GetComponentsInChildren every frame) since the set of
    // renderers itself never changes mid-drag.
    private Transform draggingRoot;
    private Renderer[] draggingRenderers;

    private Vector3 arrowAnchor;
    private bool active;
    private float showTime;

    void Awake()
    {
        cam = Camera.main;

        var arrowGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        arrowGo.name = "Arrow";
        arrowGo.transform.SetParent(transform, false);
        Destroy(arrowGo.GetComponent<Collider>()); // must never intercept placement/pick raycasts
        arrowRend = arrowGo.GetComponent<Renderer>();
        arrowRend.sharedMaterial = MakeQuadMaterial(GetArrowTexture());
        arrowRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        arrowQuad = arrowGo.transform;
        arrowQuad.localScale = Vector3.one * arrowSize;
        arrowBlock = new MaterialPropertyBlock();

        var ringGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ringGo.name = "GroundRing";
        ringGo.transform.SetParent(transform, false);
        ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat on the surface
        Destroy(ringGo.GetComponent<Collider>());
        ringRend = ringGo.GetComponent<Renderer>();
        ringRend.sharedMaterial = MakeQuadMaterial(GetRingTexture());
        ringRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringQuad = ringGo.transform;
        ringBlock = new MaterialPropertyBlock();

        var cableGo = new GameObject("CableOutline");
        cableGo.transform.SetParent(transform, false);
        cableOutlineLine = cableGo.AddComponent<LineRenderer>();
        cableOutlineLine.useWorldSpace = true;
        cableOutlineLine.material = MakeQuadMaterial(null);
        cableOutlineLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cableOutlineLine.numCapVertices = 4;
        cableOutlineLine.numCornerVertices = 4;
        cableOutlineLine.gameObject.SetActive(false);

        gameObject.SetActive(false);
    }

    /// <summary>Sets this indicator's tint (arrow glow, ring glow, outline color) —
    /// call once after AddComponent, before the first Show call. Defaults to the
    /// same cyan DragDropManager/TableManager used for their old tint highlight.</summary>
    public void SetColor(Color c) => color = c;

    // ── Devices / tables (mesh-based) ────────────────────────────────────────

    /// <summary>Outlines every mesh renderer under root, floats the arrow above
    /// topAnchor, and — if groundCenter is given — drops a breathing ring sized
    /// to footprintXZ at the target's base. Pass groundCenter: null for a target
    /// that shouldn't get a ring (see class doc — currently only cables, via
    /// ShowOnCable instead of this method).</summary>
    public void ShowOnMesh(Transform root, Vector3 topAnchor, Vector3? groundCenter, Vector2 footprintXZ)
    {
        trackedCable = null;
        cableOutlineLine.gameObject.SetActive(false);
        draggingRoot = null;
        draggingRenderers = null;

        BuildMeshShells(root);

        arrowAnchor = topAnchor;

        ringActive = groundCenter.HasValue;
        ringQuad.gameObject.SetActive(ringActive);
        if (ringActive)
        {
            Vector3 gc = groundCenter.Value;
            ringQuad.position = new Vector3(gc.x, gc.y + ringYOffset, gc.z);
            ringDiameter = Mathf.Max(footprintXZ.x, footprintXZ.y) + ringPadding;
        }

        showTime = Time.time;
        gameObject.SetActive(true);
        active = true;
    }

    /// <summary>Same outline + arrow + ring as ShowOnMesh, but for a device/table
    /// that's actively being repositioned (held and following the cursor) rather
    /// than sitting still selected — the arrow/ring anchor is recomputed from
    /// root's live bounds every frame instead of a fixed point, and the arrow
    /// gets an extra alpha breathe (see dragFadeMinAlpha/dragFadeSpeed) so
    /// "you're moving this" reads as visibly different from "this is selected".
    /// The outline shells themselves need no such live resync — they're parented
    /// directly onto each renderer, so they already follow the drag for free.</summary>
    public void ShowDragging(Transform root)
    {
        trackedCable = null;
        cableOutlineLine.gameObject.SetActive(false);

        BuildMeshShells(root);
        draggingRoot = root;
        draggingRenderers = root.GetComponentsInChildren<Renderer>();

        ringActive = true;
        ringQuad.gameObject.SetActive(true);

        showTime = Time.time;
        gameObject.SetActive(true);
        active = true;
        SyncDraggingAnchor(); // avoid a one-frame pop at the wrong spot before the first LateUpdate
    }

    // Recomputes the arrow/ring anchor from draggingRoot's current combined
    // renderer bounds — called every LateUpdate while dragging, since (unlike
    // ShowOnMesh's static selection) the object actually moves every frame.
    void SyncDraggingAnchor()
    {
        if (draggingRoot == null || draggingRenderers == null || draggingRenderers.Length == 0) return;

        Bounds b = default;
        bool first = true;
        foreach (var r in draggingRenderers)
        {
            if (r == null) continue;
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        if (first) return; // every renderer was destroyed mid-drag — nothing to anchor to

        arrowAnchor = new Vector3(draggingRoot.position.x, b.max.y, draggingRoot.position.z);
        ringQuad.position = new Vector3(b.center.x, b.min.y + ringYOffset, b.center.z);
        ringDiameter = Mathf.Max(b.size.x, b.size.z) + ringPadding;
    }

    void BuildMeshShells(Transform root)
    {
        ClearMeshShells();

        var renderers = root.GetComponentsInChildren<MeshRenderer>();
        s_shellBlock ??= new MaterialPropertyBlock();
        Color tint = new Color(color.r * GlowMultiplier, color.g * GlowMultiplier, color.b * GlowMultiplier, 0.95f);
        s_shellBlock.Clear();
        s_shellBlock.SetColor("_Color", tint);
        s_shellBlock.SetFloat("_Thickness", thickness);

        foreach (var r in renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            var go = new GameObject("OutlineShell");
            go.transform.SetParent(r.transform, false); // local identity: exact overlap, follows this part live
            var shellMf = go.AddComponent<MeshFilter>();
            shellMf.sharedMesh = mf.sharedMesh;
            var shellR = go.AddComponent<MeshRenderer>();
            shellR.sharedMaterial = GetOutlineShellMaterial();
            shellR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shellR.SetPropertyBlock(s_shellBlock);

            shells.Add(go);
        }
    }

    void ClearMeshShells()
    {
        for (int i = 0; i < shells.Count; i++)
            if (shells[i] != null) Destroy(shells[i]);
        shells.Clear();
    }

    // ── Cable ─────────────────────────────────────────────────────────────────

    /// <summary>Outlines a cable's own live curve with a wider duplicate line and
    /// floats the arrow above its midpoint. No ground ring — a cable strung
    /// between two devices has no footprint to ring.</summary>
    public void ShowOnCable(Cable cable)
    {
        ClearMeshShells();
        trackedCable = cable;
        cableOutlineLine.gameObject.SetActive(true);
        draggingRoot = null;
        draggingRenderers = null;

        ringActive = false;
        ringQuad.gameObject.SetActive(false);

        showTime = Time.time;
        gameObject.SetActive(true);
        active = true;
        SyncCableOutline(); // avoid a one-frame pop at the wrong spot before the first LateUpdate
    }

    void SyncCableOutline()
    {
        if (trackedCable == null) return;
        var lr = trackedCable.GetComponent<LineRenderer>();
        if (lr == null || lr.positionCount < 2) return;

        cableOutlineLine.positionCount = lr.positionCount;
        for (int i = 0; i < lr.positionCount; i++)
            cableOutlineLine.SetPosition(i, lr.GetPosition(i));

        float w = Mathf.Max(lr.startWidth, lr.endWidth) * cableOutlineWidthMultiplier;
        cableOutlineLine.startWidth = w;
        cableOutlineLine.endWidth = w;

        arrowAnchor = trackedCable.SampleCurve(0.5f);

        Color c = new Color(color.r * GlowMultiplier, color.g * GlowMultiplier, color.b * GlowMultiplier, 0.85f);
        cableOutlineLine.startColor = c;
        cableOutlineLine.endColor = c;
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    public void Hide()
    {
        active = false;
        trackedCable = null;
        draggingRoot = null;
        draggingRenderers = null;
        ClearMeshShells();
        cableOutlineLine.gameObject.SetActive(false);
        ringQuad.gameObject.SetActive(false);
        gameObject.SetActive(false);
    }

    // LateUpdate, not Update — IsometricCamera moves the camera in its own
    // Update(), so billboarding here (after every script's Update() has
    // already run this frame) keeps the arrow from lagging a frame behind and
    // reading as jitter while the player pans/zooms (same reasoning as
    // WorldPulseMarker).
    void LateUpdate()
    {
        if (!active) return;

        // trackedCable != null also catches a "fake null" destroyed Cable
        // (Unity's overridden equality operator on UnityEngine.Object) —
        // e.g. CableRemoverManager deleting the hovered cable mid-frame —
        // so this simply stops re-syncing rather than throwing.
        if (trackedCable != null)
            SyncCableOutline();
        if (draggingRoot != null)
            SyncDraggingAnchor();

        float since = Time.time - showTime;
        float pop = Mathf.Clamp01(since / popDuration);
        pop = 1f - (1f - pop) * (1f - pop); // ease-out

        float t = Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f;
        float scale = arrowSize * Mathf.Lerp(minScale, maxScale, t) * pop;
        float bob = Mathf.Lerp(-bobHeight, bobHeight, t);

        // hoverHeight/bob describe where the arrow's TIP should sit, not the
        // quad's own center — same fix WorldPulseMarker's arrow needs (see
        // its LateUpdate comment), so lift the center by half the current
        // (pulsing) size to keep the tip pinned regardless of the breathing scale.
        arrowQuad.position = arrowAnchor + Vector3.up * (hoverHeight + bob + scale * 0.5f);
        arrowQuad.localScale = Vector3.one * scale;

        if (cam == null) cam = Camera.main;
        if (cam != null)
            arrowQuad.rotation = Quaternion.LookRotation(cam.transform.position - arrowQuad.position, Vector3.up);

        // Steady selection: alpha just ramps in with pop and then holds at 1 —
        // only the scale pulses. Dragging additionally breathes the alpha on
        // top of that (see dragFadeMinAlpha/dragFadeSpeed), on its own slower
        // sine so the fade reads as smooth rather than tied to the same beat
        // as the size pulse.
        float arrowAlpha = Mathf.Lerp(0.65f, 1f, pop);
        if (draggingRoot != null)
        {
            float fadeT = Mathf.Sin(Time.time * dragFadeSpeed) * 0.5f + 0.5f;
            arrowAlpha *= Mathf.Lerp(dragFadeMinAlpha, 1f, fadeT);
        }

        arrowBlock.Clear();
        arrowBlock.SetColor("_Color", new Color(color.r * GlowMultiplier, color.g * GlowMultiplier, color.b * GlowMultiplier, arrowAlpha));
        arrowRend.SetPropertyBlock(arrowBlock);

        if (ringActive)
        {
            ringQuad.localScale = Vector3.one * (ringDiameter * pop);
            float ringAlpha = Mathf.Lerp(ringMinAlpha, ringMaxAlpha, t) * pop;
            ringBlock.Clear();
            ringBlock.SetColor("_Color", new Color(color.r * GlowMultiplier * 0.8f, color.g * GlowMultiplier * 0.8f, color.b * GlowMultiplier * 0.8f, ringAlpha));
            ringRend.SetPropertyBlock(ringBlock);
        }
    }

    // ── Material / texture helpers ───────────────────────────────────────────

    static Material GetOutlineShellMaterial()
    {
        if (s_outlineShellMat != null) return s_outlineShellMat;
        Shader shader = Shader.Find("Custom/OutlineShell");
        s_outlineShellMat = new Material(shader);
        return s_outlineShellMat;
    }

    // Sprites/Default: simple, always-transparent-capable, double-sided, unlit
    // shader — same fallback WorldPulseMarker/CableManager already lean on for
    // quad/line materials.
    static Material MakeQuadMaterial(Texture2D tex)
    {
        Shader shader = Shader.Find("Sprites/Default");
        return new Material(shader) { mainTexture = tex };
    }

    static Texture2D GetArrowTexture()
    {
        if (s_arrowTexture != null) return s_arrowTexture;
        s_arrowTexture = BakeTexture("SelectionArrow", (u, v) =>
        {
            // Downward-pointing chevron: half-width tapers linearly from wide
            // at the top edge (v=1) to a point at the bottom (v=0, the tip
            // nearest the target), soft anti-aliased edge instead of a hard cut.
            float halfWidth = Mathf.Lerp(0.03f, 0.34f, v);
            float edgeDist = Mathf.Abs(u) - halfWidth;
            float alpha = Mathf.Clamp01(1f - edgeDist * 22f);
            alpha *= Mathf.Clamp01((1.05f - v) * 6f); // fades the top edge too, reads as a glyph not a hard triangle
            return alpha;
        });
        return s_arrowTexture;
    }

    static Texture2D GetRingTexture()
    {
        if (s_ringTexture != null) return s_ringTexture;
        s_ringTexture = BakeTexture("SelectionRing", (u, v) =>
        {
            float dist = Mathf.Sqrt(u * u + (v - 0.5f) * (v - 0.5f)) * 2f; // 0 at center, 1 at edge
            return Mathf.Clamp01(1f - Mathf.Abs(dist - 0.8f) * 4.5f); // soft-edged band, not a hairline
        });
        return s_ringTexture;
    }

    static Texture2D BakeTexture(string name, System.Func<float, float, float> alphaAt)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = y / (float)(size - 1);
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1) - 0.5f;
                float alpha = alphaAt(u, v);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
