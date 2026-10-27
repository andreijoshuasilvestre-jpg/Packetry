using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Ctrl+Z failsafe for placement/removal/cabling mistakes — a last resort for
/// the ones no amount of guardrail UI catches (wrong device removed, wrong
/// cable pulled, wrong device placed). Listens to the same events
/// AutoSaveManager already relies on (DragDropManager.OnDeviceDropped,
/// DeviceRemoverManager.OnDeviceRemoved, CableManager.OnCableCreated/
/// OnCableRemoved, TableManager.OnTablePlaced/OnTableResized,
/// TableRemoverManager.OnTableRemoved) and pushes one reversible entry per
/// event onto a bounded stack; Ctrl+Z pops and reverses the most recent one.
///
/// Deliberately NOT a generalized command/redo system — every entry's Undo()
/// just re-derives the opposite of what already happened (destroy what was
/// just built, rebuild what was just destroyed) from a snapshot taken at the
/// moment its event fired, going through the same builders
/// (DeviceModelBuilder, CableManager.CreateCable/RemoveCable) every other
/// placement flow already uses. A cascading removal (TableRemoverManager
/// destroying every device on a table alongside it) becomes several stack
/// entries, not one — undoing a whole cascade takes several Ctrl+Z presses,
/// same order (most recent first) as any other sequence of actions.
///
/// Rebuilding something always makes a NEW object, so every other entry
/// still pointing at the destroyed original goes through Live() (see
/// replacements) to find what replaced it — that's what lets a cable
/// between two devices removed one after another (or together, with their
/// table) come back once both of them have.
///
/// Only what an undo actually touches animates, borrowing the level's own
/// entrance/exit language (LevelBuildInEffect.PlayForItem/PlayExitForItems)
/// rather than the player-placement blueprint sweep: a device/table taken
/// away shrinks off with a dust puff, one put back grows in and its cables
/// pop in once it lands. A lone cable undo keeps CableManager's own
/// draw-in/instant removal — the level sequence gives cables no animation.
/// </summary>
public class UndoManager : MonoBehaviour
{
    public static UndoManager Instance { get; private set; }

    const int MaxStackDepth = 20;

    interface IUndoEntry
    {
        string Description { get; }
        // Null once undone; otherwise why it couldn't be (its target is gone,
        // or a port it needs has been reused since) — PerformUndo reports
        // that rather than claiming something happened when nothing did.
        string Undo();
    }

    readonly List<IUndoEntry> stack = new();

    // Guards against an Undo() call's own Destroy()/CreateCable()/etc. calls
    // re-triggering the very event handlers that push entries onto this
    // stack — without this, pressing Ctrl+Z once would push a fresh entry
    // for every event its own reversal fires, turning one undo into an
    // infinite ping-pong.
    bool suppressRecording;

    // Destroyed original -> the object an undo rebuilt in its place. Keys
    // stay valid after Destroy(): UnityEngine.Object hashes and compares by
    // instance ID, which a destroyed object keeps.
    readonly Dictionary<Object, Object> replacements = new();

    /// <summary>Guided levels (the Level 1 tutorial, Level2Guide..Level4Guide)
    /// turn this off alongside hiding the tools they don't teach — each stage
    /// assumes only what it unlocked can change the scene, and an undo taking
    /// back a placed device or cable (sidestepping the hidden Remove tools)
    /// can leave a stage waiting on something that no longer exists. Level 4
    /// only lets its Router be placed on the first stage, so undoing past it
    /// was a soft-lock.</summary>
    public bool AvailableInThisLevel { get; set; } = true;

    /// <summary>Fired for each device an undo rebuilds — the counterpart of
    /// DragDropManager.OnDeviceDropped for a device that comes back without
    /// being dropped, so level objectives tracking what's on the field (see
    /// PlacedDeviceTracker) count it again.</summary>
    public event System.Action<NetworkDevice> OnDeviceRestored;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped += HandleDeviceDropped;
        if (DeviceRemoverManager.Instance != null) DeviceRemoverManager.Instance.OnDeviceRemoved += HandleDeviceRemoved;
        if (CableManager.Instance != null)
        {
            CableManager.Instance.OnCableCreated += HandleCableCreated;
            CableManager.Instance.OnCableRemoved += HandleCableRemoved;
        }
        if (TableManager.Instance != null)
        {
            TableManager.Instance.OnTablePlaced  += HandleTablePlaced;
            TableManager.Instance.OnTableResized += HandleTableResized;
        }
        if (TableRemoverManager.Instance != null) TableRemoverManager.Instance.OnTableRemoved += HandleTableRemoved;
    }

    void Update()
    {
        if (Keyboard.current == null) return;
        bool ctrl = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
        if (!ctrl || !Keyboard.current.zKey.wasPressedThisFrame) return;

        // Don't steal Ctrl+Z away from an actual text field (CLI command line,
        // address popup's IP field, ...) — those already get TMP_InputField's
        // own text-undo for free.
        var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel != null && sel.GetComponent<TMP_InputField>() != null) return;

        if (IsBusy()) return;

        if (!AvailableInThisLevel)
        {
            NotificationManager.Instance?.Notify("Undo isn't available in guided levels.", NotifyKind.Info);
            return;
        }

        PerformUndo();
    }

    // Something mid-use still holds a direct reference to a device/cable/
    // table an undo could destroy out from under it — a device or table in
    // hand (Ctrl is also the grid-snap key while dragging, so Ctrl+Z is an
    // easy slip there), a port picker or config panel open on a device — or
    // the level is already tearing down. Ctrl+Z waits until that's settled
    // instead of each of them having to survive its target vanishing.
    static bool IsBusy()
    {
        if (GameBootstrap.ExitStarted) return true;
        if (DragDropManager.Instance != null && DragDropManager.Instance.IsDragging) return true;
        if (TableManager.Instance != null && (TableManager.Instance.IsDragging || TableManager.Instance.IsResizing)) return true;
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return true;
        if (InteractionMode.IsSuspended) return true;
        if (ConfirmDialogManager.Instance != null && ConfirmDialogManager.Instance.IsOpen) return true;
        if (DeviceAddressPopup.Instance != null && DeviceAddressPopup.Instance.IsOpen) return true;
        if (InterfacePickerPopup.Instance != null && InterfacePickerPopup.Instance.IsOpen) return true;
        if (PacketTypePickerPopup.Instance != null && PacketTypePickerPopup.Instance.IsOpen) return true;
        return false;
    }

    void PerformUndo()
    {
        if (stack.Count == 0)
        {
            NotificationManager.Instance?.Notify("Nothing to undo", NotifyKind.Info);
            return;
        }

        var entry = stack[^1];
        stack.RemoveAt(stack.Count - 1);

        // The selected device/table may be what this undo takes away —
        // deselect first so its highlight and camera focus are released
        // normally instead of being left tracking a destroyed object.
        DragDropManager.Instance?.ClearHighlight();
        TableManager.Instance?.ClearHighlight();

        string failure = null;
        suppressRecording = true;
        try { failure = entry.Undo(); }
        finally { suppressRecording = false; }

        if (failure == null)
            NotificationManager.Instance?.Notify($"Undone: {entry.Description}", NotifyKind.Info);
        else
            NotificationManager.Instance?.Notify(failure, NotifyKind.Warning);
    }

    void Push(IUndoEntry entry)
    {
        stack.Add(entry);
        if (stack.Count > MaxStackDepth) stack.RemoveAt(0);
    }

    // ── Identity across rebuilds ──────────────────────────────────────────

    static void Replaced(Object original, Object replacement)
    {
        if (Instance == null || ReferenceEquals(original, null) || replacement == null) return;
        Instance.replacements[original] = replacement;
    }

    // The live object standing in for obj: obj itself while it still
    // exists, otherwise whatever an undo rebuilt in its place (following
    // the chain if that was itself removed and rebuilt again). Returns the
    // destroyed original if it was never brought back.
    static T Live<T>(T obj) where T : Object
    {
        if (Instance == null) return obj;
        while (obj == null && !ReferenceEquals(obj, null) &&
               Instance.replacements.TryGetValue(obj, out Object next))
            obj = next as T;
        return obj;
    }

    // ── Event handlers (snapshot at the moment each action happens) ─────────

    void HandleDeviceDropped(NetworkDevice device, bool isNewDevice)
    {
        if (suppressRecording || !isNewDevice || device == null) return;
        Push(new DevicePlacedEntry(device));
    }

    void HandleDeviceRemoved(NetworkDevice device)
    {
        if (suppressRecording || device == null) return;
        Push(new DeviceRemovedEntry(device));
    }

    void HandleCableCreated(Cable cable)
    {
        if (suppressRecording || cable == null) return;
        Push(new CableCreatedEntry(cable));
    }

    void HandleCableRemoved(Cable cable)
    {
        if (suppressRecording || cable == null) return;
        Push(new CableRemovedEntry(cable));
    }

    void HandleTablePlaced(Table table)
    {
        if (suppressRecording || table == null) return;
        Push(new TablePlacedEntry(table));
    }

    void HandleTableRemoved(Table table)
    {
        if (suppressRecording || table == null) return;
        Push(new TableRemovedEntry(table));
    }

    void HandleTableResized(Table table, Vector3 oldCenter, float oldWidth, float oldLength)
    {
        if (suppressRecording || table == null) return;
        Push(new TableResizedEntry(table, oldCenter, oldWidth, oldLength));
    }

    // ── Entries ───────────────────────────────────────────────────────────

    // Undo a fresh placement: just remove the device again.
    class DevicePlacedEntry : IUndoEntry
    {
        readonly NetworkDevice device;
        public string Description { get; }

        public DevicePlacedEntry(NetworkDevice device)
        {
            this.device = device;
            Description = $"placed a {device.deviceType}";
        }

        public string Undo()
        {
            var live = Live(device);
            if (live == null || DeviceRemoverManager.Instance == null)
                return "Couldn't undo placing that device — it's already gone.";

            LevelBuildInEffect.PlayExitForItems(new[] { live });
            DeviceRemoverManager.Instance.RemoveDevice(live);
            return null;
        }
    }

    // Undo a removal: rebuild the device at the same spot, reapply its
    // DeviceAddress (if any), port up/down state and Router interface table,
    // and reconnect any cables it had — snapshotted here since the real
    // device/cables are about to be destroyed.
    class DeviceRemovedEntry : IUndoEntry
    {
        readonly NetworkDevice original;
        readonly DeviceType type;
        readonly Vector3 position;
        readonly Quaternion rotation;
        readonly bool hadAddress;
        readonly string ip, mask;
        readonly long addressedAt;
        readonly bool acceptHttp, acceptFile;
        readonly bool[] portActive;
        // Router only — a copy, since the originals are destroyed with it.
        readonly RouterInterfaceConfig.PortConfig[] interfaces;
        readonly List<(int myPort, NetworkDevice other, int otherPort, Cable cable)> links = new();

        public string Description { get; }

        public DeviceRemovedEntry(NetworkDevice device)
        {
            original = device;
            type = device.deviceType;
            position = device.transform.position;
            rotation = device.transform.rotation;
            Description = $"removed a {type}";

            var addr = device.GetComponent<DeviceAddress>();
            if (addr != null)
            {
                hadAddress = true;
                ip = addr.Ip;
                mask = addr.Mask;
                addressedAt = addr.AddressedAtTicks;
                acceptHttp = addr.AcceptsHttpRequest;
                acceptFile = addr.AcceptsFileTransfer;
            }

            portActive = new bool[device.PortCount];
            for (int i = 0; i < device.PortCount; i++) portActive[i] = device.IsPortActive(i);

            var cfg = device.GetComponent<RouterInterfaceConfig>();
            if (cfg != null)
            {
                interfaces = new RouterInterfaceConfig.PortConfig[cfg.Count];
                for (int i = 0; i < cfg.Count; i++)
                {
                    var pc = cfg.Get(i);
                    interfaces[i] = pc == null
                        ? new RouterInterfaceConfig.PortConfig()
                        : new RouterInterfaceConfig.PortConfig
                        {
                            ip = pc.ip, mask = pc.mask, shutdown = pc.shutdown, addressedAtTicks = pc.addressedAtTicks,
                        };
                }
            }

            for (int i = 0; i < device.PortCount; i++)
            {
                Cable cable = device.GetPortCable(i);
                if (cable == null) continue;
                NetworkDevice other = cable.DeviceA == device ? cable.DeviceB : cable.DeviceA;
                if (other == null) continue;
                int otherPort = other.GetPortIndexForCable(cable);
                if (otherPort < 0) continue;
                links.Add((i, other, otherPort, cable));
            }
        }

        public string Undo()
        {
            GameObject go = type switch
            {
                DeviceType.PC     => DeviceModelBuilder.BuildPC(),
                DeviceType.Switch => DeviceModelBuilder.BuildSwitch(),
                DeviceType.Router => DeviceModelBuilder.BuildRouter(),
                _                 => null,
            };
            if (go == null) return $"Couldn't rebuild that {type}.";

            go.transform.position = position;
            go.transform.rotation = rotation;

            var nd = go.GetComponent<NetworkDevice>();
            Replaced(original, nd);

            if (hadAddress)
            {
                var addr = go.AddComponent<DeviceAddress>();
                addr.SetAddress(ip, mask, addressedAt); // keeps its original timestamp rather than stamping "now"
                addr.SetAccepts(acceptHttp, acceptFile);
            }

            // Both live on the device itself (RouterInterfaceConfig's doc
            // comment covers why), so they come back with it — the same
            // fields SceneStateCapture saves and restores.
            for (int i = 0; i < portActive.Length && i < nd.PortCount; i++)
                nd.SetPortActive(i, portActive[i]);

            if (interfaces != null)
            {
                var cfg = go.GetComponent<RouterInterfaceConfig>();
                if (cfg == null) cfg = go.AddComponent<RouterInterfaceConfig>();
                cfg.EnsureSize(nd.PortCount);
                for (int i = 0; i < interfaces.Length; i++)
                {
                    var pc = cfg.Get(i);
                    if (pc == null) continue;
                    pc.ip = interfaces[i].ip;
                    pc.mask = interfaces[i].mask;
                    pc.shutdown = interfaces[i].shutdown;
                    pc.addressedAtTicks = interfaces[i].addressedAtTicks;
                }
            }

            var cables = new List<Cable>();
            foreach (var (myPort, other, otherPort, oldCable) in links)
            {
                var live = Live(other);
                if (live == null) continue; // that endpoint was removed too and hasn't been restored (yet) — its own restore reconnects this link
                if (live.GetPortCable(otherPort) != null) continue; // that port's been reused by something else since — don't steal it back
                var cable = CableManager.Instance?.CreateCable(nd, myPort, live, otherPort, playReveal: false);
                if (cable == null) continue;
                cables.Add(cable);
                Replaced(oldCable, cable);
            }

            // Restored port/interface state never went through CreateCable/
            // SetAddress, whose own refresh ran before it was applied.
            SubnetVisualizer.RefreshAll();

            // Stays kinematic (as built) while it grows — a near-zero collider
            // under gravity would sink before the full-size one shoved it back
            // up — then rests like any dropped device (DragDropManager.Drop()),
            // unless the player has already picked it back up mid-grow.
            var rb = go.GetComponent<Rigidbody>();
            LevelBuildInEffect.PlayForItem(go.transform, cables, onComplete: () =>
            {
                if (rb == null) return;
                if (DragDropManager.Instance != null && DragDropManager.Instance.IsHolding(go)) return;
                rb.isKinematic = false;
            });

            Instance?.OnDeviceRestored?.Invoke(nd);
            return null;
        }
    }

    // Undo a fresh cable: remove it again.
    class CableCreatedEntry : IUndoEntry
    {
        readonly Cable cable;
        public string Description => "connected a cable";

        public CableCreatedEntry(Cable cable) { this.cable = cable; }

        public string Undo()
        {
            var live = Live(cable);
            if (live == null) return "Couldn't undo that cable — it's already gone.";
            CableManager.Instance?.RemoveCable(live);
            return null;
        }
    }

    // Undo a removal: reconnect the same two ports, snapshotted before the
    // cable's own Destroy() runs.
    class CableRemovedEntry : IUndoEntry
    {
        readonly Cable original;
        readonly NetworkDevice deviceA, deviceB;
        readonly int portA, portB;
        public string Description => "removed a cable";

        public CableRemovedEntry(Cable cable)
        {
            original = cable;
            deviceA  = cable.DeviceA;
            portA    = cable.PortA;
            deviceB  = cable.DeviceB;
            portB    = cable.PortB;
        }

        public string Undo()
        {
            var a = Live(deviceA);
            var b = Live(deviceB);
            if (a == null || b == null) return "Couldn't reconnect that cable — a device it was plugged into is gone.";
            if (a.GetPortCable(portA) != null || b.GetPortCable(portB) != null)
                return "Couldn't reconnect that cable — one of its ports is in use now.";

            var cable = CableManager.Instance?.CreateCable(a, portA, b, portB);
            Replaced(original, cable);
            return null;
        }
    }

    // Undo a fresh table: remove it again — but only while it's empty.
    class TablePlacedEntry : IUndoEntry
    {
        readonly Table table;
        public string Description => "placed a table";

        public TablePlacedEntry(Table table) { this.table = table; }

        public string Undo()
        {
            var live = Live(table);
            if (live == null) return "Couldn't undo that table — it's already gone.";

            // Anything sitting on it now was moved there afterward, or is one
            // of the level's own starting devices — whatever was placed on it
            // after the table has its own entry above this one, already
            // undone by the time this runs. Taking the table away used to
            // delete those along with it, unrecorded and unrecoverable.
            foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
                if (!device.isPaletteItem && live.ContainsXZ(device.transform.position))
                    return "Kept the table — there are devices on it now.";

            LevelBuildInEffect.PlayExitForItems(null, new[] { live });
            Object.Destroy(live.gameObject);
            return null;
        }
    }

    // Undo a resize: put the old footprint back — unless a device now sits on
    // the part it would lose, or something placed since is in the way of the
    // part it would get back.
    class TableResizedEntry : IUndoEntry
    {
        readonly Table table;
        readonly Vector3 center;
        readonly float width, length;
        public string Description => "resized a table";

        public TableResizedEntry(Table table, Vector3 oldCenter, float oldWidth, float oldLength)
        {
            this.table = table;
            center = oldCenter;
            width  = oldWidth;
            length = oldLength;
        }

        public string Undo()
        {
            var live = Live(table);
            if (live == null) return "Couldn't undo that resize — the table is gone.";

            var onTable = new List<NetworkDevice>();
            foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
            {
                if (device.isPaletteItem || !live.ContainsXZ(device.transform.position)) continue;
                Vector3 p = device.transform.position;
                if (Mathf.Abs(p.x - center.x) > width * 0.5f || Mathf.Abs(p.z - center.z) > length * 0.5f)
                    return "Kept the table's size — there are devices on the part it would lose.";
                onTable.Add(device);
            }
            if (Table.WouldOverlap(center, width, length, live) || Table.WouldCoverDevice(center, width, length, onTable))
                return "Kept the table's size — something's in the way of its old size now.";

            DeviceModelBuilder.ResizeTable(live, width, length);
            live.transform.position = new Vector3(center.x, live.transform.position.y, center.z);
            live.SyncFinalCenter();
            PacketDeviceEffect.Play(live.gameObject, live.transform, Color.white, PacketDeviceEffect.PlacementIntensity, AnimationKind.Build);
            // Recording is suppressed during an undo, and nothing here fires
            // the event AutoSaveManager listens for — save the old size directly.
            AutoSaveManager.Instance?.SaveNow();
            return null;
        }
    }

    // Undo a removal: rebuild the table at the same spot. Any devices that
    // were on it are restored independently by their own DeviceRemovedEntry
    // (pushed just before this one, per TableRemoverManager.DoRemove's
    // device-then-table order) — no need to duplicate that here.
    class TableRemovedEntry : IUndoEntry
    {
        readonly Table original;
        readonly float width, length;
        readonly Vector3 center;
        public string Description => "removed a table";

        public TableRemovedEntry(Table table)
        {
            original = table;
            width    = table.width;
            length   = table.length;
            center   = table.FinalCenter;
        }

        public string Undo()
        {
            GameObject go = DeviceModelBuilder.BuildTable(width, length);
            go.transform.position = center;
            var restored = go.GetComponent<Table>();
            if (restored != null) restored.SyncFinalCenter();
            Replaced(original, restored);
            LevelBuildInEffect.PlayForItem(go.transform);
            return null;
        }
    }
}
