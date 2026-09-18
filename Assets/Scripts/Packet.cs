using System.Collections.Generic;
using UnityEngine;

public enum PacketType  { HttpRequest, HttpResponse, FileTransfer }
public enum PacketState { InFlight, Delivered, Dropped }

/// <summary>
/// A single in-flight packet: a source/destination pair, the precomputed
/// multi-hop route between them (from ReachabilityService.Ping), and its
/// current position along that route. Plain data + a companion world-space
/// Visual — PacketManager drives all of it from one Update() loop, mirroring
/// how Cable already drives its own decorative packet dots from one loop
/// rather than a MonoBehaviour per dot.
/// </summary>
public class Packet
{
    public PacketType  Type;
    public PacketState State = PacketState.InFlight;

    public NetworkDevice Source;
    public NetworkDevice Destination;

    // The cables actually walked (ReachabilityService.PingResult.Path) and the
    // device standing at the start of each of them, Source..Destination —
    // Waypoints[i] is whichever device Path[i] was entered FROM, precomputed
    // once at spawn so per-frame movement never has to re-derive "which end
    // am I leaving from".
    public List<Cable> Path;
    public List<NetworkDevice> Waypoints;

    public int   HopIndex;
    public float T; // 0..1 progress along Path[HopIndex]

    // Source's subnet tint (GameTheme.SubnetTint) — computed once at spawn and
    // reused both for this packet's own mesh and for the guide dots Cable
    // shows on whichever segment currently carries it (see PacketManager.Update).
    public Color Tint;

    // Set only on an HttpResponse — the HttpRequest it's answering.
    public Packet LinkedRequest;

    // Set only on a multi-destination "trunk" packet (PacketManager.TrySendMulti)
    // covering the route prefix shared by every destination — once it reaches
    // the end of that shared prefix it doesn't deliver, it splits: one child
    // Packet per branch continues along that destination's own remaining Path.
    // Destination is meaningless on a trunk packet (there isn't a single one).
    public List<PacketBranch> PendingBranches;

    public Transform Visual;
}

/// <summary>One still-to-travel leg of a split multi-destination send — the
/// remainder of a destination's route beyond the shared trunk prefix.</summary>
public class PacketBranch
{
    public NetworkDevice Destination;
    public List<Cable> Path;
    public List<NetworkDevice> Waypoints;
}
