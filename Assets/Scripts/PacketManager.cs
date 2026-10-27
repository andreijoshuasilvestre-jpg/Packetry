using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns every in-flight Packet: player-triggered spawning (TrySendMulti,
/// called by PacketSendManager, which splits a shared route prefix into one
/// trunk packet that fans out into per-destination packets — see SplitPacket),
/// the reactive HttpRequest-to-HttpResponse round trip, and delivery/drop
/// outcome events. Movement reuses Cable.SampleCurve/PathLength
/// per segment; outcome feedback reuses Cable.SetPulse, the exact idiom
/// PingToolManager already uses for ping results.
/// </summary>
public class PacketManager : MonoBehaviour
{
    public static PacketManager Instance { get; private set; }

    public event Action<Packet> OnPacketDelivered;
    public event Action<Packet> OnPacketDropped;

    // Departure wipe timing — mirrors PingPacketEffect's own SourceGlow*
    // constants so a regular transfer's touch-the-device beat reads with the
    // same pacing a ping's does. Arrival timing now lives in PingWipeEffect's
    // own NotifyArrival/RunSustained (see PlayDeliveryEffects).
    const float SourceGlowBuild = 0.16f;
    const float SourceGlowHold  = 0.05f;
    const float SourceGlowFade  = 0.22f;

    private readonly List<Packet> activePackets = new();

    // Which cables showed a guide-dot flow last frame — diffed against this
    // frame's set so cables that no longer carry any in-flight packet get an
    // explicit SetFlowing(false) instead of being left stuck on.
    private readonly HashSet<Cable> flowingLastFrame = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // One travel loop for all traffic — Run Network packets and pings alike
    // (PingPacketEffect reports into NetworkActivity too) — sounding exactly
    // while something's in flight. LateUpdate so every traffic source has
    // already reported in for this frame.
    void LateUpdate()
    {
        if (NetworkActivity.IsInFlight) AudioManager.Instance?.StartLoop(SfxId.PacketTravel);
        else                            AudioManager.Instance?.StopLoop(SfxId.PacketTravel);
    }

    void OnDisable() => AudioManager.Instance?.StopLoop(SfxId.PacketTravel);

    void Update()
    {
        // Checked before the loop below removes anything, so the frame the
        // last packet lands still counts — NetworkActivity's landing window
        // runs from here.
        if (activePackets.Count > 0) NetworkActivity.MarkActive();

        var flowingNow = new Dictionary<Cable, (bool reversed, Color tint)>();

        for (int i = activePackets.Count - 1; i >= 0; i--)
        {
            Packet p = activePackets[i];
            Cable cable = p.Path[p.HopIndex];
            NetworkDevice from = p.Waypoints[p.HopIndex];

            if (!IsHopValid(cable, from))
            {
                activePackets.RemoveAt(i);
                Finish(p, delivered: false);
                continue;
            }

            p.T += PacketVisual.SpeedFor(p.Type) * AnimationSpeed.Delta(AnimationKind.Packets) / Mathf.Max(0.0001f, cable.PathLength);

            if (p.T >= 1f)
            {
                if (p.HopIndex + 1 >= p.Path.Count)
                {
                    activePackets.RemoveAt(i);
                    if (p.PendingBranches != null && p.PendingBranches.Count > 0)
                        SplitPacket(p);
                    else
                        Finish(p, delivered: true);
                    continue;
                }

                p.HopIndex++;
                p.T = 0f;
                cable = p.Path[p.HopIndex];
                from  = p.Waypoints[p.HopIndex];
                PacketDeviceEffect.Play(from, p.Tint);
                AudioManager.Instance?.PlaySfx(SfxId.PacketHop);
            }

            bool reversed = cable.DeviceA != from;
            float sampleT = reversed ? 1f - p.T : p.T;
            p.Visual.position = cable.SampleCurve(Mathf.Clamp01(sampleT));

            // Last write for a given cable wins if two packets share a segment
            // this frame — the guide dots are a cosmetic hint, not a precise
            // per-packet readout, so that's an acceptable simplification.
            flowingNow[cable] = (reversed, p.Tint);
        }

        foreach (var cable in flowingLastFrame)
            if (cable != null && !flowingNow.ContainsKey(cable))
                cable.SetFlowing(false);

        foreach (var kv in flowingNow)
            kv.Key.SetFlowing(true, kv.Value.reversed, kv.Value.tint);

        flowingLastFrame.Clear();
        flowingLastFrame.UnionWith(flowingNow.Keys);

        // Re-derived from activePackets every frame rather than shown/hidden
        // at spawn/finish — every way a packet leaves the list (delivered,
        // dropped by a broken hop, a device deleted mid-flight) clears it
        // with no extra bookkeeping, and an HttpRequest's auto-spawned
        // response lands in the list during this same loop (see Finish), so
        // the line hands straight over from request to response instead of
        // flickering off for a frame.
        ActivitySubtitle.Instance?.Show(this, InFlightActivityLabel());
    }

    // ── Activity subtitle ────────────────────────────────────────────────────

    const string MixedActivityLabel = "SENDING PACKETS";

    /// <summary>What the bottom-of-screen ActivitySubtitle should say about
    /// the packets currently in flight — that type's own line if they're all
    /// the same type, a generic one if they're mixed, null (hide) if none.</summary>
    string InFlightActivityLabel()
    {
        if (activePackets.Count == 0) return null;

        PacketType type = activePackets[0].Type;
        for (int i = 1; i < activePackets.Count; i++)
            if (activePackets[i].Type != type) return MixedActivityLabel;

        return ActivityLabelFor(type);
    }

    static string ActivityLabelFor(PacketType type) => type switch
    {
        PacketType.HttpRequest  => "SENDING HTTP REQUEST",
        PacketType.HttpResponse => "SENDING HTTP RESPONSE",
        PacketType.FileTransfer => "TRANSFERRING FILE",
        _                       => MixedActivityLabel, // a newly added type still gets a sensible line until it's given its own
    };

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Player-triggered send to one or more destinations at once.
    /// Returns the user-facing failure reason paired with which destination it
    /// applies to, for each destination that couldn't be sent to (empty if all
    /// succeeded) — the pairing lets the caller (PacketSendManager) play a
    /// per-device failure effect rather than just a blind count. All
    /// destinations that pass validation share a single visual "trunk" packet
    /// out of source for whatever route prefix they all have in common (often
    /// the source's one uplink cable); it splits into one packet per
    /// destination the moment their routes diverge, rather than looking like N
    /// independent sends from frame one.</summary>
    public List<(NetworkDevice dest, string reason)> TrySendMulti(NetworkDevice source, List<NetworkDevice> destinations, PacketType type)
    {
        var failures = new List<(NetworkDevice dest, string reason)>();
        var direct = new List<(NetworkDevice dest, List<Cable> path)>();

        foreach (var dest in destinations)
        {
            if (source == null || dest == null) { failures.Add((dest, "Invalid device.")); continue; }
            if (source == dest) { failures.Add((dest, "Source and destination must be different devices.")); continue; }

            var srcAddr = source.GetComponent<DeviceAddress>();
            var dstAddr = dest.GetComponent<DeviceAddress>();
            if (srcAddr == null || !srcAddr.HasAddress) { failures.Add((dest, "Source has no IP address configured.")); continue; }
            if (dstAddr == null || !dstAddr.HasAddress) { failures.Add((dest, "Destination has no IP address configured.")); continue; }

            ReachabilityService.PingResult result = ReachabilityService.Ping(source, dest);
            if (!result.Success) { failures.Add((dest, result.FailureReason)); continue; }
            direct.Add((dest, result.Path));
        }

        if (direct.Count == 0) return failures;

        if (direct.Count == 1)
        {
            string failure = SpawnWithPath(source, direct[0].dest, type, direct[0].path, null, null);
            if (failure != null) failures.Add((direct[0].dest, failure));
            return failures;
        }

        int commonLen = CommonPrefixLength(direct[0].path, direct[1].path);
        for (int i = 2; i < direct.Count; i++)
            commonLen = Mathf.Min(commonLen, CommonPrefixLength(direct[0].path, direct[i].path));

        if (commonLen == 0)
        {
            // No shared first hop (e.g. a source with several independent
            // uplinks) — nothing to visually trunk, so each destination just
            // gets its own packet straight away.
            foreach (var (dest, path) in direct)
            {
                string failure = SpawnWithPath(source, dest, type, path, null, null);
                if (failure != null) failures.Add((dest, failure));
            }
            return failures;
        }

        var trunkPath = direct[0].path.GetRange(0, commonLen);
        var branches = new List<PacketBranch>(direct.Count);
        foreach (var (dest, path) in direct)
        {
            List<NetworkDevice> fullWaypoints = BuildWaypoints(source, path);
            branches.Add(new PacketBranch
            {
                Destination = dest,
                Path        = path.GetRange(commonLen, path.Count - commonLen),
                Waypoints   = fullWaypoints.GetRange(commonLen, fullWaypoints.Count - commonLen),
            });
        }

        string trunkFailure = SpawnWithPath(source, null, type, trunkPath, null, branches);
        if (trunkFailure != null)
            foreach (var (dest, _) in direct) failures.Add((dest, trunkFailure)); // whole batch blocked at the shared first hop

        return failures;
    }

    static int CommonPrefixLength(List<Cable> a, List<Cable> b)
    {
        int n = Mathf.Min(a.Count, b.Count);
        int i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    // ── Spawning ─────────────────────────────────────────────────────────────

    string Spawn(NetworkDevice source, NetworkDevice dest, PacketType type, Packet linkedRequest = null)
    {
        ReachabilityService.PingResult result = ReachabilityService.Ping(source, dest);
        if (!result.Success) return result.FailureReason;
        return SpawnWithPath(source, dest, type, result.Path, linkedRequest, null);
    }

    string SpawnWithPath(NetworkDevice source, NetworkDevice dest, PacketType type, List<Cable> path,
                          Packet linkedRequest, List<PacketBranch> pendingBranches)
    {
        var srcAddr = source.GetComponent<DeviceAddress>();
        Color tint = GameTheme.SubnetTint(SubnetMath.SubnetOf(srcAddr.Ip, srcAddr.Mask));

        // Same beat PingPacketEffect opens its journey with — the source
        // glows uniformly (screen-scoped on a PC, see PingWipeEffect) right
        // as the packet forms, tinted by this send's PacketType rather than
        // subnet so "this is an HTTP request" / "this is a file" reads at a
        // glance even before the traveling icon is visible. Skipped for an
        // auto-generated HttpResponse (linkedRequest != null) — that source
        // is the device that JUST played its own arrival glow receiving the
        // request a moment ago, so charging up again here read as the same
        // wipe firing twice in a row rather than two distinct events.
        Color typeColor = PacketVisual.ColorFor(type);
        if (linkedRequest == null && !PCMonitor.PlaySend(source, typeColor, PacketVisual.ScreenGlyphFor(type)))
        {
            // Fallback for a source without a monitor — a PC flashes its
            // screen and shows the type's glyph instead (see PCMonitor).
            int exitPortIndex = path.Count > 0 ? source.GetPortIndexForCable(path[0]) : -1;
            Vector3 exitPoint = exitPortIndex >= 0 ? source.GetPortWorldPoint(exitPortIndex) : source.VisualTopPoint();
            PingWipeEffect.PlayGlowPulse(source, exitPoint, typeColor, SourceGlowBuild, SourceGlowHold, SourceGlowFade);
        }

        var packet = new Packet
        {
            Type            = type,
            Source          = source,
            Destination     = dest,
            Path            = path,
            Waypoints       = BuildWaypoints(source, path),
            HopIndex        = 0,
            T               = 0f,
            Tint            = tint,
            LinkedRequest   = linkedRequest,
            PendingBranches = pendingBranches,
            Visual          = PacketVisual.Create(type, tint, transform),
        };
        activePackets.Add(packet);
        PacketDeviceEffect.Play(source, tint);
        return null;
    }

    // A trunk packet reaching the end of its shared prefix doesn't deliver —
    // it fans out into one child Packet per remaining destination, each
    // continuing from here along its own Path/Waypoints tail. A branch whose
    // Path is already empty means that destination WAS the split point, so it
    // delivers immediately instead of spawning a zero-length mover.
    void SplitPacket(Packet trunk)
    {
        if (trunk.Visual != null) Destroy(trunk.Visual.gameObject);
        PacketDeviceEffect.Play(trunk.Waypoints[trunk.Waypoints.Count - 1], trunk.Tint);

        foreach (var branch in trunk.PendingBranches)
        {
            if (branch.Path.Count == 0)
            {
                Finish(new Packet
                {
                    Type = trunk.Type, Source = trunk.Source, Destination = branch.Destination,
                    Path = branch.Path, Waypoints = branch.Waypoints, Tint = trunk.Tint,
                }, delivered: true);
                continue;
            }

            var child = new Packet
            {
                Type        = trunk.Type,
                Source      = trunk.Source,
                Destination = branch.Destination,
                Path        = branch.Path,
                Waypoints   = branch.Waypoints,
                HopIndex    = 0,
                T           = 0f,
                Tint        = trunk.Tint,
                Visual      = PacketVisual.Create(trunk.Type, trunk.Tint, transform),
            };
            activePackets.Add(child);
        }
    }

    // Public so PingPacketEffect can reuse the exact same source->destination
    // device sequence for its own hop-by-hop travel instead of re-deriving it.
    public static List<NetworkDevice> BuildWaypoints(NetworkDevice source, List<Cable> path)
    {
        var wp = new List<NetworkDevice>(path.Count + 1) { source };
        NetworkDevice cur = source;
        foreach (var cable in path)
        {
            cur = cable.DeviceA == cur ? cable.DeviceB : cable.DeviceA;
            wp.Add(cur);
        }
        return wp;
    }

    // A router interface can be shut down mid-flight via RouterCLI — treat
    // that as the path breaking, the same way ReachabilityService would refuse
    // it if this packet were spawned fresh right now. Switches never carry an
    // "active" flag of their own (only PCs/routers do), so this deliberately
    // only inspects router endpoints rather than a blanket per-cable check.
    static bool IsHopValid(Cable cable, NetworkDevice from)
    {
        if (cable == null || cable.DeviceA == null || cable.DeviceB == null) return false;

        // GetPortIndexForCable can return -1 the instant a cable is removed
        // (CableRemoverManager clears the port's own cable reference before the
        // Cable component's GameObject actually finishes destroying), so this
        // must treat "not found" as invalid rather than indexing IsPortActive
        // with -1.
        if (from.deviceType == DeviceType.Router)
        {
            int fromPort = from.GetPortIndexForCable(cable);
            if (fromPort < 0 || !from.IsPortActive(fromPort)) return false;
        }

        NetworkDevice to = cable.DeviceA == from ? cable.DeviceB : cable.DeviceA;
        if (to.deviceType == DeviceType.Router)
        {
            int toPort = to.GetPortIndexForCable(cable);
            if (toPort < 0 || !to.IsPortActive(toPort)) return false;
        }

        return true;
    }

    // ── Outcome ──────────────────────────────────────────────────────────────

    void Finish(Packet p, bool delivered)
    {
        p.State = delivered ? PacketState.Delivered : PacketState.Dropped;

        // Deliberately no cable.SetPulse() here — that flash used to linger
        // for pulseDuration (2.2s) on every cable the packet had crossed, so
        // the guide dots kept animating well after the transfer was actually
        // done. The dots already disappear immediately once this packet stops
        // contributing to Update()'s flowingNow map (see below); that's the
        // only signal a finished transfer should leave behind.
        if (delivered)
        {
            StartCoroutine(PlayDeliveryEffects(p));

            // The response is routed fresh, receiver back to sender — if the
            // network changed while the request was in flight (a cable
            // pulled, an interface shut down, a device removed) that can
            // fail. Treat it like any other drop rather than the reply just
            // silently never appearing.
            if (p.Type == PacketType.HttpRequest &&
                Spawn(p.Destination, p.Source, PacketType.HttpResponse, p) != null)
            {
                PacketFailureEffect.Play(p.Destination, p.Source);
                OnPacketDropped?.Invoke(new Packet
                {
                    Type = PacketType.HttpResponse, State = PacketState.Dropped,
                    Source = p.Destination, Destination = p.Source, LinkedRequest = p,
                    Path = new List<Cable>(), Waypoints = new List<NetworkDevice>(),
                });
            }

            OnPacketDelivered?.Invoke(p);
        }
        else
        {
            if (p.Visual != null) Destroy(p.Visual.gameObject);
            // Both ends show the error. A trunk packet's Destination is null
            // (see Packet.PendingBranches' doc comment) — no single receiver
            // to blame a shared-prefix drop on, so only its sender shows it.
            PacketFailureEffect.Play(p.Source, p.Destination);
            OnPacketDropped?.Invoke(p);
        }
    }

    // Mirrors PingPacketEffect's own arrival beat: squash the traveling body
    // flat (PacketTravelEffect.PlaySquashOut) instead of just vanishing it,
    // then notify the destination's screen glow and, once it's lit, pop the
    // delivered PacketType's own icon (globe for HTTP, folder for a file) on
    // top — the packet-transfer counterpart to PingCheckmarkEffect's plain
    // success tick, colored/shaped per type instead. PingCheckmarkEffect
    // already accepts an arbitrary icon Sprite, so this reuses it as-is
    // rather than duplicating the pop-in/bob/fade. Uses NotifyArrival, not a
    // fresh PlayForward every time — several packets landing on the same
    // device in a burst (multiple senders targeting one PC, or a multi-hop
    // File Transfer) coalesce into one continuous glow instead of each
    // packet retriggering its own sweep-in/fade-out.
    IEnumerator PlayDeliveryEffects(Packet p)
    {
        if (p.Visual != null)
        {
            var travel = p.Visual.GetComponent<PacketTravelEffect>();
            if (travel != null) yield return travel.PlaySquashOut();
            if (p.Visual != null) Destroy(p.Visual.gameObject);
        }

        NetworkDevice dest = p.Waypoints[p.Waypoints.Count - 1];
        if (dest == null) yield break;

        Cable lastCable = p.Path.Count > 0 ? p.Path[p.Path.Count - 1] : null;
        int entryPortIndex = lastCable != null ? dest.GetPortIndexForCable(lastCable) : -1;
        Vector3 entryPoint = entryPortIndex >= 0 ? dest.GetPortWorldPoint(entryPortIndex) : dest.VisualTopPoint();

        Color typeColor = PacketVisual.ColorFor(p.Type);
        Action onPeak = () =>
        {
            PacketDeviceEffect.Play(dest, typeColor, PacketDeviceEffect.PlacementIntensity);
            PingCheckmarkEffect.Play(dest, typeColor, PacketVisual.IconFor(p.Type));
            AudioManager.Instance?.PlaySfx(SfxId.PacketSuccess);
        };
        // A PC's monitor coalesces a burst of arrivals itself (PCMonitor.
        // PlayReceive), same as NotifyArrival does for the fallback wipe.
        if (!PCMonitor.PlayReceive(dest, typeColor, PacketVisual.ScreenGlyphFor(p.Type), onPeak))
            PingWipeEffect.NotifyArrival(dest, entryPoint, typeColor, onPeak);
    }
}
