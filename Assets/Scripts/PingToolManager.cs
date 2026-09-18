using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// Ping interaction mode — mirrors CableManager's click-source/click-target
/// shape, but instead of drawing a cable it runs ReachabilityService and
/// pulses the result along whatever cables the ping actually walked.
/// </summary>
public class PingToolManager : MonoBehaviour
{
    public static PingToolManager Instance { get; private set; }

    public bool IsPinging => isPinging;

    // Fired once per RunPing, after the result is computed — lets level
    // objective providers (see Level2Guide, Level3Guide) evaluate ping-based tasks
    // without polling, same idiom as CableManager.OnCableCreated.
    public event Action<NetworkDevice, NetworkDevice, ReachabilityService.PingResult> OnPingCompleted;

    bool isPinging;
    NetworkDevice sourceDevice;
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
    }

    void Update()
    {
        if (!isPinging) return;
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return;
        if (SettingsPanelManager.Instance != null && SettingsPanelManager.Instance.IsOpen) return;
        if (DeviceAddressPopup.Instance != null && DeviceAddressPopup.Instance.IsOpen) return;

        if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI())
            TrySelect();

        if (Mouse.current.rightButton.wasPressedThisFrame)
            Cancel();
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
            sourceDevice = device;
            HighlightDevice(device, true);
        }
        else if (device != sourceDevice)
        {
            NetworkDevice dest = device;
            NetworkDevice source = sourceDevice;
            Cancel(); // reset selection state before running/reporting the result
            RunPing(source, dest);
        }
    }

    void RunPing(NetworkDevice source, NetworkDevice dest)
    {
        ReachabilityService.PingResult result = ReachabilityService.Ping(source, dest);

        foreach (Cable cable in result.Path)
            cable.SetPulse(result.Success);

        if (result.Success)
        {
            // A real traveling packet — condenses out of the source, hops
            // cable-to-cable through whatever switches/routers the path
            // actually crosses, and explodes into the destination. See
            // PingPacketEffect for the full story; it also covers the
            // source/destination touch feedback the instant dual bounce used
            // to provide below — a failed ping has no walked Path to
            // animate, so it keeps that instant feedback instead.
            PingPacketEffect.Play(source, dest, result.Path, GameTheme.Success);

            string hops = result.HopCount == 1 ? "1 hop" : $"{result.HopCount} hops";
            NotificationManager.Instance?.Notify($"Ping succeeded — {hops}.", NotifyKind.Success);
            AudioManager.Instance?.PlaySfx(SfxId.Success);
        }
        else
        {
            PacketDeviceEffect.Play(source, GameTheme.Danger);
            PacketDeviceEffect.Play(dest, GameTheme.Danger);

            // No top-of-screen banner for a failed ping anymore — instead the
            // same "materialize above the device" payoff the success case gets
            // (PingCheckmarkEffect), just red with CrossIconBuilder's X instead
            // of a tick. Played on both ends rather than just the destination:
            // a failed ping has no walked Path to single out one side of (see
            // ReachabilityService.Fail), so both already get equal Danger
            // feedback above via PacketDeviceEffect.
            Sprite cross = CrossIconBuilder.GetSprite(Color.white);
            PingCheckmarkEffect.Play(source, GameTheme.Danger, cross);
            PingCheckmarkEffect.Play(dest, GameTheme.Danger, cross);

            AudioManager.Instance?.PlaySfx(SfxId.Fail);
        }

        OnPingCompleted?.Invoke(source, dest, result);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void BeginPinging()
    {
        if (isPinging) { Cancel(); return; } // toggle off

        InteractionMode.CancelOthers(this);

        isPinging    = true;
        sourceDevice = null;
    }

    public void CancelPinging() => Cancel();

    void Cancel()
    {
        if (sourceDevice != null) HighlightDevice(sourceDevice, false);
        sourceDevice = null;
        isPinging = false;
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
