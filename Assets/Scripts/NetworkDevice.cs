using UnityEngine;
using System.Collections.Generic;

public enum DeviceType { PC, Switch, Router }

public class NetworkDevice : MonoBehaviour
{
    [System.Serializable]
    public class Port
    {
        public Vector3 localOffset;
        public NetworkDevice connectedDevice;
        public Cable cable;
        // True when this interface is administratively up with an IP assigned
        // (driven by RouterCLI). Drives whether an attached cable shows packet flow.
        public bool active;
        // The port's own small visual part (see DeviceModelBuilder — the port
        // cubes now use their own s_portMat instead of blending into the rest
        // of the chassis), so HighlightFreePorts can tint just this one part
        // instead of the whole device.
        public GameObject visualGo;
    }

    [SerializeField] public DeviceType deviceType;
    [SerializeField] public string deviceName;

    // Stable cross-session identity for the save system — SaveManager/
    // SceneStateCapture use this (not the Unity instance, which doesn't
    // survive a scene reload) as the FK cables/topology rows reference.
    // Lazily assigned on first read rather than in Awake so a device cloned
    // via Instantiate (see DragDropManager) doesn't inherit its template's ID.
    [SerializeField] private string saveId;
    public string SaveId
    {
        get
        {
            if (string.IsNullOrEmpty(saveId)) saveId = System.Guid.NewGuid().ToString();
            return saveId;
        }
        set => saveId = value; // used by SceneStateCapture.Restore to re-apply a saved ID
    }

    public bool isPaletteItem = false;

    // When true, this device may only be dropped inside a Table's footprint
    // (checked in DragDropManager.Drop()). Set per device type in DeviceModelBuilder.
    public bool requiresTable = false;

    // Read-only view of every live device — mirrors Table.AllTables. Cable uses
    // this to route around/over a device's own body instead of clipping through
    // it (see Cable.AvoidDevices). Palette-shelf items are filtered out by the
    // reader (isPaletteItem can still be true/false-flipping at the moment this
    // fires during Instantiate — see DragDropManager — so checking it live at
    // query time is safer than baking it in here).
    private static readonly List<NetworkDevice> s_all = new List<NetworkDevice>();
    public static IReadOnlyList<NetworkDevice> AllDevices => s_all;

    void OnEnable()  => s_all.Add(this);
    void OnDisable() => s_all.Remove(this);

    // The root BoxCollider DeviceModelBuilder adds to every device (PC/Switch/
    // Router) already matches its visual footprint exactly, so this reuses it
    // rather than duplicating width/height/length as separate fields the way
    // Table does. Deliberately NOT BoxCollider.bounds — PhysX only re-syncs a
    // kinematic Rigidbody's collider a physics step after transform.position
    // changes, so .bounds reads one frame stale while DragDropManager is
    // actively moving a device (this is presumably also why Table computes
    // ContainsXZ from its own transform/fields rather than a collider). Instead
    // transform the local box's corners through this frame's actual transform,
    // which is always exactly in sync. Encapsulating all 8 corners (not just
    // center +/- extents) keeps this correct if a device is ever rotated.
    private BoxCollider _footprintCollider;
    public Bounds? FootprintBounds()
    {
        _footprintCollider ??= GetComponent<BoxCollider>();
        if (_footprintCollider == null) return null;

        Vector3 c = _footprintCollider.center;
        Vector3 e = _footprintCollider.size * 0.5f;
        Bounds b = new Bounds(transform.TransformPoint(c), Vector3.zero);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    b.Encapsulate(transform.TransformPoint(c + Vector3.Scale(e, new Vector3(sx, sy, sz))));
        return b;
    }

    // [SerializeField] so Unity's Instantiate() (used to clone the placed-device
    // template in DragDropManager) actually copies port state to the clone —
    // a plain, non-serialized field would silently reset to empty on every clone.
    [SerializeField] private Port[] ports = new Port[0];
    public int PortCount => ports.Length;

    // Child transform holding every visual part (DeviceModelBuilder parents the
    // model's cubes/cylinders under here, not directly under this transform),
    // left as identity so it has zero effect until something animates it. Lets
    // PacketDeviceEffect squash/stretch/bounce the model without moving this
    // transform itself — the collider, Rigidbody, and port offsets (GetPortWorldPoint
    // reads this transform, not visualRoot) all stay put, so cables never jitter.
    [SerializeField] public Transform visualRoot;

    // Where the whole device's port cluster faces, in visualRoot-local space —
    // set once by DeviceModelBuilder alongside InitPorts(). Used only to draw
    // the single port-side direction arrow on the drag hologram (see
    // DragDropManager.ApplyHologram); not consulted for cable connection logic.
    public Vector3 PortSideLocalOffset { get; private set; }
    public Vector3 PortSideLocalDirection { get; private set; } = Vector3.forward;

    public void SetPortSideIndicator(Vector3 localOffset, Vector3 localDirection)
    {
        PortSideLocalOffset = localOffset;
        PortSideLocalDirection = localDirection;
    }

    // Sets the device's fixed set of ports from local-space attach offsets
    // (the same offsets used to draw the port-slot decorations).
    public void InitPorts(Vector3[] localOffsets)
    {
        ports = new Port[localOffsets.Length];
        for (int i = 0; i < localOffsets.Length; i++)
            ports[i] = new Port { localOffset = localOffsets[i] };
    }

    public Vector3 GetPortWorldPoint(int index) => transform.TransformPoint(ports[index].localOffset);

    // Called once per port right after InitPorts by DeviceModelBuilder, with
    // the small GameObject that port's own cube part built as — see Port.visualGo.
    public void SetPortVisual(int index, GameObject go) => ports[index].visualGo = go;

    public List<int> GetFreePorts()
    {
        var free = new List<int>();
        for (int i = 0; i < ports.Length; i++)
            if (ports[i].connectedDevice == null) free.Add(i);
        return free;
    }

    public void ConnectPort(int index, NetworkDevice other, Cable cable)
    {
        ports[index].connectedDevice = other;
        ports[index].cable = cable;
    }

    public void DisconnectPort(int index)
    {
        ports[index].connectedDevice = null;
        ports[index].cable = null;
    }

    public Cable GetPortCable(int index) => ports[index].cable;

    /// <summary>Reverse lookup of GetPortCable — which of this device's own ports a given
    /// cable is plugged into. Used by ReachabilityService to find the entry interface when
    /// walking onto a router from a neighboring device.</summary>
    public int GetPortIndexForCable(Cable cable)
    {
        for (int i = 0; i < ports.Length; i++)
            if (ports[i].cable == cable) return i;
        return -1;
    }

    // Interface up/down state (IP assigned + not administratively shut down),
    // driven by RouterCLI. Cable uses this to decide whether to show packet flow.
    public bool IsPortActive(int index) => ports[index].active;
    public void SetPortActive(int index, bool active) => ports[index].active = active;

    // Reused across calls the same way DragDropManager/TableManager/CableManager's
    // own selection-highlight helpers already do — Renderer.SetPropertyBlock
    // snapshots the block's current values, so one shared instance is safe to
    // reuse sequentially across many different renderers/devices.
    static MaterialPropertyBlock s_portHighlightBlock;

    // Tints every currently-FREE port's own small visual part (see Port.visualGo)
    // a bright color — driven by CableManager while InterfacePickerPopup's
    // eth0/eth1/... list is open for this device, so the player can see exactly
    // where each listed interface physically is on the model instead of the
    // list being disconnected from anything visible. Already-connected ports
    // are left alone since they're not choices in that list. Passing on=false
    // clears every port back to its normal material regardless of color.
    public void HighlightFreePorts(bool on, Color color)
    {
        foreach (var p in ports)
        {
            if (p.visualGo == null || p.connectedDevice != null) continue;
            var r = p.visualGo.GetComponent<Renderer>();
            if (r == null) continue;

            if (!on) { r.SetPropertyBlock(null); continue; }

            // Same guards as PacketSendManager.HighlightDevice.
            if (r.sharedMaterial == null) continue;
            bool hasBase = r.sharedMaterial.HasProperty("_BaseColor");
            if (!hasBase && !r.sharedMaterial.HasProperty("_Color")) continue;

            s_portHighlightBlock ??= new MaterialPropertyBlock();
            string prop = hasBase ? "_BaseColor" : "_Color";
            s_portHighlightBlock.Clear();
            s_portHighlightBlock.SetColor(prop, color);
            r.SetPropertyBlock(s_portHighlightBlock);
        }
    }

    // General fallback anchor (device center) — used only for the in-progress
    // drag-preview line before a source port has been chosen.
    public Vector3 GetConnectionPoint() => transform.position + Vector3.up * 0.6f;

    // World point directly above the device's own tallest visible point (its
    // monitor for a PC, its antennas for a Router, ...) — a single fixed
    // offset above transform.position can't work for every device type at
    // once (a PC's monitor alone reaches ~0.73 above its base, nearly 4x a
    // Switch's ~0.2 profile), so callers that need to float something (e.g. a
    // WorldPulseMarker) clearly above whichever device it's pointing at
    // should anchor to this instead of the device's raw position. Falls back
    // to transform.position if the device has no renderers yet.
    public Vector3 VisualTopPoint()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return transform.position;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return new Vector3(transform.position.x, b.max.y, transform.position.z);
    }
}
