using System.Collections.Generic;

/// <summary>
/// Shared "prove the whole network works" objective for challenge levels:
/// remembers every PC that has been at either end of a successfully
/// delivered packet (PacketManager.OnPacketDelivered). A sender counts once
/// a packet it sent actually arrives, not merely for sending it, so a PC
/// with bad addressing still fails its own check; a receiver counts once a
/// packet reaches it. An HttpRequest's auto-generated HttpResponse lands
/// back on the original sender, so it's covered both ways naturally.
///
/// Plain C# class, not a MonoBehaviour — each level provider owns one,
/// calls Attach() alongside its other event subscriptions (once
/// PacketManager.Instance exists) and Detach() in its own OnDestroy, the
/// same lifetime every level already gives its DragDropManager/
/// PingToolManager subscriptions.
/// </summary>
public class PacketCoverageTracker
{
    const string TaskDescription = "Run Network so every PC successfully sends or receives a packet";
    const string HintNudge       = "Click Send Packet, assign senders and receivers, then click Run Network.";
    const string HintSpecific    = "A PC counts once a packet it sent arrives, or once a packet reaches it. One Run Network can cover several PCs at once.";
    const string HintAnswer      = "Click Send Packet, pick one PC as the sender, click every other PC as a receiver, press Enter, then click Run Network.";

    readonly HashSet<NetworkDevice> covered = new();
    PacketManager subscribedTo;

    public void Attach()
    {
        Detach();
        subscribedTo = PacketManager.Instance;
        if (subscribedTo != null) subscribedTo.OnPacketDelivered += HandlePacketDelivered;
    }

    public void Detach()
    {
        if (subscribedTo != null) subscribedTo.OnPacketDelivered -= HandlePacketDelivered;
        subscribedTo = null;
    }

    /// <summary>Registers the standard task against the level's own live PC
    /// list — evaluated every frame by TaskManager, so PCs placed (or
    /// removed) after registration are accounted for automatically.</summary>
    public void RegisterTask(TaskManager tm, IReadOnlyList<NetworkDevice> pcs) =>
        tm.RegisterTask(TaskDescription, () => AllCovered(pcs), HintNudge, HintSpecific, HintAnswer);

    // Removed PCs linger in each level's placedPCs list as destroyed
    // references (Unity-null) — skipped rather than counted as uncovered.
    public bool AllCovered(IReadOnlyList<NetworkDevice> pcs)
    {
        bool any = false;
        foreach (var pc in pcs)
        {
            if (pc == null) continue;
            any = true;
            if (!covered.Contains(pc)) return false;
        }
        return any;
    }

    void HandlePacketDelivered(Packet p)
    {
        Mark(p.Source);
        Mark(p.Destination);
    }

    void Mark(NetworkDevice device)
    {
        if (device != null && device.deviceType == DeviceType.PC) covered.Add(device);
    }
}
