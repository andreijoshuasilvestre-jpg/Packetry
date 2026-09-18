using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a packet's world-space visual: a small glowing orb (PacketTravelEffect
/// drives its elastic pop-in and comet-stretch, borrowed from PingPacketEffect's
/// own orb so a regular transfer reads with the same "traveling packet"
/// language a ping does) plus a billboarded type glyph hovering above it.
/// Mirrors DeviceModelBuilder's role (one static factory owning "what does a
/// Type look like"), but color is deliberately NOT part of a type's profile —
/// packets are tinted per-instance by their source device's subnet
/// (GameTheme.SubnetTint), the same convention DeviceAddress already uses to
/// tint devices, so color reads as "whose subnet is this" while the glyph
/// alone carries the packet's type. Movement/timing (PacketManager.Update)
/// only ever writes to the returned Transform's position — none of that
/// behaviour lives here or in PacketTravelEffect.
/// </summary>
public static class PacketVisual
{
    struct Profile
    {
        public float  Radius;    // orb diameter, same sizing convention as PingPacketEffect.OrbRadius
        public Sprite Icon;
        public float  Speed;     // world units / second
        public int    CapWeight; // cost against a cable's in-flight cap
    }

    static readonly Dictionary<PacketType, Profile> Profiles = new()
    {
        [PacketType.HttpRequest] = new Profile
        {
            Radius = 0.075f, Icon = GridGlobeIconBuilder.GetSprite(Color.white),
            Speed = 1.6f, CapWeight = 1,
        },
        [PacketType.HttpResponse] = new Profile
        {
            Radius = 0.075f, Icon = GridGlobeIconBuilder.GetSprite(Color.white),
            Speed = 1.6f, CapWeight = 1,
        },
        [PacketType.FileTransfer] = new Profile
        {
            Radius = 0.10f, Icon = FolderIconBuilder.GetSprite(Color.white),
            Speed = 0.8f, CapWeight = 2,
        },
    };

    // Fixed per-type accent used only by the departure/arrival wipe + landing
    // icon (PacketManager) — deliberately separate from the subnet Tint above,
    // which stays on the traveling body/icon so color there still reads as
    // "whose subnet is this". Http reuses the project's existing blue accent
    // (GameTheme.ElectricBlue); FileTransfer gets a matching yellow-orange.
    static readonly Dictionary<PacketType, Color> TypeColors = new()
    {
        [PacketType.HttpRequest]   = GameTheme.ElectricBlue,
        [PacketType.HttpResponse]  = GameTheme.ElectricBlue,
        [PacketType.FileTransfer]  = new Color(1f, 0.62f, 0.12f),
    };

    const float GlowBoost = 2.2f; // toned down slightly from PingPacketEffect.OrbGlowBoost (2.6) — these fire far more often than a one-shot ping

    static Material s_bodyMaterial;
    static MaterialPropertyBlock s_tintBlock;
    static readonly Dictionary<Texture, Material> s_iconMaterials = new();

    public static float SpeedFor(PacketType type)     => Profiles[type].Speed;
    public static int   CapWeightFor(PacketType type) => Profiles[type].CapWeight;
    public static Sprite IconFor(PacketType type)      => Profiles[type].Icon;
    public static Color ColorFor(PacketType type)      => TypeColors[type];

    public static Transform Create(PacketType type, Color tint, Transform parent)
    {
        Profile profile = Profiles[type];

        var root = new GameObject($"Packet_{type}");
        root.transform.SetParent(parent, false);

        var bodyGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bodyGo.name = "Body";
        bodyGo.transform.SetParent(root.transform, false);
        Object.Destroy(bodyGo.GetComponent<Collider>());

        var bodyRenderer = bodyGo.GetComponent<Renderer>();
        s_bodyMaterial ??= MakeUnlitMaterial();
        bodyRenderer.sharedMaterial = s_bodyMaterial;

        s_tintBlock ??= new MaterialPropertyBlock();
        s_tintBlock.Clear();
        string prop = s_bodyMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
        s_tintBlock.SetColor(prop, tint * GlowBoost);
        bodyRenderer.SetPropertyBlock(s_tintBlock);

        BuildIcon(profile.Icon, tint, root.transform);

        var travel = root.AddComponent<PacketTravelEffect>();
        travel.Init(bodyGo.transform, profile.Radius, profile.Speed);

        return root.transform;
    }

    static void BuildIcon(Sprite iconSprite, Color tint, Transform parent)
    {
        var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadGo.name = "Icon";
        quadGo.transform.SetParent(parent, false);
        quadGo.transform.localPosition = new Vector3(0f, 0.14f, 0f);
        quadGo.transform.localScale    = Vector3.one * 0.09f;
        Object.Destroy(quadGo.GetComponent<Collider>());

        var renderer = quadGo.GetComponent<Renderer>();
        renderer.sharedMaterial = GetIconMaterial(iconSprite.texture);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", tint * GlowBoost);
        renderer.SetPropertyBlock(block);

        quadGo.AddComponent<BillboardLabel>();
    }

    static Material GetIconMaterial(Texture tex)
    {
        if (s_iconMaterials.TryGetValue(tex, out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find("Sprites/Default");
        var material = new Material(shader) { mainTexture = tex };
        s_iconMaterials[tex] = material;
        return material;
    }

    static Material MakeUnlitMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        return new Material(shader);
    }
}
