using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// Packet Assign Mode — click a free PC to pick its outgoing type (it becomes
/// a sender, up-arrow badge via PacketAssignBadge), click other free PCs to
/// become its receivers (down-arrow badges), Enter COMMITS that pairing
/// (unlike the old immediate-fire flow) so the player can build up many
/// sender/receiver pairings across the whole network before anything actually
/// sends. Right-click any placed PC opens a contextual popup instead: free →
/// same type pick as left-click; already assigned → switch type / Clear. The
/// sidebar's separate Run Network button (NetworkUIManager.OnRunNetworkClicked)
/// fires every committed pairing at once via RunNetwork(), checking each PC's
/// accept-flags (DeviceAddress) before handing surviving destinations to
/// PacketManager.TrySendMulti, then clears
/// the whole board. A receiver is locked out of becoming a sender until its
/// assignment is cleared — real computers can send and receive at once, so
/// the two roles are otherwise independent (see the packet-send redesign
/// memory: earlier considered a mutually-exclusive send/receive mode and
/// rejected it as unrealistic and confusing).
/// </summary>
public class PacketSendManager : MonoBehaviour
{
    public static PacketSendManager Instance { get; private set; }

    public bool IsSending => isSending;
    public bool HasSource => currentSender != null;
    public int QueuedCount => currentReceivers.Count;
    public int AssignmentCount => assignments.Count;
    public bool HasPendingWork => assignments.Count > 0 || (currentSender != null && currentReceivers.Count > 0);

    class Assignment
    {
        public NetworkDevice Sender;
        public PacketType Type;
        public List<NetworkDevice> Receivers;
    }

    bool isSending;
    NetworkDevice currentSender;
    PacketType currentType;
    readonly List<NetworkDevice> currentReceivers = new();
    readonly List<Assignment> assignments = new();

    readonly Dictionary<NetworkDevice, PacketAssignBadge> senderBadges = new();
    readonly Dictionary<NetworkDevice, PacketAssignBadge> receiverBadges = new();

    // Which PCs' monitors are showing a standby (armed sender/receiver) screen
    // and as what — diffed against each frame's roles (same idiom as
    // PacketManager's flowingLastFrame), so every role change reaches the
    // monitors without each of the many paths that change roles having to.
    readonly Dictionary<NetworkDevice, (PacketType type, bool sender)> monitorRoles = new();

    Camera mainCamera;

    static MaterialPropertyBlock s_propBlock;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        mainCamera = Camera.main;
        EnsurePickerExists();

        if (DeviceRemoverManager.Instance != null)
            DeviceRemoverManager.Instance.OnDeviceRemoved += HandleDeviceRemoved;
    }

    void OnDestroy()
    {
        if (DeviceRemoverManager.Instance != null)
            DeviceRemoverManager.Instance.OnDeviceRemoved -= HandleDeviceRemoved;
    }

    static void EnsurePickerExists()
    {
        if (PacketTypePickerPopup.Instance == null)
        {
            var go = new GameObject("PacketTypePickerPopup");
            go.AddComponent<PacketTypePickerPopup>();
        }
    }

    void Update()
    {
        if (!isSending) return;
        SyncMonitors();
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return;
        if (InteractionMode.IsSuspended) return;
        if (DeviceAddressPopup.Instance != null && DeviceAddressPopup.Instance.IsOpen) return;

        // While the type/edit popup is open, let clicks go to its own UI
        // buttons instead of the 3D scene — it already dismisses itself on a
        // click outside its own rect (see PacketTypePickerPopup.Update), so
        // there's nothing extra to do here beyond not leaking the click through.
        if (PacketTypePickerPopup.Instance != null && PacketTypePickerPopup.Instance.IsOpen) return;

        if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI())
            TryLeftClick();

        if (Mouse.current.rightButton.wasPressedThisFrame && !IsPointerOverUI())
            TryRightClick();

        if (currentSender != null && currentReceivers.Count > 0 && Keyboard.current != null &&
            (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            CommitCurrent();
    }

    static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    // ── Click handling ───────────────────────────────────────────────────────

    void TryLeftClick()
    {
        NetworkDevice device = RaycastForDevice();
        if (device == null) return;
        if (!HasValidAddress(device)) return;

        bool isSenderRole = IsSenderRole(device);
        bool isReceiverRole = IsReceiverRole(device);

        if (isSenderRole || isReceiverRole)
        {
            NotificationManager.Instance?.Notify(
                "That PC is already assigned — right-click it to change or clear its role.", NotifyKind.Warning);
            AudioManager.Instance?.PlaySfx(SfxId.Refusal);
            return;
        }

        if (currentSender == null)
            OpenContextPopup(device);
        else
            ToggleReceiver(device);
    }

    void TryRightClick()
    {
        NetworkDevice device = RaycastForDevice();
        if (device == null || device.deviceType != DeviceType.PC) { Cancel(); return; }
        if (!HasValidAddress(device)) return;

        OpenContextPopup(device);
    }

    NetworkDevice RaycastForDevice()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return null;

        var device = hit.collider.GetComponentInParent<NetworkDevice>();
        return (device == null || device.isPaletteItem) ? null : device;
    }

    static bool HasValidAddress(NetworkDevice device)
    {
        var addr = device.GetComponent<DeviceAddress>();
        if (addr != null && addr.HasAddress) return true;
        NotificationManager.Instance?.Notify(
            "That device has no IP address yet — right-click a PC to configure one.", NotifyKind.Warning);
        AudioManager.Instance?.PlaySfx(SfxId.Refusal);
        return false;
    }

    // ── Popup ────────────────────────────────────────────────────────────────

    // Serves every context: a free device only ever offers the two type
    // rows (identical result whether reached via left- or right-click); an
    // already-assigned device offers switch-type/Clear rows for whichever
    // role(s) it currently holds — a device can hold both (sender in one
    // pairing, receiver in another), in which case both role's rows appear.
    void OpenContextPopup(NetworkDevice device)
    {
        bool isSenderRole = IsSenderRole(device);
        bool isReceiverRole = IsReceiverRole(device);
        var rows = new List<PacketTypePickerPopup.Row>();

        if (!isSenderRole && !isReceiverRole)
        {
            rows.Add(TypeRow("HTTP Request", PacketType.HttpRequest, false, () => BeginNewSender(device, PacketType.HttpRequest)));
            rows.Add(TypeRow("File Transfer", PacketType.FileTransfer, false, () => BeginNewSender(device, PacketType.FileTransfer)));
        }
        else
        {
            if (isSenderRole)
            {
                PacketType current = SenderType(device);
                rows.Add(TypeRow("HTTP Request", PacketType.HttpRequest, current == PacketType.HttpRequest, () => SetSenderType(device, PacketType.HttpRequest)));
                rows.Add(TypeRow("File Transfer", PacketType.FileTransfer, current == PacketType.FileTransfer, () => SetSenderType(device, PacketType.FileTransfer)));
                rows.Add(ClearRow("Clear Sending", () => ClearSenderRole(device)));
            }
            if (isReceiverRole)
                rows.Add(ClearRow(isSenderRole ? "Stop Receiving" : "Clear", () => ClearReceiverRole(device)));
        }

        string ip = device.GetComponent<DeviceAddress>().Ip;
        string header = isSenderRole ? $"Sending from {ip}"
                      : isReceiverRole ? $"Receiving at {ip}"
                      : $"Send from {ip}";
        PacketTypePickerPopup.Instance.Show(Mouse.current.position.ReadValue(), header, rows);
    }

    // A packet-type row wears that type's own glyph and color — the same ones
    // the traveling packet and the armed PC monitor show (PacketVisual).
    static PacketTypePickerPopup.Row TypeRow(string label, PacketType type, bool selected, Action onClick) => new()
    {
        Label = label, OnClick = onClick, Selected = selected,
        Icon = PacketVisual.IconFor(type), Accent = PacketVisual.ColorFor(type),
    };

    static PacketTypePickerPopup.Row ClearRow(string label, Action onClick) => new()
    {
        Label = label, OnClick = onClick, Destructive = true,
        Icon = CrossIconBuilder.GetSprite(Color.white),
    };

    PacketType SenderType(NetworkDevice device) =>
        device == currentSender ? currentType : assignments.Find(a => a.Sender == device).Type;

    // ── Role queries ─────────────────────────────────────────────────────────

    bool IsSenderRole(NetworkDevice device) =>
        device == currentSender || assignments.Exists(a => a.Sender == device);

    bool IsReceiverRole(NetworkDevice device) =>
        (currentSender != null && currentReceivers.Contains(device)) ||
        assignments.Exists(a => a.Receivers.Contains(device));

    // ── State transitions ────────────────────────────────────────────────────

    void BeginNewSender(NetworkDevice device, PacketType type)
    {
        ResolveInProgress();
        currentSender = device;
        currentType = type;
        AttachSenderBadge(device);
        HighlightDevice(device, true);
    }

    // Auto-commits the in-progress sender if it already has receivers picked
    // (same effect as pressing Enter), or quietly abandons it if it doesn't —
    // called before starting a brand new sender so there's never more than
    // one uncommitted pairing in flight at once.
    void ResolveInProgress()
    {
        if (currentSender == null) return;

        if (currentReceivers.Count > 0)
        {
            CommitCurrent();
        }
        else
        {
            HighlightDevice(currentSender, false);
            RemoveSenderBadge(currentSender);
            currentSender = null;
        }
    }

    void CommitCurrent()
    {
        assignments.Add(new Assignment
        {
            Sender = currentSender,
            Type = currentType,
            Receivers = new List<NetworkDevice>(currentReceivers),
        });

        HighlightDevice(currentSender, false);
        foreach (var r in currentReceivers) HighlightDevice(r, false);

        currentSender = null;
        currentReceivers.Clear();
    }

    void ToggleReceiver(NetworkDevice device)
    {
        if (currentReceivers.Contains(device))
        {
            currentReceivers.Remove(device);
            HighlightDevice(device, false);
            RemoveReceiverBadgeIfUnused(device);
        }
        else
        {
            currentReceivers.Add(device);
            HighlightDevice(device, true);
            AttachReceiverBadge(device);
        }
    }

    void SetSenderType(NetworkDevice device, PacketType type)
    {
        if (device == currentSender) { currentType = type; return; }
        var a = assignments.Find(x => x.Sender == device);
        if (a != null) a.Type = type;
    }

    void ClearSenderRole(NetworkDevice device)
    {
        if (device == currentSender)
        {
            HighlightDevice(currentSender, false);
            foreach (var r in currentReceivers)
            {
                HighlightDevice(r, false);
                RemoveReceiverBadgeIfUnused(r);
            }
            currentReceivers.Clear();
            RemoveSenderBadge(currentSender);
            currentSender = null;
            return;
        }

        var a = assignments.Find(x => x.Sender == device);
        if (a == null) return;
        assignments.Remove(a);
        RemoveSenderBadge(device);
        foreach (var r in a.Receivers) RemoveReceiverBadgeIfUnused(r);
    }

    void ClearReceiverRole(NetworkDevice device)
    {
        if (currentSender != null) currentReceivers.Remove(device);
        foreach (var a in assignments) a.Receivers.Remove(device);
        HighlightDevice(device, false);
        RemoveReceiverBadgeIfUnused(device);
    }

    // ── Badges ───────────────────────────────────────────────────────────────

    void AttachSenderBadge(NetworkDevice device)
    {
        if (senderBadges.ContainsKey(device)) return;
        senderBadges[device] = PacketAssignBadge.Attach(device, isSender: true);
        AudioManager.Instance?.PlaySfx(SfxId.Tagging);
    }

    void RemoveSenderBadge(NetworkDevice device)
    {
        if (!senderBadges.TryGetValue(device, out var badge)) return;
        badge?.Remove();
        senderBadges.Remove(device);
    }

    void AttachReceiverBadge(NetworkDevice device)
    {
        if (receiverBadges.ContainsKey(device)) return;
        receiverBadges[device] = PacketAssignBadge.Attach(device, isSender: false);
        AudioManager.Instance?.PlaySfx(SfxId.Tagging);
    }

    void RemoveReceiverBadgeIfUnused(NetworkDevice device)
    {
        if (IsReceiverRole(device)) return; // still a receiver somewhere else — keep the badge
        if (!receiverBadges.TryGetValue(device, out var badge)) return;
        badge?.Remove();
        receiverBadges.Remove(device);
    }

    // ── Run Network ──────────────────────────────────────────────────────────

    /// <summary>Called by NetworkUIManager's Run Network button. Auto-commits
    /// any in-progress pick, then fires every committed pairing at once,
    /// checking accept-flags before handing surviving destinations to
    /// PacketManager — enforcement is experiential here, not preventive;
    /// nothing earlier in this flow ever blocked a refused assignment from
    /// being made, only from succeeding.</summary>
    public void RunNetwork()
    {
        if (!isSending) return;

        ResolveInProgress();
        if (assignments.Count == 0)
        {
            NotificationManager.Instance?.Notify("Nothing assigned yet — pick a sender and at least one destination first.", NotifyKind.Warning);
            AudioManager.Instance?.PlaySfx(SfxId.Refusal);
            return;
        }

        int sent = 0, failed = 0, acceptFails = 0, otherFails = 0;

        foreach (var a in assignments)
        {
            var passed = new List<NetworkDevice>();
            foreach (var r in a.Receivers)
            {
                var addr = r.GetComponent<DeviceAddress>();
                if (addr != null && !addr.Accepts(a.Type))
                {
                    failed++; acceptFails++;
                    PacketFailureEffect.Play(a.Sender, r);
                    continue;
                }

                passed.Add(r);
            }

            if (passed.Count == 0) continue;

            List<(NetworkDevice dest, string reason)> results =
                PacketManager.Instance?.TrySendMulti(a.Sender, passed, a.Type) ?? new List<(NetworkDevice, string)>();

            foreach (var (dest, _) in results) PacketFailureEffect.Play(a.Sender, dest);
            failed += results.Count;
            otherFails += results.Count;
            sent += passed.Count - results.Count;
        }

        // No run-wide sound: each failure above already played PingError
        // (PacketFailureEffect), and each delivery plays PacketSuccess as it lands.
        NotifyRunResult(sent, failed, acceptFails, otherFails);

        ClearBoard();
        SyncMonitors();
    }

    // Puts every sender/receiver's monitor on its standby screen in its
    // packet type's color and glyph (chevrons up for a sender, down for a
    // receiver; a PC that's both shows as a sender), and takes it back off
    // any PC that no longer holds a role. Empty whenever the mode is off.
    void SyncMonitors()
    {
        var roles = new Dictionary<NetworkDevice, (PacketType type, bool sender)>();
        if (isSending)
        {
            foreach (var a in assignments)
                foreach (var r in a.Receivers) roles[r] = (a.Type, false);
            if (currentSender != null)
                foreach (var r in currentReceivers) roles[r] = (currentType, false);
            foreach (var a in assignments) roles[a.Sender] = (a.Type, true);
            if (currentSender != null) roles[currentSender] = (currentType, true);
        }

        foreach (var kv in monitorRoles)
            if (!roles.ContainsKey(kv.Key)) PCMonitor.ClearStandby(kv.Key);

        foreach (var kv in roles)
        {
            if (monitorRoles.TryGetValue(kv.Key, out var shown) && shown.Equals(kv.Value)) continue;
            PacketType type = kv.Value.type;
            PCMonitor.SetStandby(kv.Key, PacketVisual.ColorFor(type), PacketVisual.ScreenGlyphFor(type), outgoing: kv.Value.sender);
        }

        monitorRoles.Clear();
        foreach (var kv in roles) monitorRoles[kv.Key] = kv.Value;
    }

    void NotifyRunResult(int sent, int failed, int acceptFails, int otherFails)
    {
        if (failed == 0)
        {
            NotificationManager.Instance?.Notify(
                $"Sent to {sent} device{(sent == 1 ? "" : "s")}.", NotifyKind.Info);
            return;
        }

        var reasons = new List<string>();
        if (acceptFails > 0) reasons.Add($"{acceptFails} refused");
        if (otherFails > 0) reasons.Add($"{otherFails} unreachable");
        string detail = string.Join(", ", reasons);

        if (sent == 0)
            NotificationManager.Instance?.Notify($"All {failed} sends failed ({detail}).", NotifyKind.Danger);
        else
            NotificationManager.Instance?.Notify($"Sent to {sent}, {failed} failed ({detail}).", NotifyKind.Warning);
    }

    void ClearBoard()
    {
        assignments.Clear();
        foreach (var kv in senderBadges) kv.Value?.Remove();
        senderBadges.Clear();
        foreach (var kv in receiverBadges) kv.Value?.Remove();
        receiverBadges.Clear();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void BeginSending()
    {
        if (isSending) { Cancel(); return; } // toggle off

        InteractionMode.CancelOthers(this);
        isSending = true;
    }

    public void CancelSending() => Cancel();

    void Cancel()
    {
        if (currentSender != null) HighlightDevice(currentSender, false);
        foreach (var r in currentReceivers) HighlightDevice(r, false);
        currentSender = null;
        currentReceivers.Clear();
        assignments.Clear();

        foreach (var kv in senderBadges) kv.Value?.Remove();
        senderBadges.Clear();
        foreach (var kv in receiverBadges) kv.Value?.Remove();
        receiverBadges.Clear();

        isSending = false;
        SyncMonitors(); // mode off — every armed monitor back to normal
        if (PacketTypePickerPopup.Instance != null) PacketTypePickerPopup.Instance.Hide();
    }

    // A device deleted mid-assignment (Remove Device tool) needs to drop out
    // of every role it held — PacketAssignBadge also self-destroys once its
    // own device goes null as a second safety net, but this keeps
    // assignments/currentReceivers themselves from holding a dangling
    // reference too.
    void HandleDeviceRemoved(NetworkDevice device)
    {
        if (device == null) return;

        if (device == currentSender)
        {
            RemoveSenderBadge(currentSender);
            foreach (var r in currentReceivers) RemoveReceiverBadgeIfUnused(r);
            currentSender = null;
            currentReceivers.Clear();
        }
        else if (currentReceivers.Remove(device))
        {
            RemoveReceiverBadgeIfUnused(device);
        }

        for (int i = assignments.Count - 1; i >= 0; i--)
        {
            var a = assignments[i];
            if (a.Sender == device)
            {
                assignments.RemoveAt(i);
                RemoveSenderBadge(device);
                foreach (var r in a.Receivers) RemoveReceiverBadgeIfUnused(r);
            }
            else if (a.Receivers.Remove(device))
            {
                RemoveReceiverBadgeIfUnused(device);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static void HighlightDevice(NetworkDevice device, bool on)
    {
        if (!on)
        {
            // See DragDropManager.ApplyHighlightTint — a subnet-tinted device must
            // restore its tint rather than clear to null.
            var addr = device.GetComponent<DeviceAddress>();
            if (addr != null && addr.HasAddress) { addr.ApplyTint(); return; }

            foreach (var r in device.GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(null);
            return;
        }

        s_propBlock ??= new MaterialPropertyBlock();
        foreach (var r in device.GetComponentsInChildren<Renderer>())
        {
            // A renderer mid-effect (PingWipeEffect swaps materials while a
            // wipe/pulse plays) or one with no material slot filled at all
            // has a null sharedMaterial — skip it rather than crash the
            // whole click on a NullReferenceException (this is what was
            // breaking receiver-selection: PacketSendManager.cs:558). Also
            // skip a material that has neither _BaseColor nor _Color at all
            // (DeviceRimLight's additive Custom/RimGlow shells, which only
            // expose _RimColor) instead of blindly assuming "_Color".
            if (r.sharedMaterial == null) continue;
            bool hasBase = r.sharedMaterial.HasProperty("_BaseColor");
            if (!hasBase && !r.sharedMaterial.HasProperty("_Color")) continue;
            string prop = hasBase ? "_BaseColor" : "_Color";
            Color orig = r.sharedMaterial.GetColor(prop);
            s_propBlock.Clear();
            s_propBlock.SetColor(prop, Color.Lerp(orig, GameTheme.Info, 0.5f));
            r.SetPropertyBlock(s_propBlock);
        }
    }
}
