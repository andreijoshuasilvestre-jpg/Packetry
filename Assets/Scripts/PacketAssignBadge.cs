using UnityEngine;

/// <summary>
/// Persistent world-space up-arrow (sender) or down-arrow (receiver) badge
/// hovering above a device while it's part of Packet Assign Mode's
/// sender/receiver board (see PacketSendManager) — stays until Remove() is
/// called, unlike PingCheckmarkEffect's one-shot pop-and-fade, since this is
/// tracking standing assignment state rather than a momentary event. Built
/// the same way WorldPulseMarker's own arrow is: a billboarded quad sampling
/// a runtime-baked alpha texture, tinted via MaterialPropertyBlock — just
/// persistent, with a gentle idle bob instead of a spawn/pulse cycle.
/// </summary>
public class PacketAssignBadge : MonoBehaviour
{
    const float HoverHeight  = 0.55f; // clearance above the device's own visual top point
    const float Size         = 0.30f; // world-space quad diameter
    const float BobAmplitude = 0.08f; // a clearly visible up-and-down, same as PingSelectBadge
    const float BobSpeed     = 2.6f;
    const float GlowMultiplier = HologramMaterial.GlowMultiplier; // same bloom calibration as every other glowing effect in this project

    static readonly Color SenderColor   = GameTheme.Success; // up-arrow: this device is sending out
    static readonly Color ReceiverColor = GameTheme.Info;    // down-arrow: this device is receiving

    static Texture2D s_upTexture;
    static Texture2D s_downTexture;

    NetworkDevice device;
    Transform quad;
    Renderer rend;
    MaterialPropertyBlock block;
    Color tint;
    Camera cam;

    public static PacketAssignBadge Attach(NetworkDevice device, bool isSender)
    {
        if (device == null) return null;
        var go = new GameObject(isSender ? "SenderBadge" : "ReceiverBadge");
        var badge = go.AddComponent<PacketAssignBadge>();
        badge.Init(device, isSender);
        return badge;
    }

    /// <summary>Tears the badge down. Safe to call more than once.</summary>
    public void Remove()
    {
        if (this != null && gameObject != null) Destroy(gameObject);
    }

    void Init(NetworkDevice targetDevice, bool isSender)
    {
        device = targetDevice;
        cam    = Camera.main;
        tint   = isSender ? SenderColor : ReceiverColor;

        var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadGo.name = "Icon";
        quadGo.transform.SetParent(transform, false);
        Destroy(quadGo.GetComponent<Collider>()); // must never intercept placement/send raycasts

        rend = quadGo.GetComponent<Renderer>();
        Shader shader = Shader.Find("Sprites/Default");
        rend.sharedMaterial = new Material(shader) { mainTexture = isSender ? GetUpTexture() : GetDownTexture() };
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        quad = quadGo.transform;
        quad.localScale = Vector3.one * Size;
        block = new MaterialPropertyBlock();
    }

    void OnDestroy()
    {
        if (rend != null && rend.sharedMaterial != null) Destroy(rend.sharedMaterial);
    }

    void LateUpdate()
    {
        // The device this badge belongs to can vanish out from under it
        // (deleted via Remove Device, or a level reload) even if
        // PacketSendManager's own cleanup misses a path — self-destroy rather
        // than hang a headless badge in the air.
        if (device == null) { Destroy(gameObject); return; }

        if (cam == null) cam = Camera.main;

        float bob = Mathf.Sin(Time.time * BobSpeed) * BobAmplitude;
        quad.position = device.VisualTopPoint() + Vector3.up * (HoverHeight + bob);

        if (cam != null)
            quad.rotation = Quaternion.LookRotation(quad.position - cam.transform.position, Vector3.up);

        block.SetColor("_Color", new Color(tint.r * GlowMultiplier, tint.g * GlowMultiplier, tint.b * GlowMultiplier, 1f));
        rend.SetPropertyBlock(block);
    }

    static Texture2D GetUpTexture()
    {
        if (s_upTexture != null) return s_upTexture;
        s_upTexture = BakeArrow("PacketBadgeUp", pointsUp: true);
        return s_upTexture;
    }

    static Texture2D GetDownTexture()
    {
        if (s_downTexture != null) return s_downTexture;
        s_downTexture = BakeArrow("PacketBadgeDown", pointsUp: false);
        return s_downTexture;
    }

    // Pixel-art arrow (head + stem), drawn pointing up; flipped for down.
    // Blocky and point-filtered like every other icon in the game (the
    // checkmark/cross/globe/folder builders, PingSelectBadge, the PC
    // monitors) rather than the soft-edged smooth arrow this used to be.
    static readonly string[] ArrowBitmap =
    {
        "............",
        ".....##.....",
        "....####....",
        "...######...",
        "..########..",
        ".##########.",
        "############",
        "....####....",
        "....####....",
        "....####....",
        "....####....",
        "............",
    };
    const int ArrowPixelScale = 6;

    static Texture2D BakeArrow(string name, bool pointsUp)
    {
        int logical = ArrowBitmap.Length;
        int size = logical * ArrowPixelScale;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            // Texture rows run bottom-up; the bitmap is written top-down.
            int ly = y / ArrowPixelScale;
            int row = pointsUp ? logical - 1 - ly : ly;
            for (int x = 0; x < size; x++)
            {
                bool on = ArrowBitmap[row][x / ArrowPixelScale] == '#';
                pixels[y * size + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
