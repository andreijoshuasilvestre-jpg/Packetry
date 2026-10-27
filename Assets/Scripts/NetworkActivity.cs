using UnityEngine;

/// <summary>
/// Whether any traffic is still playing out on screen — a Run Network packet
/// (PacketManager) or a ping (PingPacketEffect) traveling, or one that only
/// just landed and is still showing its arrival beat (the destination
/// monitor's flash, then PingCheckmarkEffect's icon pop). Lets anything that
/// would take over the screen — LevelCompletePanel — hold off until the
/// player has actually watched the traffic that finished the level arrive,
/// instead of each level guessing a fixed delay.
///
/// Report-in rather than polled: a traffic source calls MarkActive() every
/// frame it has something in flight, and IsBusy stays true for
/// LandingDuration past the last such frame. A new kind of traffic only has
/// to call MarkActive — nothing that waits on IsBusy has to change — and
/// there's no count to leak if a device or cable is removed mid-flight.
/// </summary>
public static class NetworkActivity
{
    // Covers the arrival beat after the last packet/orb touches down:
    // squash-out (0.08s) + PCMonitor flash-in (0.14s) + the type icon's
    // pop-in (0.22s), plus a moment for it to actually register.
    const float LandingDuration = 1.0f;

    static float lastActiveTime = float.NegativeInfinity;
    static int   lastActiveFrame = int.MinValue;

    public static bool IsBusy => Time.time < lastActiveTime + AnimationSpeed.Seconds(AnimationKind.Packets, LandingDuration);

    /// <summary>Something is actually traveling right now — IsBusy without the
    /// landing window. Counts last frame's mark too, so it reads the same from
    /// any script regardless of Update order (PacketManager's travel sound).</summary>
    public static bool IsInFlight => Time.frameCount - lastActiveFrame <= 1;

    public static void MarkActive()
    {
        lastActiveTime  = Time.time;
        lastActiveFrame = Time.frameCount;
    }

    // Time.time restarts every Play session — a stamp left over from a
    // previous one (no domain reload) would otherwise read as busy for ages.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        lastActiveTime  = float.NegativeInfinity;
        lastActiveFrame = int.MinValue;
    }
}
