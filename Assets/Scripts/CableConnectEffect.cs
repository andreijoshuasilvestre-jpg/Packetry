using UnityEngine;

/// <summary>
/// One-shot "this device just got wired up" feedback, played on each end of a
/// new cable: a slight glow + squash-stretch bounce (reuses PacketDeviceEffect,
/// the same wobble used for packet touches and placement landings) plus a
/// small burst of electric sparks (reuses ConstructionSparks, the same look
/// used while a cable/blueprint is being drawn). The spark burst's spawn
/// radius is measured from the device's own renderer bounds — see
/// ConstructionRevealEffect.Run's identical bounds.extents.magnitude trick —
/// so a PC's sparks sit close around it while a Router's spread further out,
/// instead of every device getting the same fixed-size poof.
/// </summary>
public static class CableConnectEffect
{
    // Soft electric cyan-blue — distinct from ConstructionRevealEffect's pure
    // blueprint blue so a "just connected" pop doesn't read as a re-trigger of
    // the spawn-in reveal, while staying in the same "electric" family.
    static readonly Color GlowTint = new Color(0.35f, 0.8f, 1f, 1f);
    static readonly Color SparkColor = new Color(0.4f, 0.85f, 1f, 1f);

    // Milder than PacketDeviceEffect.PlacementIntensity (2.4) — this should
    // read as a quick, slight pop, not the bigger "just landed" bounce.
    const float BounceIntensity = 1.5f;
    const int SparkCount = 20; // fewer than ConstructionRevealEffect's 48 completion burst — a slight sparkle, not a full finish

    public static void Play(NetworkDevice device)
    {
        if (device == null) return;

        PacketDeviceEffect.Play(device, GlowTint, BounceIntensity);

        var renderers = device.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        // Shell radius matches the device's own half-diagonal so sparks read
        // as bursting from around/outside its actual surface regardless of
        // device type/size, same reasoning as ConstructionRevealEffect.Run.
        ConstructionSparks.EmitBurst(bounds.center, SparkColor, count: SparkCount, spawnRadius: bounds.extents.magnitude);
    }
}
