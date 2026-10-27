using UnityEngine;

/// <summary>Which of the player's animation speed settings an animation follows.</summary>
public enum AnimationKind
{
    Packets, // pings and packet transfers: the traveling packet and every device/screen reaction to it
    Build,   // the player placing a device, table or cable (blueprint sweep, axis-grow, landing pop)
    Level,   // a level's entrance and exit: platform, walls, starting topology, HUD, camera zoom-out
}

/// <summary>
/// The player's animation speed settings (Settings → Animation Speed) — one
/// multiplier per AnimationKind, 1x to 3x in quarter steps, so a player who has
/// sat through the level entrance a dozen times can hurry it without also
/// rushing the packets they're still learning from. 1x is every animation's own
/// authored pace, so old saves play exactly as before.
///
/// An animation follows its setting by advancing its own timer with
/// Delta()/UnscaledDelta() instead of Time.deltaTime/unscaledDeltaTime, and
/// shortening each fixed wait with Seconds(). One that stamps when something
/// started and compares against "now" instead of stepping a timer (PCMonitor's
/// screen events) reads Clock() in place of Time.time.
/// </summary>
public static class AnimationSpeed
{
    public const float Min  = 1f;
    public const float Max  = 3f;
    public const float Step = 0.25f;

    public static float Get(AnimationKind kind)
    {
        var s = SaveManager.Settings;
        float value = kind switch
        {
            AnimationKind.Packets => s.packetAnimationSpeed,
            AnimationKind.Build   => s.buildAnimationSpeed,
            _                     => s.levelAnimationSpeed,
        };
        return Snap(value);
    }

    /// <summary>Stores a new multiplier (snapped onto the step grid) in memory —
    /// the caller saves, same as the other Settings controls.</summary>
    public static void Set(AnimationKind kind, float value)
    {
        var s = SaveManager.Settings;
        value = Snap(value);
        switch (kind)
        {
            case AnimationKind.Packets: s.packetAnimationSpeed = value; break;
            case AnimationKind.Build:   s.buildAnimationSpeed  = value; break;
            default:                    s.levelAnimationSpeed  = value; break;
        }
    }

    /// <summary>Nearest step inside Min..Max — also turns an old save's missing
    /// field (0) or a hand-edited value into something playable.</summary>
    public static float Snap(float value) => Mathf.Clamp(Mathf.Round(value / Step) * Step, Min, Max);

    /// <summary>"1x", "1.25x", "2x"...</summary>
    public static string Label(float multiplier) => multiplier.ToString("0.##") + "x";

    /// <summary>This frame's Time.deltaTime, sped up by kind's setting.</summary>
    public static float Delta(AnimationKind kind) => Time.deltaTime * Get(kind);

    /// <summary>This frame's Time.unscaledDeltaTime, sped up by kind's setting.</summary>
    public static float UnscaledDelta(AnimationKind kind) => Time.unscaledDeltaTime * Get(kind);

    /// <summary>A fixed duration or wait, shortened by kind's setting.</summary>
    public static float Seconds(AnimationKind kind, float seconds) => seconds / Get(kind);

    static readonly float[] s_clock = new float[3];
    static readonly float[] s_clockSampledAt = { -1f, -1f, -1f };

    /// <summary>A running time that advances Get(kind) times as fast as
    /// Time.time. Integrates from whenever it was last read, so frames nobody
    /// read it still count.</summary>
    public static float Clock(AnimationKind kind)
    {
        int i = (int)kind;
        float now = Time.time;
        float last = s_clockSampledAt[i];
        // now < last: a new Play session restarted Time.time while this static
        // survived (domain reload off) — carry on from where it was instead.
        if (last >= 0f && now >= last) s_clock[i] += (now - last) * Get(kind);
        s_clockSampledAt[i] = now;
        return s_clock[i];
    }
}
