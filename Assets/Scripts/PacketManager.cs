using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns every in-flight Packet: player-triggered spawning (TrySendMulti,
/// called by PacketSendManager, which splits a shared route prefix into one
/// trunk packet that fans out into per-destination packets — see SplitPacket),
/// the reactive HttpRequest-to-HttpResponse round trip, per-cable in-flight
/// capacity (Factorio-belt-style backpressure — a packet that can't advance
/// holds at the end of its current segment instead of being dropped), and
/// delivery/drop outcome events. Movement reuses Cable.SampleCurve/PathLength
/// per segment; outcome feedback reuses Cable.SetPulse, the exact idiom
/// PingToolManager already uses for ping results.
/// </summary>
public class PacketManager : MonoBehaviour
{
    public static PacketManager Instance { get; private set; }

    public event Action<Packet> OnPacketDelivered;
    public event Action<Packet> OnPacketDropped;

    [SerializeField] private int maxInFlightPerCable = 4;

    // Departure/arrival wipe timings — mirror PingPacketEffect's own
    // SourceGlow*/DestWipeDuration constants so a regular transfer's
    // touch-the-device beats read with the same pacing a ping's do.
    const float SourceGlowBuild      = 0.16f;
    const float SourceGlowHold       = 0.05f;
    const float SourceGlowFade       = 0.22f;
    const float DeliveryWipeDuration = 0.4f;

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

    void Update()
    {
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

            p.T += PacketVisual.SpeedFor(p.Type) * Time.deltaTime / Mathf.Max(0.0001f, cable.PathLength);

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

                Cable nextCable = p.Path[p.HopIndex + 1];
                int weight = PacketVisual.CapWeightFor(p.Type);
                if (Occupancy(nextCable) + weight > maxInFlightPerCable)
                {
                    p.T = 1f; // hold at the tail of the current segment until a slot frees — congestion, not a drop
                }
                else
                {
                    p.HopIndex++;
                    p.T = 0f;
                    cable = p.Path[p.HopIndex];
                    from  = p.Waypoints[p.HopIndex];
                    PacketDeviceEffect.Play(from, p.Tint);
                }
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
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Player-triggered send to one or more destinations at once.
    /// Returns the user-facing failure reason for each destination that
    /// couldn't be sent to (empty if all succeeded). All destinations that
    /// pass validation share a single visual "trunk" packet out of source for
    /// whatever route prefix they all have in common (often the source's one
    /// uplink cable); it splits into one packet per destination the moment
    /// their routes diverge, rather than looking like N independent sends
    /// from frame one.</summary>
    public List<string> TrySendMulti(NetworkDevice source, List<NetworkDevice> destinations, PacketType type)
    {
        var failures = new List<string>();
        var direct = new List<(NetworkDevice dest, List<Cable> path)>();

        foreach (var dest in destinations)
        {
            if (source == null || dest == null) { failures.Add("Invalid device."); continue; }
            if (source == dest) { failures.Add("Source and destination must be different devices."); continue; }

            var srcAddr = source.GetComponent<DeviceAddress>();
            var dstAddr = dest.GetComponent<DeviceAddress>();
            if (srcAddr == null || !srcAddr.HasAddress) { failures.Add("Source has no IP address configured."); continue; }
            if (dstAddr == null || !dstAddr.HasAddress) { failures.Add("Destination has no IP address configured."); continue; }

            ReachabilityService.PingResult result = ReachabilityService.Ping(source, dest);
            if (!result.Success) { failures.Add(result.FailureReason); continue; }
            direct.Add((dest, result.Path));
        }

        if (direct.Count == 0) return failures;

        if (direct.Count == 1)
        {
            string failure = SpawnWithPath(source, direct[0].dest, type, direct[0].path, null, null);
            if (failure != null) failures.Add(failure);
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
                if (failure != null) failures.Add(failure);
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
            foreach (var _ in direct) failures.Add(trunkFailure); // whole batch blocked at the shared first hop

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
        int weight = PacketVisual.CapWeightFor(type);
        if (path.Count > 0 && Occupancy(path[0]) + weight > maxInFlightPerCable)
            return "The first link on this route is already at capacity — try again shortly.";

        var srcAddr = source.GetComponent<DeviceAddress>();
        Color tint = GameTheme.SubnetTint(SubnetMath.NetworkAddress(srcAddr.Ip, srcAddr.Mask));

        // Same beat PingPacketEffect opens its journey with — the source
        // glows uniformly (screen-scoped on a PC, see PingWipeEffect) right
        // as the packet forms, tinted by this send's PacketType rather than
        // subnet so "this is an HTTP request" / "this is a file" reads at a
        // glance even before the traveling icon is visible.
        Color typeColor = PacketVisual.ColorFor(type);
        int exitPortIndex = path.Count > 0 ? source.GetPortIndexForCable(path[0]) : -1;
        Vector3 exitPoint = exitPortIndex >= 0 ? source.GetPortWorldPoint(exitPortIndex) : source.VisualTopPoint();
        PingWipeEffect.PlayGlowPulse(source, exitPoint, typeColor, SourceGlowBuild, SourceGlowHold, SourceGlowFade);

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
    // delivers immediately instead of spawning a zero-length mover; a branch
    // whose first hop is already at capacity drops the same way a fresh single
    // send would.
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

            int weight = PacketVisual.CapWeightFor(trunk.Type);
            if (Occupancy(branch.Path[0]) + weight > maxInFlightPerCable)
            {
                Finish(new Packet
                {
                    Type = trunk.Type, Source = trunk.Source, Destination = branch.Destination,
                    Path = branch.Path, Waypoints = branch.Waypoints,
                }, delivered: false);
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

    int Occupancy(Cable cable)
    {
        int sum = 0;
        foreach (var p in activePackets)
            if (p.HopIndex < p.Path.Count && p.Path[p.HopIndex] == cable)
                sum += PacketVisual.CapWeightFor(p.Type);
        return sum;
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

            if (p.Type == PacketType.HttpRequest)
                Spawn(p.Destination, p.Source, PacketType.HttpResponse, p);

            OnPacketDelivered?.Invoke(p);
        }
        else
        {
            if (p.Visual != null) Destroy(p.Visual.gameObject);
            OnPacketDropped?.Invoke(p);
        }
    }

    // Mirrors PingPacketEffect's own arrival beat: squash the traveling body
    // flat (PacketTravelEffect.PlaySquashOut) instead of just vanishing it,
    // then sweep a forward wipe across the destination and, once it fully
    // covers, pop the delivered PacketType's own icon (globe for HTTP, folder
    // for a file) on top — the packet-transfer counterpart to
    // PingCheckmarkEffect's plain success tick, colored/shaped per type
    // instead. PingCheckmarkEffect already accepts an arbitrary icon Sprite,
    // so this reuses it as-is rather than duplicating the pop-in/bob/fade.
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
        PingWipeEffect.PlayForward(dest, entryPoint, typeColor, DeliveryWipeDuration, () =>
        {
            PacketDeviceEffect.Play(dest, typeColor, PacketDeviceEffect.PlacementIntensity);
            PingCheckmarkEffect.Play(dest, typeColor, PacketVisual.IconFor(p.Type));
        });
    }
}
