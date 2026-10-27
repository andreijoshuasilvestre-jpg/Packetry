using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Level 10's ("Resilience Challenge") objective provider. Only instantiated
/// by GameBootstrap.SetupLevelObjectives when LevelManager.CurrentLevel == 10
/// — TaskManager itself stays level-agnostic, so this is just another
/// provider alongside Level5Challenge through Level9Challenge.
///
/// Same open-topology spirit as Level8Challenge — no fixed device list or
/// shape, just properties checked against whatever the player builds — but
/// raises the bar from "everyone can reach everyone" to "everyone can still
/// reach everyone after losing any single cable" (ReachabilityService
/// .SurvivesAnyCableCut). A PC only ever has one port (see
/// DeviceModelBuilder.BuildPCProcedural), so its own last-mile cable can
/// never be made redundant — the check accounts for that by only requiring
/// the OTHER PCs to stay connected when a given cable is cut, which in
/// practice means the player has to build actual backbone redundancy (e.g.
/// two Switches linked by two separate cables) rather than a single chain.
/// </summary>
public class Level10Challenge : MonoBehaviour, IKeepsFinishedBuild
{
    const int MinPcs = 4;

    // Live view of what's on the field — see PlacedDeviceTracker.
    readonly PlacedDeviceTracker placed = new PlacedDeviceTracker();
    IReadOnlyList<NetworkDevice> placedPCs => placed.PCs;
    readonly PacketCoverageTracker packetCoverage = new PacketCoverageTracker();
    bool completionHandled;

    void Start()
    {
        StartCoroutine(InitNextFrame());
    }

    // Deferred one frame so NetworkUIManager/TaskUIManager have finished
    // building before anything here reacts to them — same reasoning as
    // Level5Challenge.InitNextFrame.
    IEnumerator InitNextFrame()
    {
        yield return null;
        while (!GameBootstrap.IntroSequenceReady) yield return null;

        // Seed with whatever devices already exist when this level starts —
        // a restored save (TopologyLoader, built in Awake, earlier than this
        // Start) counts toward objectives the same as anything placed fresh.
        placed.Attach();
        packetCoverage.Attach();

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.RegisterTask($"Place at least {MinPcs} PCs — add Switches (or a Router) to build redundancy in",
                () => placedPCs.Count >= MinPcs,
                hintNudge: "A single chain of devices has no redundancy — you'll need at least one loop in the backbone.",
                hintSpecific: $"You need at least {MinPcs} PCs on the table before the rest of the checklist can pass.",
                hintAnswer: "Click the PC button in the sidebar four times, then add two Switches.");
            tm.RegisterTask("Cable every PC into the network", AllPcsCabled,
                hintNudge: "Use the Cable tool — every PC needs at least one connection into whatever you've built.",
                hintSpecific: "Check each PC for a dangling, unconnected port.",
                hintAnswer: "Click Cable, click a PC, pick its port, then click whatever it should connect to.");
            tm.RegisterTask("Give every PC an IP address", AllPcsAddressed,
                hintNudge: "Right-click a PC to open its address panel.",
                hintSpecific: "Every PC needs both an IP and a subnet mask set — not just some of them.",
                hintAnswer: "e.g. 192.168.1.10 / 255.255.255.0 — reuse the same subnet for PCs that should talk directly.");
            tm.RegisterTask("Get every PC talking to every other PC", () => ReachabilityService.AllPairsReachable(placedPCs),
                hintNudge: "Every PC should be able to ping every other PC.",
                hintSpecific: "If two PCs are on different subnets with no Router bridging them, they can never reach each other.",
                hintAnswer: "Simplest fix: put every PC on the same subnet through Switches.");
            tm.RegisterTask("Make sure the network survives losing any single cable",
                () => ReachabilityService.SurvivesAnyCableCut(placedPCs, AllCables()),
                hintNudge: "Picture unplugging one cable at a time — does everyone except the PC it belonged to stay connected?",
                hintSpecific: "A single chain between two Switches is a single point of failure — one cut splits the network in two.",
                hintAnswer: "Run a second cable directly between two Switches (using their spare ports) so a loop exists — losing either one still leaves the other.");
            packetCoverage.RegisterTask(tm, placedPCs);

            // Subscribed only once every task is in — a resumed save's tasks
            // register already complete one at a time, and the first alone
            // would otherwise read as the whole level done. A restored
            // finished build (see IKeepsFinishedBuild) arrives fully complete:
            // its completion and stars were recorded the first time round,
            // so the Level Complete panel isn't replayed.
            completionHandled = tm.Tasks.All(t => t.IsComplete);
            tm.OnTasksChanged += HandleTasksChanged;
        }
    }

    void OnDestroy()
    {
        placed.Detach();
        packetCoverage.Detach();
        if (TaskManager.Instance != null) TaskManager.Instance.OnTasksChanged -= HandleTasksChanged;
    }

    bool AllPcsCabled()
    {
        if (placedPCs.Count < MinPcs) return false;
        foreach (var pc in placedPCs)
        {
            if (pc == null) return false;
            bool cabled = false;
            for (int i = 0; i < pc.PortCount; i++)
            {
                if (pc.GetPortCable(i) != null) { cabled = true; break; }
            }
            if (!cabled) return false;
        }
        return true;
    }

    bool AllPcsAddressed()
    {
        if (placedPCs.Count < MinPcs) return false;
        foreach (var pc in placedPCs)
        {
            var addr = pc == null ? null : pc.GetComponent<DeviceAddress>();
            if (addr == null || !addr.HasAddress) return false;
        }
        return true;
    }

    // Queried fresh (rather than tracked via CableManager.OnCableCreated
    // /OnCableRemoved) so a cable the player deletes mid-attempt is never
    // stale in the resilience check — TaskManager already polls every
    // RegisterTask condition once a frame, so this costs nothing extra.
    static List<Cable> AllCables() =>
        Object.FindObjectsByType<Cable>(FindObjectsInactive.Exclude).ToList();

    void HandleTasksChanged()
    {
        if (completionHandled) return;

        var tasks = TaskManager.Instance.Tasks;
        int completed = tasks.Count(t => t.IsComplete);
        if (tasks.Count == 0 || completed < tasks.Count) return;

        completionHandled = true;

        int stars = TaskManager.Instance.ComputeStars();
        SaveManager.MarkLevelCompleted(10);
        SaveManager.SetStars(10, stars);
        AutoSaveManager.Instance?.SaveNow();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "You built a network with no single point of failure in its backbone — the mark of a resilient design.",
            "Back to Level Select",
            () =>
            {
                LoadingScreenManager.ExitToLevelSelect();
            },
            stars);
    }
}
