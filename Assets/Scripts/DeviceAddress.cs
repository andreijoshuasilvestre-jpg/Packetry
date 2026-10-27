using UnityEngine;

/// <summary>
/// Holds a PC's configured ip/mask (set via DeviceAddressPopup) and reflects
/// it onto the device model as a subnet-colored tint — same subnet, same hue,
/// so grouping is visible before the player ever runs Ping.
/// </summary>
public class DeviceAddress : MonoBehaviour
{
    // Backing fields, not auto-properties — a level-template scene (see
    // LevelTemplateAuthoringWindow) bakes this component with whatever address
    // the player set live, but only [SerializeField] fields survive that
    // save/load round-trip; an auto-property's compiler-generated backing
    // field doesn't, so Ip/Mask would silently come back null on every
    // template-loaded device (same failure mode as Table.finalCenter — see
    // its own doc comment).
    [SerializeField] private string ip;
    [SerializeField] private string mask;
    public string Ip   => ip;
    public string Mask => mask;

    public bool HasAddress => SubnetMath.IsDottedQuad(Ip) && SubnetMath.IsDottedQuad(Mask);

    // When this device joined its current subnet (DateTime ticks). 0 = unknown
    // (template-baked, or a save from before this existed). It no longer
    // decides any color — SubnetVisualizer now votes on what's configured
    // rather than on the order it was typed — but it's still saved/restored so
    // saves round-trip unchanged, and Level2Guide reads it to find which PC
    // was addressed last on resume.
    [SerializeField] private long addressedAtTicks;
    public long AddressedAtTicks => addressedAtTicks;

    // Per-PC receive capability, configured in DeviceAddressPopup — default
    // both on so an un-touched PC behaves exactly like before this field
    // existed. Independent of each other on purpose (see the packet-send
    // redesign memory: mutually-exclusive send/receive PC modes were
    // considered and rejected as unrealistic and confusing).
    [SerializeField] private bool acceptHttpRequest = true;
    [SerializeField] private bool acceptFileTransfer = true;
    public bool AcceptsHttpRequest  => acceptHttpRequest;
    public bool AcceptsFileTransfer => acceptFileTransfer;

    public bool Accepts(PacketType type) => type switch
    {
        PacketType.FileTransfer => acceptFileTransfer,
        // HttpResponse always accepted — it's the reactive reply to a request
        // this same device (or its peer) already chose to send, never a
        // player-initiated send a receive-flag should be able to block.
        _ => acceptHttpRequest || type == PacketType.HttpResponse,
    };

    public void SetAccepts(bool http, bool file)
    {
        acceptHttpRequest  = http;
        acceptFileTransfer = file;
    }

    static MaterialPropertyBlock s_tintBlock;

    // Only the PC's own case gets recolored by its subnet — the monitor
    // (bezel/screen/stand), keyboard, and port keep their own fixed material
    // color regardless of address, so a PC still reads as "a PC" at a glance
    // and the tint reads as an accent rather than a full repaint. Matches the
    // GameObject name DeviceModelBuilder/DevicePrefabBuilder give the tower
    // part on both the runtime-fallback and prefab-baked build paths.
    const string TintTargetPartName = "PC_Tower";

    // Blend weight for the subnet tint. PC_Tower's own base color is very
    // dark (~0.1) — the original assumption (see old comment below) was that
    // scoping the tint down to just the case alone would read stronger even
    // at a low blend weight, but a low lerp from a near-black base just stays
    // dark (e.g. 0.42 toward red only reaches ~(0.42, 0.14, 0.14), easy to
    // mistake for "not tinted at all" next to true black) — confirmed live:
    // Switch_Body's own tint (same 0.42, see SubnetVisualizer) reads clearly
    // because its base is a mid-grey, not because scoping helped. Bumped up
    // so a dark base still lands somewhere clearly colored.
    const float TintStrength = 0.65f;

    // Re-applies the tint on load too — SetPropertyBlock is a runtime-only
    // renderer state, not something Unity's scene serializer restores, so a
    // template-loaded device needs this to actually show its baked address's
    // color instead of just carrying the (correct, once this fix lands) data.
    void Awake() => ApplyTint();

    /// <param name="addressedAt">Restores a saved AddressedAtTicks (save load,
    /// undo). Omitted = a player action: stamped now, unless the device stays on
    /// the same subnet — only moving to a different subnet counts as joining it
    /// later, so re-confirming or changing just the host part keeps its place.</param>
    public void SetAddress(string newIp, string newMask, long? addressedAt = null)
    {
        if (addressedAt.HasValue)
            addressedAtTicks = addressedAt.Value;
        else if (!SubnetMath.SameSubnet(ip, mask, newIp, newMask))
            addressedAtTicks = System.DateTime.UtcNow.Ticks;

        ip   = newIp;
        mask = newMask;
        // RefreshAll first — it frees the old subnet's color slot (if no other
        // device is still on it) so the new subnet can take that slot back,
        // rather than moving on to the next color.
        SubnetVisualizer.RefreshAll();
        ApplyTint();
    }

    /// <summary>
    /// Re-paints the subnet tint. Public because DragDropManager/CableManager's
    /// selection/cable highlights also drive this device's renderers via
    /// SetPropertyBlock — when they turn their own highlight off they call this
    /// instead of clearing to null, so a tinted device doesn't revert to its
    /// plain material color the moment it's deselected.
    /// </summary>
    public void ApplyTint()
    {
        s_tintBlock ??= new MaterialPropertyBlock();
        Renderer[] all = GetComponentsInChildren<Renderer>();

        // Devices with no part named TintTargetPartName (Switch/Router if one
        // is ever given an address, or an older save's PC missing the part)
        // fall back to tinting every renderer — this component's original
        // whole-model behavior — rather than silently tinting nothing.
        bool hasNamedTarget = false;
        foreach (var r in all)
            if (r.gameObject.name == TintTargetPartName) { hasNamedTarget = true; break; }

        Color tint = HasAddress
            ? GameTheme.SubnetTint(SubnetMath.SubnetOf(Ip, Mask))
            : default;

        foreach (var r in all)
        {
            bool isTarget = !hasNamedTarget || r.gameObject.name == TintTargetPartName;

            // Clears the monitor/keyboard/port back to their own material color
            // even when the device has an address — needed because DragDropManager's
            // selection highlight paints every renderer (see ApplyHighlightTint),
            // then calls back into this method to restore the "real" look; without
            // this, deselecting a tinted PC would leave those parts stuck highlighted.
            if (!HasAddress || !isTarget) { r.SetPropertyBlock(null); continue; }

            // Skip a material with neither property (e.g. DeviceRimLight's
            // additive Custom/RimGlow shells, which only expose _RimColor)
            // instead of blindly assuming "_Color" exists — only reachable in
            // practice on a hasNamedTarget=false device (Switch/Router, if
            // ever given a DeviceAddress), since a PC's own isTarget check
            // above already narrows to just its "PC_Tower" part.
            if (r.sharedMaterial == null) continue; // see PacketSendManager.HighlightDevice
            bool hasBase = r.sharedMaterial.HasProperty("_BaseColor");
            if (!hasBase && !r.sharedMaterial.HasProperty("_Color")) continue;
            string prop = hasBase ? "_BaseColor" : "_Color";
            Color orig = r.sharedMaterial.GetColor(prop);
            s_tintBlock.Clear();
            s_tintBlock.SetColor(prop, Color.Lerp(orig, tint, TintStrength));
            r.SetPropertyBlock(s_tintBlock);
        }
    }
}
