using UnityEngine;

/// <summary>
/// Floating green "ping" badge over a PC the ping tool has picked — the ping
/// tool's counterpart to PacketAssignBadge's up/down arrows, drawn as the same
/// sonar symbol a PC's monitor shows for a ping (PCMonitor's Ping glyph): a
/// center dot, two faint rings, and one bright ring that sweeps outward on the
/// source (sending) or closes inward on the destination (receiving).
/// PingToolManager shows it on the source as soon as it's picked and on the
/// destination once the ping fires, and removes both as the result lands
/// (where PingCheckmarkEffect's tick/cross pops in at the same height).
///
/// Built like PacketAssignBadge — a billboarded quad, tinted via
/// MaterialPropertyBlock, bobbing gently above the device — but the rings are
/// animated by cycling a few pre-baked pixel-art frames rather than one
/// static texture.
/// </summary>
public class PingSelectBadge : MonoBehaviour
{
    const float HoverHeight  = 0.55f; // matches PacketAssignBadge / PingCheckmarkEffect
    const float Size         = 0.34f; // world-space quad diameter
    const float BobAmplitude = 0.08f;
    const float BobSpeed     = 2.6f;
    const float GlowMultiplier = HologramMaterial.GlowMultiplier;

    // Frame baking — logical pixel grid, upscaled blocky with point
    // filtering like CheckmarkIconBuilder, so it reads as the same pixel art
    // as the monitor's own ping symbol.
    const int   LogicalSize = 17;
    public const int PixelScale = 8;

    /// <summary>World size of one logical pixel of this badge's art — what
    /// PingCheckmarkEffect sizes its tick/cross grid against, so the result
    /// that replaces this badge is the same pixel art, not a chunkier one.</summary>
    public const float CellWorldSize = Size / LogicalSize;
    const int   FrameCount  = 10;
    const float CycleSeconds = 0.9f; // one full ring sweep

    static Texture2D[] s_frames;

    NetworkDevice device;
    bool outgoing;
    Transform quad;
    Renderer rend;
    Material material;
    MaterialPropertyBlock block;
    Camera cam;
    int shownFrame = -1;

    /// <param name="outgoing">True on the ping's source (rings sweep outward),
    /// false on its destination (rings close inward).</param>
    public static PingSelectBadge Attach(NetworkDevice device, bool outgoing)
    {
        if (device == null) return null;
        var badge = new GameObject(outgoing ? "PingSourceBadge" : "PingTargetBadge").AddComponent<PingSelectBadge>();
        badge.Init(device, outgoing);
        return badge;
    }

    /// <summary>Tears the badge down. Safe to call more than once, or on null.</summary>
    public static void Remove(PingSelectBadge badge)
    {
        if (badge != null) Destroy(badge.gameObject);
    }

    void Init(NetworkDevice targetDevice, bool isOutgoing)
    {
        device   = targetDevice;
        outgoing = isOutgoing;
        cam      = Camera.main;

        var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadGo.name = "Icon";
        quadGo.transform.SetParent(transform, false);
        Destroy(quadGo.GetComponent<Collider>()); // must never intercept ping/placement raycasts

        rend = quadGo.GetComponent<Renderer>();
        material = new Material(Shader.Find("Sprites/Default"));
        rend.sharedMaterial = material;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        quad = quadGo.transform;
        quad.localScale = Vector3.one * Size;
        block = new MaterialPropertyBlock();

        Color c = GameTheme.Success;
        block.SetColor("_Color", new Color(c.r * GlowMultiplier, c.g * GlowMultiplier, c.b * GlowMultiplier, 1f));
        rend.SetPropertyBlock(block);
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
    }

    void LateUpdate()
    {
        // Same safety net as PacketAssignBadge — never hang a headless badge in
        // the air if its device is removed mid-ping.
        if (device == null) { Destroy(gameObject); return; }
        if (cam == null) cam = Camera.main;

        float bob = Mathf.Sin(Time.time * BobSpeed) * BobAmplitude;
        quad.position = device.VisualTopPoint() + Vector3.up * (HoverHeight + bob);
        if (cam != null)
            quad.rotation = Quaternion.LookRotation(quad.position - cam.transform.position, Vector3.up);

        Texture2D[] frames = GetFrames();
        int frame = Mathf.FloorToInt(Mathf.Repeat(Time.time / CycleSeconds, 1f) * FrameCount) % FrameCount;
        if (!outgoing) frame = FrameCount - 1 - frame; // play the sweep backwards: rings close in
        if (frame != shownFrame)
        {
            shownFrame = frame;
            material.mainTexture = frames[frame];
        }
    }

    static Texture2D[] GetFrames()
    {
        if (s_frames != null && s_frames[0] != null) return s_frames;
        s_frames = new Texture2D[FrameCount];
        for (int i = 0; i < FrameCount; i++) s_frames[i] = BakeFrame(i / (float)FrameCount);
        return s_frames;
    }

    // One step of the sweep: center dot, two faint static rings, and a bright
    // ring at `phase` (0 = at the center, 1 = at the edge) fading as it grows.
    static Texture2D BakeFrame(float phase)
    {
        int size = LogicalSize * PixelScale;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "PingBadgeFrame",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        float center = LogicalSize * 0.5f;
        float sweepR = Mathf.Lerp(1.8f, 8f, phase);
        float sweepA = 1f - phase * 0.8f;

        var pixels = new Color32[size * size];
        for (int ly = 0; ly < LogicalSize; ly++)
            for (int lx = 0; lx < LogicalSize; lx++)
            {
                float dx = lx + 0.5f - center, dy = ly + 0.5f - center;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);

                float a = 0f;
                if (d <= 1.6f) a = 1f;                                  // center dot
                if (Mathf.Abs(d - 4.5f) <= 0.5f) a = Mathf.Max(a, 0.45f); // static rings
                if (Mathf.Abs(d - 7.5f) <= 0.5f) a = Mathf.Max(a, 0.45f);
                if (Mathf.Abs(d - sweepR) <= 0.6f) a = Mathf.Max(a, sweepA);

                var px = new Color32(255, 255, 255, (byte)(a * 255f));
                for (int sy = 0; sy < PixelScale; sy++)
                    for (int sx = 0; sx < PixelScale; sx++)
                        pixels[(ly * PixelScale + sy) * size + lx * PixelScale + sx] = px;
            }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
