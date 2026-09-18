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

    static MaterialPropertyBlock s_tintBlock;

    // Only the PC's own case gets recolored by its subnet — the monitor
    // (bezel/screen/stand), keyboard, and port keep their own fixed material
    // color regardless of address, so a PC still reads as "a PC" at a glance
    // and the tint reads as an accent rather than a full repaint. Matches the
    // GameObject name DeviceModelBuilder/DevicePrefabBuilder give the tower
    // part on both the runtime-fallback and prefab-baked build paths.
    const string TintTargetPartName = "PC_Tower";

    // Blend weight for the subnet tint — kept low so it reads as a subtle
    // accent on the case rather than a loud color swap (was 0.7 back when the
    // whole model was tinted; scoping down to just the case alone already
    // reads stronger, so this also came down).
    const float TintStrength = 0.42f;

    // Re-applies the tint on load too — SetPropertyBlock is a runtime-only
    // renderer state, not something Unity's scene serializer restores, so a
    // template-loaded device needs this to actually show its baked address's
    // color instead of just carrying the (correct, once this fix lands) data.
    void Awake() => ApplyTint();

    public void SetAddress(string newIp, string newMask)
    {
        ip   = newIp;
        mask = newMask;
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
            ? GameTheme.SubnetTint(SubnetMath.NetworkAddress(Ip, Mask))
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

            string prop = r.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            Color orig = r.sharedMaterial.GetColor(prop);
            s_tintBlock.Clear();
            s_tintBlock.SetColor(prop, Color.Lerp(orig, tint, TintStrength));
            r.SetPropertyBlock(s_tintBlock);
        }
    }
}
