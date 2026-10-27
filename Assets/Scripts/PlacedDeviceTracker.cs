using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The PCs/Switches/Routers a challenge level's objectives are checked
/// against: whatever was already on the field when the level's objectives
/// started (a resumed save, a template's starting network), plus every
/// device the player drops in afterward, minus every device removed along
/// the way.
///
/// Plain C# class, not a MonoBehaviour — same Attach/Detach lifetime as
/// PacketCoverageTracker. Replaces each level keeping its own add-only
/// lists: a removed device used to linger in them as a destroyed reference,
/// so a check that treated one as a failure ("every PC is cabled" in
/// Level8Challenge/Level10Challenge) could never pass again, and
/// Level7Challenge's cabled-count check could never catch up once a PC was
/// deleted and replaced. A removal is dropped here synchronously
/// (DeviceRemoverManager.OnDeviceRemoved fires before the Destroy), and a
/// device an Undo brings back rejoins through UndoManager.OnDeviceRestored,
/// since it never goes through DragDropManager.OnDeviceDropped.
/// </summary>
public class PlacedDeviceTracker
{
    readonly List<NetworkDevice> pcs = new();
    readonly List<NetworkDevice> switches = new();
    readonly List<NetworkDevice> routers = new();
    bool attached;

    // Safety net for a device destroyed by some path that never raised
    // OnDeviceRemoved — pruned on every read, so no caller ever sees one.
    static readonly Predicate<NetworkDevice> IsGone = d => d == null;

    public IReadOnlyList<NetworkDevice> PCs      { get { pcs.RemoveAll(IsGone);      return pcs; } }
    public IReadOnlyList<NetworkDevice> Switches { get { switches.RemoveAll(IsGone); return switches; } }

    // The earliest-placed one still on the field — removing it hands over to
    // the next one rather than leaving the level tracking nothing.
    public NetworkDevice FirstSwitch => Switches.Count > 0 ? switches[0] : null;
    public NetworkDevice FirstRouter { get { routers.RemoveAll(IsGone); return routers.Count > 0 ? routers[0] : null; } }

    /// <summary>Seeds from every device already in the scene and starts
    /// following placements, removals and undo restores.</summary>
    public void Attach()
    {
        Detach();
        pcs.Clear();
        switches.Clear();
        routers.Clear();

        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
            if (!IsUndroppedGhost(device)) Add(device);

        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped += HandleDeviceDropped;
        if (DeviceRemoverManager.Instance != null) DeviceRemoverManager.Instance.OnDeviceRemoved += Remove;
        if (UndoManager.Instance != null) UndoManager.Instance.OnDeviceRestored += Add;
        attached = true;
    }

    public void Detach()
    {
        if (!attached) return;
        attached = false;

        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped -= HandleDeviceDropped;
        if (DeviceRemoverManager.Instance != null) DeviceRemoverManager.Instance.OnDeviceRemoved -= Remove;
        if (UndoManager.Instance != null) UndoManager.Instance.OnDeviceRestored -= Add;
    }

    // A fresh palette ghost still under the cursor isn't on the field yet —
    // it joins through OnDeviceDropped if it's actually dropped, and a
    // cancelled one is destroyed without any removal event, so seeding it
    // would leave it stuck in a list. A placed device picked up to be
    // repositioned is still placed, so only the fresh-ghost case is skipped.
    static bool IsUndroppedGhost(NetworkDevice device)
    {
        var dd = DragDropManager.Instance;
        return dd != null && dd.IsHolding(device.gameObject) && !dd.IsRepositioning;
    }

    void HandleDeviceDropped(NetworkDevice device, bool isNewDevice)
    {
        if (isNewDevice) Add(device);
    }

    void Add(NetworkDevice device)
    {
        if (device == null || device.isPaletteItem) return;
        var list = ListFor(device.deviceType);
        if (list != null && !list.Contains(device)) list.Add(device);
    }

    void Remove(NetworkDevice device)
    {
        if (device == null) return;
        ListFor(device.deviceType)?.Remove(device);
    }

    List<NetworkDevice> ListFor(DeviceType type) => type switch
    {
        DeviceType.PC     => pcs,
        DeviceType.Switch => switches,
        DeviceType.Router => routers,
        _                 => null,
    };
}
