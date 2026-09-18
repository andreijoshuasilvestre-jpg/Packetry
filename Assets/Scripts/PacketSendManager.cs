using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// Player-triggered packet send — click a source PC, pick a type from the
/// popup, then click one or more destination PCs to queue them (clicking a
/// queued device again un-queues it). Enter fires a packet at every queued
/// destination simultaneously via PacketManager.TrySendMulti (which visually
/// trunks the shared start of their routes before splitting); right-click
/// discards the whole queue instead. Mirrors PingToolManager's click-source/
/// click-target shape, with a type-pick step and multi-destination queue
/// inserted before the actual send.
/// </summary>
public class PacketSendManager : MonoBehaviour
{
    public static PacketSendManager Instance { get; private set; }

    public bool IsSending => isSending;
    public bool HasSource => sourceDevice != null;
    public int QueuedCount => queuedDestinations.Count;

    bool isSending;
    NetworkDevice sourceDevice;
    PacketType pendingType;
    readonly List<NetworkDevice> queuedDestinations = new();
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
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return;
        if (SettingsPanelManager.Instance != null && SettingsPanelManager.Instance.IsOpen) return;
        if (DeviceAddressPopup.Instance != null && DeviceAddressPopup.Instance.IsOpen) return;

        // While the type popup is open, let clicks go to its UI buttons instead
        // of the 3D scene.
        if (PacketTypePickerPopup.Instance != null && PacketTypePickerPopup.Instance.IsOpen)
        {
            if (Mouse.current.rightButton.wasPressedThisFrame)
                Cancel();
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI())
            TrySelect();

        if (Mouse.current.rightButton.wasPressedThisFrame)
            Cancel();

        if (sourceDevice != null && queuedDestinations.Count > 0 && Keyboard.current != null &&
            (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            SendAll();
    }

    static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    void TrySelect()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return;

        var device = hit.collider.GetComponentInParent<NetworkDevice>();
        if (device == null || device.isPaletteItem) return;

        var addr = device.GetComponent<DeviceAddress>();
        if (addr == null || !addr.HasAddress)
        {
            NotificationManager.Instance?.Notify(
                "That device has no IP address yet — right-click a PC to configure one.", NotifyKind.Warning);
            return;
        }

        if (sourceDevice == null)
        {
            // sourceDevice is only committed once a type is actually picked —
            // dismissing the popup without picking (click-outside) just leaves
            // us waiting for a fresh source click, no stale half-armed state.
            PacketTypePickerPopup.Instance.Show(Mouse.current.position.ReadValue(), type =>
            {
                sourceDevice = device;
                pendingType  = type;
                HighlightDevice(device, true);
            });
        }
        else if (device != sourceDevice)
        {
            ToggleDestination(device);
        }
    }

    void ToggleDestination(NetworkDevice device)
    {
        int idx = queuedDestinations.IndexOf(device);
        if (idx >= 0)
        {
            queuedDestinations.RemoveAt(idx);
            HighlightDevice(device, false);
        }
        else
        {
            queuedDestinations.Add(device);
            HighlightDevice(device, true);
        }
    }

    void SendAll()
    {
        NetworkDevice source = sourceDevice;
        PacketType type = pendingType;
        var destinations = new List<NetworkDevice>(queuedDestinations);
        Cancel(); // reset selection before reporting results

        List<string> failures = PacketManager.Instance?.TrySendMulti(source, destinations, type)
                                 ?? new List<string>();
        int failed = failures.Count;
        int sent = destinations.Count - failed;

        if (failed == 0)
            NotificationManager.Instance?.Notify(
                $"Sending {type} to {sent} device{(sent == 1 ? "" : "s")}...", NotifyKind.Info);
        else if (sent == 0)
            NotificationManager.Instance?.Notify(
                $"Send failed for all {failed} device{(failed == 1 ? "" : "s")}.", NotifyKind.Danger);
        else
            NotificationManager.Instance?.Notify(
                $"Sent to {sent} device{(sent == 1 ? "" : "s")}, {failed} failed.", NotifyKind.Warning);

        AudioManager.Instance?.PlaySfx(sent > 0 ? SfxId.UIClick : SfxId.Fail);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void BeginSending()
    {
        if (isSending) { Cancel(); return; } // toggle off

        InteractionMode.CancelOthers(this);

        isSending    = true;
        sourceDevice = null;
    }

    public void CancelSending() => Cancel();

    void Cancel()
    {
        if (sourceDevice != null) HighlightDevice(sourceDevice, false);
        foreach (var d in queuedDestinations) HighlightDevice(d, false);
        queuedDestinations.Clear();
        sourceDevice = null;
        isSending = false;
        if (PacketTypePickerPopup.Instance != null) PacketTypePickerPopup.Instance.Hide();
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
            string prop = r.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            Color orig = r.sharedMaterial.GetColor(prop);
            s_propBlock.Clear();
            s_propBlock.SetColor(prop, Color.Lerp(orig, GameTheme.Info, 0.5f));
            r.SetPropertyBlock(s_propBlock);
        }
    }
}
