using UnityEngine;

/// <summary>
/// Shared "something went wrong here" visual — a PC's monitor error screen
/// (PCMonitor.PlayError: red flash + cross), falling back to a red
/// PingWipeEffect glow-pulse on a device without a monitor. Every packet-send
/// failure funnels through here, played on both the sender and the receiver,
/// so a busy Run Network batch reads consistently regardless of which check
/// actually failed: bad address/reachability (PacketManager.TrySendMulti), a
/// disabled accept-flag (PacketSendManager.RunNetwork), or a mid-flight drop
/// (PacketManager.Finish).
/// </summary>
public static class PacketFailureEffect
{
    const float BuildDuration = 0.18f;
    const float HoldDuration  = 0.6f; // longer than the ping pulse's ~0.05s hold so it reads clearly amid a whole batch of sends
    const float FadeDuration  = 0.3f;

    /// <summary>A failed send, shown on both ends — the sender and the
    /// receiver it didn't reach — so the player sees which pair failed.
    /// Either may be null (a trunk packet that dropped before it split has no
    /// single receiver; a device may have been removed mid-flight).</summary>
    public static void Play(NetworkDevice sender, NetworkDevice receiver)
    {
        Play(sender);
        if (receiver != sender) Play(receiver);
        // Once per failed send, not per end — AudioManager also folds a whole
        // Run Network batch failing in the same frame into a single play.
        AudioManager.Instance?.PlaySfx(SfxId.PingError);
    }

    public static void Play(NetworkDevice device)
    {
        if (device == null) return;
        if (PCMonitor.PlayError(device)) return; // red flash + cross on the PC's monitor
        PingWipeEffect.PlayGlowPulse(device, device.VisualTopPoint(), GameTheme.Danger,
            BuildDuration, HoldDuration, FadeDuration);
    }
}
