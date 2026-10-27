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

    // Bottom-of-screen line while a successful ping's packet is traveling
    // (ActivitySubtitle animates the trailing dots itself). A failed ping has
    // no travel phase — its result is instant — so it never posts one.
    const string PingingSubtitle = "PINGING";

    bool isPinging;
    NetworkDevice sourceDevice;
    PingSelectBadge sourceBadge;
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
        if (InteractionMode.IsSuspended) return;
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
            AudioManager.Instance?.PlaySfx(SfxId.Refusal);
            return;
        }

        if (sourceDevice == null)
        {
            sourceDevice = device;
            HighlightDevice(device, true);
            AudioManager.Instance?.PlaySfx(SfxId.Tagging);
            // A floating green ping badge over it, and its monitor's looping
            // radar screen, until the ping fires or the pick is cancelled.
            sourceBadge = PingSelectBadge.Attach(device, outgoing: true);
            PCMonitor.SetStandby(device, GameTheme.Success, PCMonitor.ScreenGlyph.Ping, outgoing: true);
        }
        else if (device != sourceDevice)
        {
            NetworkDevice dest = device;
            NetworkDevice source = sourceDevice;
            PingSelectBadge badge = sourceBadge;
            sourceBadge = null; // handed to RunPing — it stays up while the ping travels, so Cancel mustn't remove it
            Cancel(); // reset selection state before running/reporting the result
            RunPing(source, dest, badge);
        }
    }

    // srcBadge is the source's ping badge from TrySelect — the destination
    // gets its own (rings closing in) and both stay up until the ping lands,
    // where PingCheckmarkEffect's tick pops in at the same height. A failed
    // ping has no journey to wait for, and its cross pops in right away.
    void RunPing(NetworkDevice source, NetworkDevice dest, PingSelectBadge srcBadge)
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
            PingSelectBadge destBadge = PingSelectBadge.Attach(dest, outgoing: false);
            AudioManager.Instance?.PlaySfx(SfxId.Tagging); // a failed ping skips this — PingError below says it instead
            PingPacketEffect journey = PingPacketEffect.Play(source, dest, result.Path, GameTheme.Success, onLanded: () =>
            {
                PingSelectBadge.Remove(srcBadge);
                PingSelectBadge.Remove(destBadge);
            });

            // Keyed to the journey itself rather than to this manager: pings
            // can overlap (a second one fired while the first is still
            // traveling) and each should hold the subtitle up for its own
            // trip, and ActivitySubtitle drops a line on its own once its
            // owner object is destroyed — which the journey does the moment
            // the orb hits the destination or the path breaks — so there's no
            // matching Hide to keep in sync.
            if (journey != null) ActivitySubtitle.Instance?.Show(journey, PingingSubtitle);

            string hops = result.HopCount == 1 ? "1 hop" : $"{result.HopCount} hops";
            NotificationManager.Instance?.Notify($"Ping succeeded — {hops}.", NotifyKind.Success);
            // No success sound here — PingPacketEffect plays it the moment
            // the packet actually lands on the destination.
        }
        else
        {
            PingSelectBadge.Remove(srcBadge);
            PacketDeviceEffect.Play(source, GameTheme.Danger);
            PacketDeviceEffect.Play(dest, GameTheme.Danger);
            PCMonitor.PlayError(source);
            PCMonitor.PlayError(dest);

            // No top-of-screen banner for a failed ping anymore — instead the
            // same "materialize above the device" payoff the success case gets
            // (PingCheckmarkEffect), just red with CrossIconBuilder's X instead
            // of a tick. Played on both ends rather than just the destination:
            // a failed ping has no walked Path to single out one side of (see
            // ReachabilityService.Fail), so both already get equal Danger
            // feedback above via PacketDeviceEffect.
            Sprite cross = PingCheckmarkEffect.FailureGlyph;
            PingCheckmarkEffect.Play(source, GameTheme.Danger, cross);
            PingCheckmarkEffect.Play(dest, GameTheme.Danger, cross);

            AudioManager.Instance?.PlaySfx(SfxId.PingError);
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
        if (sourceDevice != null)
        {
            HighlightDevice(sourceDevice, false);
            PCMonitor.ClearStandby(sourceDevice);
        }
        PingSelectBadge.Remove(sourceBadge);
        sourceBadge = null;
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
            // Same guards as PacketSendManager.HighlightDevice — a renderer
            // with no material, or one with neither color property, is
            // skipped rather than throwing mid-click.
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
