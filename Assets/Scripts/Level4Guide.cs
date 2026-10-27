using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

/// <summary>
/// Level 4's ("Routers &amp; Subnetting") guided walkthrough. Only instantiated
/// by GameBootstrap.SetupLevelObjectives when LevelManager.CurrentLevel == 4 —
/// TaskManager itself stays level-agnostic, so a future level just adds its
/// own equivalent of this component instead of branching inside TaskManager.
///
/// Picks up right where Level 3 (see Level3Guide) left off: the player
/// already knows enable -> configure terminal -> interface ethN -> end from
/// that level's pure CLI-navigation drill, so this level applies those same
/// moves to a real task — placing a Router, cabling it into the two
/// pre-built PC-Switch LAN segments (Level4_Template starts each PC on its
/// own dedicated Switch, so the two subnets read as two distinct physical
/// segments rather than one shared wire), configuring the Router's two
/// interfaces first (with CLIPanelManager's pulsing outline highlights
/// pointing at the real command each time), THEN addressing the two PCs to
/// match whichever subnet each one's Switch ends up behind — deliberately
/// router-first: the Router is what actually defines each subnet in this
/// exercise, so the PCs get addressed to match it rather than the other way
/// around — so the PCs can reach each other across it, and finally proving
/// it with an actual Send Packet transfer (not just a Ping) between the two
/// PCs. No mode-navigation hand-holding here; that lesson is already behind
/// the player.
///
/// Locks the sidebar and forces step order — same idea as TutorialManager's
/// guided onboarding (Level 1), Level2Guide's addressing walkthrough, and
/// Level3Guide's CLI drill, reusing TutorialUIManager's callout/highlight
/// and SidebarButtonLock's dim/lock helpers rather than duplicating them.
/// Stages are revealed and gated one at a time, and stage advancement rides
/// directly on TaskManager's own IsComplete tracking (registration order
/// below == stage order) instead of re-polling the same conditions twice.
/// </summary>
public class Level4Guide : MonoBehaviour, INarratedLevel
{
    public static Level4Guide Instance { get; private set; }

    class GuideStage
    {
        public System.Func<string> Text;
        public bool UnlockRouter;     // sidebar Router button clickable this stage
        public bool UnlockCable;      // sidebar Cable button clickable this stage
        public bool UnlockPing;       // sidebar Ping button clickable this stage
        public bool UnlockSendPacket; // sidebar Send Packet button clickable this stage
        public System.Func<RectTransform> Target;     // sidebar button to highlight, if any
        public System.Func<Vector3?> WorldMarker;      // world-space device to point at instead
        // Set alongside WorldMarker when it points at an already-placed
        // device, so its pulse becomes a translucent echo of that device's
        // own model instead of a generic circle.
        public System.Func<NetworkDevice> WorldMarkerDevice;
        // What the marker's mouse acts out at that spot (see MouseGesture) —
        // RightClick where a device's popup/CLI opens that way.
        public MouseGesture MarkerGesture = MouseGesture.Click;

        // CLI-specific guidance: pulses an outline around the command to
        // type/click next (falls back to the input field itself if that
        // command isn't in the current mode's palette) plus a second outline
        // framing the prompt label, via CLIPanelManager.SetHighlightedCommand/
        // HighlightPrompt. HighlightCommand is re-evaluated every frame (see
        // Update), so it can follow the CLI's live state rather than naming
        // one fixed command for the whole stage — see NextConfigCommand.
        public bool ShowCliHighlight;
        public System.Func<string> HighlightCommand;

        // Optional full-pause explainer shown once, the first time this stage
        // is entered, before the stage's own TutorialUIManager callout —
        // reserved for ideas that don't reduce to "click this" (see
        // ConceptCardManager). Null for every other stage.
        public System.Func<ConceptCardManager.ConceptCard> ConceptCard;

        // "Drag from here to there" arrow trail (see DragPathGuide). CLI
        // stages don't set this — ShowCliHighlight already implies a chip →
        // Command Queue trail (see ApplyCliHighlight).
        public System.Func<DragGuideRoute> Drag;

        // For a stage that's several clicks in a row (running a cable: the
        // Cable button, one device, its port, the other device, its port):
        // re-evaluated every frame (see Update) and, when set, supplies the
        // callout text, the button to click and the world marker in place of
        // Text/Target/WorldMarker — so the guide follows what the player has
        // actually clicked so far, one beat at a time, instead of one long
        // instruction for the whole stage.
        public System.Func<GuideBeat> Beat;
    }

    // One beat of a GuideStage.Beat stage. Key identifies it — Update only
    // re-applies the beat once the player has moved on to a different one.
    class GuideBeat
    {
        public string Key;
        public string Text;
        // A button to click next: pulsed, with a clicking-mouse marker on it.
        public System.Func<RectTransform> ClickTarget;
        // A device to click next: the world marker points at it.
        public NetworkDevice MarkerDevice;
    }

    // Stage indices whose ConceptCard has already been shown this level
    // session — a re-entry into the same stage (shouldn't normally happen,
    // but costs nothing to guard) must not replay the explainer.
    readonly HashSet<int> conceptCardShownStages = new HashSet<int>();

    NetworkDevice placedRouter;
    readonly List<NetworkDevice> placedPCs = new List<NetworkDevice>();
    readonly List<NetworkDevice> placedSwitches = new List<NetworkDevice>();
    bool pingSucceededAcrossRouter;
    bool packetSucceededAcrossRouter;
    bool completionHandled;

    List<GuideStage> stages;
    int stageIndex = -1;

    // Read by DragDropManager.StartDragging next to its existing
    // TutorialManager check — only the Router button is ever actually
    // clickable through this gate (every other placement button is hidden
    // outright, see HideUnneededButtons), so this only ever restricts Router.
    // One Router per level, too: a second one would leave the guide tracking
    // whichever it saw first while the player configures the other.
    public bool IsDeviceAllowed(DeviceType t) =>
        t != DeviceType.Router || (placedRouter == null && stages != null && stageIndex >= 0 && stageIndex < stages.Count && stages[stageIndex].UnlockRouter);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        StartCoroutine(InitNextFrame());
    }

    // NetworkUIManager builds its sidebar buttons in its own Start() (not
    // Awake()) — deferring one frame guarantees it has finished before this
    // reads button RectTransforms, same reasoning as TutorialManager.InitNextFrame.
    IEnumerator InitNextFrame()
    {
        yield return null;

        BuildStages();
        // Runs here — one frame after scene load, well before NetworkUIManager
        // actually queues its button reveal-flash (see StaggerRevealFlashChildren/
        // QueueRevealFlash, deferred two more frames past construction) — so a
        // tool this level hasn't unlocked yet is already inactive by the time
        // that queueing decision runs, instead of flashing in and then vanishing.
        HideUnneededButtons();

        // Seed with whatever devices already exist when this level starts —
        // the Level4_Template scene's pre-placed PCs/Switches (via
        // TemplateTopologyLoader), or TopologyLoader restoring a previous
        // save — so a pre-built network segment counts toward objectives the
        // same as anything the player places afterward. Waits for
        // GameBootstrap to report the starting topology in first: a template
        // scene merges in a frame or more after load, and scanning before
        // then would find nothing.
        while (!GameBootstrap.TopologyLoaded) yield return null;
        var routers = new List<NetworkDevice>();
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
        {
            if (device.deviceType == DeviceType.PC) placedPCs.Add(device);
            else if (device.deviceType == DeviceType.Switch) placedSwitches.Add(device);
            else if (device.deviceType == DeviceType.Router) routers.Add(device);
        }
        // Normally zero or one — but a save from before IsDeviceAllowed
        // limited this level to one Router can hold several. Track the one
        // the player actually wired up (most cabled ports), not whichever
        // FindObjectsByType happened to return first.
        placedRouter = routers.OrderByDescending(r => CabledPortCount(r)).FirstOrDefault();

        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped += HandleDeviceDropped;
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted += HandlePingCompleted;
        if (PacketManager.Instance != null) PacketManager.Instance.OnPacketDelivered += HandlePacketDelivered;
        if (CLIPanelManager.Instance != null) CLIPanelManager.Instance.OnPanelOpened += HandleCliOpened;

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.OnTasksChanged += HandleTasksChanged;

            // Registration order == guide stage order (see BuildStages), and
            // completed strictly in that order — see TaskManager.CompleteInOrder.
            tm.CompleteInOrder = true;
            tm.RegisterTask("Place a Router", () => placedRouter != null);
            tm.RegisterTask("Connect the Router to the Switch", RouterConnectedToNetwork);
            tm.RegisterTask("Configure the Router's first cabled interface",
                () => { var p = CabledPorts(); return p.Count > 0 && InterfaceConfigured(p[0]); });
            tm.RegisterTask("Configure the Router's second cabled interface",
                () => { var p = CabledPorts(); return p.Count > 1 && InterfaceConfigured(p[1]); });
            tm.RegisterTask("Give two PCs IP addresses matching the Router's two subnets",
                () => placedPCs.Count > 1
                    && InterfaceMatchesSubnet(placedPCs[0]) && InterfaceMatchesSubnet(placedPCs[1])
                    && TwoPcsOnDifferentSubnets());
            tm.RegisterTask("Successfully ping between the two PCs across the router", () => pingSucceededAcrossRouter);
            tm.RegisterTask("Send a packet between the two PCs across the router", () => packetSucceededAcrossRouter);
        }

        // Hold off on this level's first guide annotation until the whole
        // opening sequence has actually finished — see GameBootstrap.
        // IntroSequenceReady.
        while (!GameBootstrap.IntroSequenceReady) yield return null;

        // Not EnterStage(0) — a resumed save can put the level mid-guide
        // (Router already placed and cabled, say). Starting back at "Place a
        // Router" there is what let a second Router get placed. Start on
        // whichever stage is actually current.
        HandleTasksChanged();
    }

    void OnDestroy()
    {
        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped -= HandleDeviceDropped;
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted -= HandlePingCompleted;
        if (PacketManager.Instance != null) PacketManager.Instance.OnPacketDelivered -= HandlePacketDelivered;
        if (TaskManager.Instance != null) TaskManager.Instance.OnTasksChanged -= HandleTasksChanged;
        if (CLIPanelManager.Instance != null) CLIPanelManager.Instance.OnPanelOpened -= HandleCliOpened;
    }

    // ── Guide stages ──────────────────────────────────────────────────────────

    void BuildStages()
    {
        stages = new List<GuideStage>
        {
            // 0 — Place a Router. World marker points at whichever table has
            // no devices on it yet (Level4_Template's reserved router table)
            // so the player sees exactly where to drop it, not just that the
            // sidebar button unlocked.
            new() { Text = () => "Place a Router to bridge the two subnets you're about to create.",
                     UnlockRouter = true,
                     Target = () => NetworkUIManager.Instance?.GetDeviceButtonRect(DeviceType.Router),
                     WorldMarker = () => FindRouterLandingPoint(),
                     MarkerGesture = MouseGesture.Release,
                     Drag = () => new DragGuideRoute(
                         GuideAnchor.Ui(() => NetworkUIManager.Instance != null ? NetworkUIManager.Instance.GetCompoundToolRect(CompoundToolKind.Router) : null),
                         GuideAnchor.World(FindRouterLandingPoint),
                         inProgress: () => DragDropManager.Instance != null && DragDropManager.Instance.IsDragging) },

            // 1 — Wire the Router into the network. The template pre-cables each
            // PC to its own dedicated Switch (two separate LAN segments — see
            // TemplateTopologyLoader) since the Router doesn't exist yet at
            // authoring time, so the player has to run these two cables
            // themselves before any addressing/config work means anything — a
            // Ping can only ever succeed over a real cable path (see
            // ReachabilityService.Ping's physical-adjacency walk). Taught one
            // click at a time, following the player — see CablingBeat.
            new() { Text = () => "Run a cable from the Router to each Switch — one Router port per subnet's Switch.",
                     UnlockCable = true,
                     Beat = CablingBeat },

            // 2 — Configure whichever of the two cabled ports comes first
            // (ascending index — see CabledPorts), router-first: the Router
            // is what actually defines this subnet, so the example IP here is
            // just a suggestion (any valid host address works) rather than
            // reading it back off a PC that doesn't have one yet. Reads the
            // real port number back from CabledPorts rather than assuming
            // eth0 — the player is free to cable any two of the Router's
            // three ports, not necessarily the first two.
            new() { Text = () =>
                {
                    var ports = CabledPorts();
                    string eth = ports.Count > 0 ? $"eth{ports[0]}" : "the first port you cabled";
                    return $"Right-click the Router to open its CLI. Type <b>interface {eth}</b>, then <b>ip address 192.168.1.1 255.255.255.0</b> and <b>no shutdown</b> to set up your first subnet.";
                },
                     WorldMarker = () => placedRouter != null ? placedRouter.VisualTopPoint() : (Vector3?)null,
                     WorldMarkerDevice = () => placedRouter,
                     MarkerGesture = MouseGesture.RightClick,
                     ShowCliHighlight = true, HighlightCommand = () => NextConfigCommand(CabledPortAt(0)) },

            // 3 — The second cabled port, a different subnet from stage 2's.
            new() { Text = () =>
                {
                    var ports = CabledPorts();
                    string eth = ports.Count > 1 ? $"eth{ports[1]}" : "the second port you cabled";
                    return $"Now set up the second subnet: <b>interface {eth}</b>, then <b>ip address 192.168.2.1 255.255.255.0</b> and <b>no shutdown</b> — a different subnet than the first interface.";
                },
                     WorldMarker = () => placedRouter != null ? placedRouter.VisualTopPoint() : (Vector3?)null,
                     WorldMarkerDevice = () => placedRouter,
                     MarkerGesture = MouseGesture.RightClick,
                     ShowCliHighlight = true, HighlightCommand = () => NextConfigCommand(CabledPortAt(1)) },

            // 4 — Address the PCs to match the two subnets the Router just
            // defined. Not a sidebar action (right-click a placed PC opens
            // DeviceAddressPopup — see DragDropManager.TryOpenDeviceConfig),
            // so nothing is unlocked here; a world marker points at the PC
            // instead. Text reads the Router's own configured subnets AND
            // port numbers back off CabledPorts/RouterSubnetHint, so both the
            // example IP and the "(ethN)" label always match whatever ports
            // the player actually cabled and configured, not a hardcoded 0/1.
            new() { Text = () =>
                {
                    var ports = CabledPorts();
                    int port0 = ports.Count > 0 ? ports[0] : 0;
                    int port1 = ports.Count > 1 ? ports[1] : 1;
                    string s0 = RouterSubnetHint(port0) ?? $"eth{port0}'s subnet";
                    string s1 = RouterSubnetHint(port1) ?? $"eth{port1}'s subnet";
                    return $"Right-click a PC to open its address popup, then give the two PCs IPs matching the Router's two subnets — one PC on <b>{s0}</b> (eth{port0}), the other on <b>{s1}</b> (eth{port1}).";
                },
                     WorldMarker = () => placedPCs.Count > 0 && placedPCs[0] != null ? placedPCs[0].VisualTopPoint() : (Vector3?)null,
                     WorldMarkerDevice = () => placedPCs.Count > 0 ? placedPCs[0] : null,
                     MarkerGesture = MouseGesture.RightClick,
                     ConceptCard = GatewayConceptCard },

            // 5 — Ping across the router
            new() { Text = () => "Click <b>Ping</b>, then pick the two PCs to confirm they can reach each other through the Router.",
                     UnlockPing = true,
                     Target = () => NetworkUIManager.Instance?.PingButtonRect },

            // 6 — Send an actual packet across the router. A ping only proves
            // reachability; this proves data actually moves — PacketSendManager's
            // own click-source -> pick-type -> click-destination(s) -> Enter
            // flow, first introduced to the player here since no earlier level
            // has needed Send Packet yet.
            new() { Text = () => "Click <b>Send Packet</b>, choose a type, then click the two PCs. <b>HTTP Request</b> is instant; <b>File Transfer</b> carries a bigger, slower payload. Press Enter to commit, then click <b>Run Network</b> to send real data across the Router.",
                     UnlockSendPacket = true,
                     Target = () => NetworkUIManager.Instance?.SendPacketButtonRect },
        };
    }

    // Table/Remove Cable/Remove Device are never needed for this level's
    // objectives — the starting tables/PCs/Switches are already placed and
    // cabled by the Level4_Template scene (only the not-yet-placed Router's
    // own two cables are left for the player, see stage 1 above), so those
    // stay hidden outright. Ping and Send Packet ARE both objectives (stages
    // 5/6), so — like Router and Cable on the devices/cable compound button
    // (see NetworkUIManager.BuildDevicesCableCompound and SetCompoundToolLock
    // in EnterStage) — they stay visible the whole level and are gated by
    // SidebarButtonLock.SetLocked per-stage instead of being hidden.
    // Mirrors TutorialManager.HideUnneededButtons.
    void HideUnneededButtons()
    {
        // Ctrl+Z is a tool too — see UndoManager.AvailableInThisLevel.
        if (UndoManager.Instance != null) UndoManager.Instance.AvailableInThisLevel = false;

        var ui = NetworkUIManager.Instance;
        if (ui == null) return;

        SidebarButtonLock.SetActive(ui.TableButton, false);
        SidebarButtonLock.SetActive(ui.RemoveCableButton, false);
        SidebarButtonLock.SetActive(ui.RemoveDeviceButton, false);
        ui.DisableCompoundFlyouts(); // every stage locks the compound button anyway (see EnterStage) — keeps its arrow from popping in with the HUD first
    }

    void EnterStage(int index)
    {
        stageIndex = index;
        cliHighlightLive = false; // re-armed by ApplyCliHighlight once this stage's callout actually shows
        calloutShown = false;     // likewise — set by ShowStageCallout (a concept card can come first)

        bool inRange = stages != null && index >= 0 && index < stages.Count;
        var stage = inRange ? stages[index] : null;

        var ui = NetworkUIManager.Instance;
        if (ui != null)
        {
            CompoundToolKind? forcedTool = stage == null ? null
                : stage.UnlockRouter ? CompoundToolKind.Router
                : stage.UnlockCable ? CompoundToolKind.Cable
                : (CompoundToolKind?)null;
            ui.SetCompoundToolLock(forcedTool);
            SidebarButtonLock.SetLocked(ui.PingButton, stage == null || !stage.UnlockPing);
            SidebarButtonLock.SetLocked(ui.SendPacketButton, stage == null || !stage.UnlockSendPacket);
        }

        if (stage == null)
        {
            // All objectives done — HandleTasksChanged hands off to
            // LevelCompletePanel; nothing left to point at.
            TutorialUIManager.Instance?.Hide();
            CLIPanelManager.Instance?.ClearHighlights();
            return;
        }

        // A stage's ConceptCard (if any) takes over the screen the first time
        // this stage is entered — the stage's own callout/world-marker/CLI
        // highlight only appear once the player clicks through it, via
        // ShowStageCallout below. Re-entries of the same stage (shouldn't
        // normally happen) skip straight past this since the index is
        // already in conceptCardShownStages.
        if (stage.ConceptCard != null && !conceptCardShownStages.Contains(index))
        {
            conceptCardShownStages.Add(index);
            TutorialUIManager.Instance?.Hide();
            CLIPanelManager.Instance?.ClearHighlights();
            ConceptCardManager.Instance?.Show(stage.ConceptCard.Invoke(), () => ShowStageCallout(stage));
            return;
        }

        ShowStageCallout(stage);
    }

    void ShowStageCallout(GuideStage stage)
    {
        calloutShown = true;
        GuideBeat beat = stage.Beat?.Invoke();
        appliedBeatKey = beat?.Key;

        string text = beat != null ? beat.Text : stage.Text.Invoke();
        RectTransform target = beat != null ? beat.ClickTarget?.Invoke() : stage.Target?.Invoke();
        TutorialUIManager.Instance?.Show(text, target, buttonLabel: null,
            progress: (stages.IndexOf(stage) + 1, stages.Count));

        TutorialUIManager.Instance?.HideWorldMarkers();
        if (beat != null) ShowBeatMarkers(beat);
        else
        {
            Vector3? marker = stage.WorldMarker?.Invoke();
            if (marker.HasValue) TutorialUIManager.Instance?.ShowWorldMarker(0, marker.Value, stage.WorldMarkerDevice?.Invoke(), stage.MarkerGesture);
        }

        ApplyCliHighlight(stage);
    }

    // The player moved on to a different beat of the current stage (see
    // GuideStage.Beat) — swap the text and markers in place rather than
    // re-showing the whole callout, the same way TutorialManager's two-beat
    // steps do.
    void ApplyBeat(GuideBeat beat)
    {
        appliedBeatKey = beat.Key;
        TutorialUIManager.Instance?.RetargetHighlight(beat.ClickTarget?.Invoke(), beat.Text);
        TutorialUIManager.Instance?.HideWorldMarkers();
        ShowBeatMarkers(beat);
    }

    static void ShowBeatMarkers(GuideBeat beat)
    {
        var ui = TutorialUIManager.Instance;
        if (ui == null) return;

        if (beat.ClickTarget != null) ui.ShowUiMarker(GuideAnchor.Ui(beat.ClickTarget));
        else ui.HideUiMarker();

        if (beat.MarkerDevice != null)
            ui.ShowWorldMarker(0, beat.MarkerDevice.VisualTopPoint(), beat.MarkerDevice, MouseGesture.Click);
    }

    // Outlines + a chip → Command Queue drag trail for a CLI stage's command
    // (the trail resolves itself live — see CLIPanelManager.
    // HighlightedCommandDragRoute — so it only appears once that command has a
    // chip in the current mode), or the stage's own Drag route otherwise.
    // Re-run whenever the panel opens: these stages ask the player to open
    // the CLI themselves, and Open() clears any highlight set before it —
    // which is how this level's outlines used to vanish the moment the
    // player actually opened the panel.
    void ApplyCliHighlight(GuideStage stage)
    {
        var cli = CLIPanelManager.Instance;
        cliHighlightLive = stage.ShowCliHighlight;
        if (stage.ShowCliHighlight)
        {
            appliedHighlight = stage.HighlightCommand?.Invoke();
            cli?.SetHighlightedCommand(appliedHighlight);
            cli?.HighlightPrompt(true);
            if (cli != null) TutorialUIManager.Instance?.ShowDragGuide(cli.HighlightedCommandDragRoute);
        }
        else
        {
            cli?.ClearHighlights();
            if (stage.Drag != null) TutorialUIManager.Instance?.ShowDragGuide(stage.Drag());
            else TutorialUIManager.Instance?.HideDragGuide();
        }
    }

    void HandleCliOpened(NetworkDevice device)
    {
        if (stages != null && stageIndex >= 0 && stageIndex < stages.Count) ApplyCliHighlight(stages[stageIndex]);
    }

    // True while the current stage's CLI highlight is showing — Update keeps
    // it pointed at whatever the stage's HighlightCommand says right now.
    bool cliHighlightLive;
    string appliedHighlight;

    // True once the current stage's callout is actually up (not still behind
    // its concept card) — Update only follows a Beat stage from then on.
    bool calloutShown;
    string appliedBeatKey;

    void Update()
    {
        if (stages == null || stageIndex < 0 || stageIndex >= stages.Count) return;
        var stage = stages[stageIndex];

        // A multi-click stage follows the player beat by beat — see GuideStage.Beat.
        if (calloutShown && stage.Beat != null)
        {
            GuideBeat beat = stage.Beat();
            if (beat != null && beat.Key != appliedBeatKey) ApplyBeat(beat);
        }

        // Re-points the outline (and so the chip → Command Queue drag trail,
        // which follows the highlighted chip by itself) whenever the CLI's
        // state moves on to a different next command — a mode change, an
        // interface switch, an address being set.
        if (!cliHighlightLive) return;
        var cli = CLIPanelManager.Instance;
        if (cli == null || !cli.IsOpen) return;

        string next = stage.HighlightCommand?.Invoke();
        if (next == appliedHighlight) return;
        appliedHighlight = next;
        cli.SetHighlightedCommand(next);
    }

    // The command that moves the Router one step closer to `port` being fully
    // configured, read off the CLI's live mode, current interface and that
    // interface's state — never a fixed "ip address" for the whole stage, which
    // prompted players to drag it in while the CLI was still sitting on the
    // previous interface (or wasn't in config mode at all). Null once the port
    // is done (the highlight falls back to the input field until the stage
    // advances).
    string NextConfigCommand(int? port)
    {
        if (placedRouter == null || CLIPanelManager.Instance == null || !port.HasValue) return null;
        RouterCLI cli = CLIPanelManager.Instance.GetOrCreateSession(placedRouter);

        switch (cli.CurrentMode)
        {
            case RouterCLI.Mode.UserExec:       return "enable";
            case RouterCLI.Mode.PrivilegedExec: return "configure terminal";
            case RouterCLI.Mode.GlobalConfig:   return "interface eth<N>";
        }

        int p = port.Value;
        if (cli.CurrentInterfacePort != p) return "interface eth<N>";
        if (!SubnetMath.IsDottedQuad(cli.GetInterfaceIp(p)) || !SubnetMath.IsDottedQuad(cli.GetInterfaceMask(p)))
            return "ip address <ip> <mask>";
        if (!placedRouter.IsPortActive(p)) return "no shutdown";
        return null;
    }

    int? CabledPortAt(int index)
    {
        var ports = CabledPorts();
        return index < ports.Count ? ports[index] : (int?)null;
    }

    // ── Cabling beats (stage 1) ──────────────────────────────────────────────
    // Running a cable is five clicks — the Cable button, one device, one of its
    // ports, the other device, one of its ports — and a single "run a cable from
    // the Router to each Switch" left players guessing where they were in it.
    // This reads CableManager's live state (the same hooks TutorialManager's
    // Level 1 cabling sub-steps poll) and points at exactly the next click,
    // following whichever end the player actually started from. Cancelling
    // partway (right-click, or Cable again) lands back on the Cable beat.

    GuideBeat CablingBeat()
    {
        var cables = CableManager.Instance;
        int wired = CabledPorts().Count;

        if (cables == null || !cables.IsConnecting)
            return new GuideBeat
            {
                Key = "cableButton" + wired,
                ClickTarget = () => NetworkUIManager.Instance != null ? NetworkUIManager.Instance.CableButtonRect : null,
                Text = wired == 0
                    ? "Your Router has to be wired into both subnets before it can bridge them. Click <b>Cable</b> to start the first connection."
                    : "One subnet wired! Click <b>Cable</b> again for the second — this time from the Router to the other Switch.",
            };

        NetworkDevice source = cables.SourceDevice;
        NetworkDevice picking = cables.PickerOpenForDevice;

        // Nothing clicked yet in this cable.
        if (source == null && picking == null)
            return new GuideBeat
            {
                Key = "clickStart",
                MarkerDevice = placedRouter,
                Text = "Click the <b>Router</b> — that's where this cable starts.",
            };

        // The device next to plug into, if the player's own click strays: a
        // Switch still waiting for its Router cable once the cable starts at
        // the Router, the Router otherwise (nothing picked yet, or the player
        // started from the Switch end instead).
        NetworkDevice expected = source == placedRouter ? NextSwitchToWire() : placedRouter;

        // Clicked a device with nothing left to plug into (e.g. a PC, whose
        // one port already runs to its Switch) — its list opens empty, so
        // say why and point back at the device that does have room. The
        // empty list has to be closed first (it can sit right on top of that
        // device, and a click outside it only closes it — see
        // InterfacePickerPopup); once it's gone this falls through to the
        // "click the other end" beat below by itself.
        if (picking != null && picking.GetFreePorts().Count == 0)
            return new GuideBeat
            {
                Key = "noFreePort:" + picking.GetInstanceID(),
                MarkerDevice = expected,
                Text = $"That {picking.deviceType} has no free port left. Click an empty spot to close its list, then click the glowing <b>{(expected != null ? expected.deviceType.ToString() : "Switch")}</b> instead.",
            };

        // A device's port list is open — the popup itself (and that device's
        // ports lit gold, see CableManager.ShowPicker) is the focus, so no
        // world marker on top of it.
        if (picking != null)
            return new GuideBeat
            {
                Key = "pickPort:" + picking.GetInstanceID() + (source == null ? ":start" : ":end"),
                Text = source == null
                    ? $"Its free ports pop up — click one (like <b>{ExamplePort(picking)}</b>) to plug the cable into the {picking.deviceType}."
                    : $"Now pick a free port on the {picking.deviceType} the same way to complete the cable.",
            };

        // One end is plugged in — mark the other (see `expected` above).
        return new GuideBeat
        {
            Key = "clickEnd:" + (expected != null ? expected.GetInstanceID() : 0),
            MarkerDevice = expected,
            Text = expected == placedRouter ? "Now click the <b>Router</b> to finish the connection."
                 : expected != null         ? "Now click the glowing <b>Switch</b> to finish the connection."
                 :                            "Now click a <b>Switch</b> to finish the connection.",
        };
    }

    // The Switch nearest the Router that isn't cabled to it yet — one clear
    // target at a time rather than marking every Switch at once.
    NetworkDevice NextSwitchToWire()
    {
        if (placedRouter == null) return null;

        NetworkDevice best = null;
        float bestDist = float.MaxValue;
        foreach (var sw in placedSwitches)
        {
            if (sw == null || IsCabledTo(placedRouter, sw)) continue;
            float dist = (sw.transform.position - placedRouter.transform.position).sqrMagnitude;
            if (dist < bestDist) { bestDist = dist; best = sw; }
        }
        return best;
    }

    static bool IsCabledTo(NetworkDevice a, NetworkDevice b)
    {
        for (int p = 0; p < a.PortCount; p++)
        {
            var cable = a.GetPortCable(p);
            if (cable != null && (cable.DeviceA == b || cable.DeviceB == b)) return true;
        }
        return false;
    }

    // The port name InterfacePickerPopup lists first for this device.
    static string ExamplePort(NetworkDevice device)
    {
        var free = device.GetFreePorts();
        return $"eth{(free.Count > 0 ? free[0] : 0)}";
    }

    // ── Stage advance + completion ───────────────────────────────────────────
    // Rides directly on TaskManager's own IsComplete tracking rather than
    // re-evaluating the same Func<bool> conditions a second time here —
    // registration order in InitNextFrame matches stage order in BuildStages,
    // so "N tasks complete" IS "we're on stage N".

    void HandleTasksChanged()
    {
        // Task registration/restore fires this long before the entrance has
        // finished — stage callouts wait for it, and InitNextFrame's own
        // post-intro call catches up to whatever stage is current by then.
        if (!GameBootstrap.IntroSequenceReady) return;

        var tasks = TaskManager.Instance.Tasks;
        int stage = TaskManager.Instance.CurrentTaskIndex;
        if (stage != stageIndex) EnterStage(stage);

        if (completionHandled) return;
        if (tasks.Count == 0 || stage < tasks.Count) return;

        completionHandled = true;
        TutorialUIManager.Instance?.Hide();

        int stars = TaskManager.Instance.ComputeStars();
        SaveManager.MarkLevelCompleted(4);
        SaveManager.SetStars(4, stars);
        AutoSaveManager.Instance?.SaveNow();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "You bridged two subnets with a router and sent real data across it. More levels are on the way.",
            "Back to Level Select",
            () =>
            {
                LoadingScreenManager.ExitToLevelSelect();
            },
            stars);
    }

    void HandleDeviceDropped(NetworkDevice device, bool isNewDevice)
    {
        if (!isNewDevice) return;

        if (device.deviceType == DeviceType.Router)
        {
            if (placedRouter == null) placedRouter = device;
        }
        else if (device.deviceType == DeviceType.PC)
        {
            placedPCs.Add(device);
        }
        else if (device.deviceType == DeviceType.Switch)
        {
            placedSwitches.Add(device);
        }
    }

    // Path.Count >= 2 means the ping crossed at least one intermediate hop
    // (PC -> router, router -> PC) rather than a direct same-subnet link —
    // see ReachabilityService.ExpandRouter, which is the only thing that adds
    // extra cables to the path.
    void HandlePingCompleted(NetworkDevice source, NetworkDevice dest, ReachabilityService.PingResult result)
    {
        if (result.Success && result.Path.Count >= 2) pingSucceededAcrossRouter = true;
    }

    // Same "crossed at least one intermediate hop" signal HandlePingCompleted
    // uses above, applied to a delivered Packet's own walked Path instead of a
    // PingResult's — fires for every delivered packet (including the
    // auto-generated HttpResponse an HttpRequest triggers), any one of which
    // is enough to prove data actually moved across the Router.
    void HandlePacketDelivered(Packet p)
    {
        if (p.Path.Count >= 2) packetSucceededAcrossRouter = true;
    }

    // True once at least two of the Router's ports are physically cabled —
    // deliberately not checking WHERE they lead (e.g. specifically the two
    // Switches), matching this level's philosophy elsewhere of checking a
    // real, mechanically-enforced condition rather than a prescribed exact
    // topology. Two cabled ports is what stages 2/3's interface config
    // (and therefore a real Ping) actually needs to mean anything.
    bool RouterConnectedToNetwork() => CabledPorts().Count >= 2;

    // The Router's own port indices that actually have a cable plugged in,
    // ascending. The player is free to use any two of the Router's three
    // ports — nothing pins this level to eth0/eth1 specifically — so stages
    // 2-4's text and completion checks all read the real numbers back from
    // here instead of assuming the first two ports.
    List<int> CabledPorts()
    {
        var result = new List<int>();
        if (placedRouter == null) return result;

        for (int p = 0; p < placedRouter.PortCount; p++)
            if (placedRouter.GetPortCable(p) != null) result.Add(p);
        return result;
    }

    static int CabledPortCount(NetworkDevice device)
    {
        int count = 0;
        for (int p = 0; p < device.PortCount; p++)
            if (device.GetPortCable(p) != null) count++;
        return count;
    }

    bool TwoPcsOnDifferentSubnets()
    {
        for (int i = 0; i < placedPCs.Count; i++)
        {
            var a = GetAddress(placedPCs[i]);
            if (a == null) continue;

            for (int j = i + 1; j < placedPCs.Count; j++)
            {
                var b = GetAddress(placedPCs[j]);
                if (b == null) continue;

                if (!SubnetMath.SameSubnet(a.Ip, a.Mask, b.Ip, b.Mask)) return true;
            }
        }
        return false;
    }

    // True once some active router interface shares a subnet with the given
    // PC's own address — used per-PC in the combined stage-4 addressing task
    // (see InitNextFrame's task registration) so both PCs land on the
    // Router's two already-configured subnets rather than "any two
    // interfaces differ from each other".
    bool InterfaceMatchesSubnet(NetworkDevice pc)
    {
        var addr = GetAddress(pc);
        if (addr == null || placedRouter == null || CLIPanelManager.Instance == null) return false;

        RouterCLI cli = CLIPanelManager.Instance.GetOrCreateSession(placedRouter);
        for (int p = 0; p < placedRouter.PortCount; p++)
        {
            if (!placedRouter.IsPortActive(p)) continue;

            string ip = cli.GetInterfaceIp(p);
            string mask = cli.GetInterfaceMask(p);
            if (ip == null || mask == null) continue;

            if (SubnetMath.SameSubnet(ip, mask, addr.Ip, addr.Mask)) return true;
        }
        return false;
    }

    // True once the given router port has a valid ip+mask and is up (no
    // shutdown) — stages 2/3's gate, checked before any PC exists to match
    // it against (router-first ordering, see class doc comment).
    bool InterfaceConfigured(int port)
    {
        if (placedRouter == null || CLIPanelManager.Instance == null) return false;
        if (port < 0 || port >= placedRouter.PortCount || !placedRouter.IsPortActive(port)) return false;

        RouterCLI cli = CLIPanelManager.Instance.GetOrCreateSession(placedRouter);
        return SubnetMath.IsDottedQuad(cli.GetInterfaceIp(port)) && SubnetMath.IsDottedQuad(cli.GetInterfaceMask(port));
    }

    // Friendly "192.168.1.x"-style example of a router port's own configured
    // subnet, for stage 4's PC-addressing text — read back from whatever the
    // player actually typed into whichever port this is (see CabledPorts),
    // not a hardcoded guess. Null if that port isn't validly configured yet.
    string RouterSubnetHint(int port)
    {
        if (!InterfaceConfigured(port)) return null;

        RouterCLI cli = CLIPanelManager.Instance.GetOrCreateSession(placedRouter);
        uint network = SubnetMath.NetworkAddress(cli.GetInterfaceIp(port), cli.GetInterfaceMask(port));
        string dotted = SubnetMath.ToDottedQuad(network);
        int lastDot = dotted.LastIndexOf('.');
        return lastDot < 0 ? dotted : dotted.Substring(0, lastDot + 1) + "x";
    }

    // Finds the table nothing is currently sitting on — the spot reserved for
    // the router — by checking occupancy rather than a hardcoded position or
    // size, so this keeps working if Level4_Template's tables are ever moved
    // or resized. Returns null (no marker shown) if every table is occupied,
    // e.g. on a replay where the router's already elsewhere.
    static Vector3? FindRouterLandingPoint()
    {
        var devices = Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude);
        foreach (var table in Table.AllTables)
        {
            bool occupied = false;
            foreach (var d in devices)
                if (table.ContainsXZ(d.transform.position)) { occupied = true; break; }

            if (!occupied) return table.VisualMarkerPoint();
        }
        return null;
    }

    static DeviceAddress GetAddress(NetworkDevice device)
    {
        if (device == null) return null;
        var addr = device.GetComponent<DeviceAddress>();
        return addr != null && addr.HasAddress ? addr : null;
    }

    // ── Gateway concept card ──────────────────────────────────────────────────
    // Shown once, the first time stage 4 (addressing the PCs) is entered —
    // see ConceptCard on GuideStage / the concept-card gate in EnterStage.
    // This is the first level where a PC's address alone stops being enough
    // to explain reachability (Level 2's single-subnet ping either matches
    // or it doesn't), so it's the right place to name the idea that's about
    // to matter: the Router interface a PC's traffic quietly falls back to
    // whenever the destination isn't on its own subnet.

    ConceptCardManager.ConceptCard GatewayConceptCard() => new ConceptCardManager.ConceptCard
    {
        Title = "Default Gateways",
        Beats = new[]
        {
            "Every PC only knows how to reach devices on its own subnet directly — same network address, same mask. Anything outside that subnet is unreachable on its own.",
            "That's what a <b>default gateway</b> is for: a router interface sitting on the PC's own subnet, responsible for forwarding traffic bound for anywhere else.",
            "The Router interfaces you just configured <b>are</b> the gateways for their subnets. When you address each PC next, its mask decides what's \"local\" — everything else quietly routes through the Router instead.",
            GatewayRecapBeat()
        },
        BuildDiagram = BuildGatewayDiagram
    };

    // Ties the concept back to the literal syntax the player already typed
    // in stages 2/3, and hands them concrete numbers for the addressing step
    // that's about to follow — reads the Router's REAL configured interfaces
    // (guaranteed to both exist by this point: stage 4 only unlocks once
    // stages 2/3's InterfaceConfigured checks pass) rather than repeating the
    // earlier stages' example IPs, so this stays correct even if the player
    // typed different (still valid) addresses.
    string GatewayRecapBeat()
    {
        var ports = CabledPorts();
        int port0 = ports.Count > 0 ? ports[0] : 0;
        int port1 = ports.Count > 1 ? ports[1] : 1;

        RouterCLI cli = CLIPanelManager.Instance != null && placedRouter != null
            ? CLIPanelManager.Instance.GetOrCreateSession(placedRouter) : null;
        string ip0 = cli?.GetInterfaceIp(port0);
        string mask0 = cli?.GetInterfaceMask(port0);
        string ip1 = cli?.GetInterfaceIp(port1);
        string mask1 = cli?.GetInterfaceMask(port1);
        string host0 = HostExample(port0);
        string host1 = HostExample(port1);

        if (ip0 == null || ip1 == null || host0 == null || host1 == null)
            return "Concretely: the <b>ip address</b> command you ran on each interface is exactly what made it a gateway — that same address is what you'll enter as each PC's gateway next.";

        return $"Concretely: <b>interface eth{port0}</b> → <b>ip address {ip0} {mask0}</b> made {ip0} the gateway for that subnet — address a PC there like <b>{host0} {mask0}</b>. Same for eth{port1}: {ip1} is the gateway for a PC addressed like <b>{host1} {mask1}</b>.";
    }

    // "192.168.1.x" -> "192.168.1.10" — a concrete, still-valid example host
    // address on the same subnet as the given router port, for the recap
    // beat above. Null if that port isn't validly configured yet.
    string HostExample(int port)
    {
        string hint = RouterSubnetHint(port);
        return hint == null ? null : hint.Substring(0, hint.Length - 1) + "10";
    }

    // Simple three-node diagram: PC -> Router (captioned as the gateway) ->
    // a generic "other subnet" box. Deliberately illustrative rather than
    // reading back the player's actual chosen IPs (those can be anything
    // valid — see stages 2-4), so the diagram stays correct no matter what
    // addresses end up configured. Node/arrow builders live on
    // ConceptCardManager now — shared with Level11Challenge's VLSM diagram
    // rather than each concept card re-implementing its own.
    static void BuildGatewayDiagram(Transform slot)
    {
        var font       = Resources.Load<TMP_FontAsset>("Fonts/PixeloidMono SDF");
        var pcIcon     = Resources.Load<Sprite>("UIIcons/PC_Icon");
        var routerIcon = Resources.Load<Sprite>("UIIcons/Router_Icon");

        ConceptCardManager.BuildIconNode(slot, font, pcIcon, "PC\n(Subnet A)", new Vector2(-230f, 10f));
        ConceptCardManager.BuildArrow(slot, font, new Vector2(-125f, 10f));
        ConceptCardManager.BuildIconNode(slot, font, routerIcon, "Router\nDefault Gateway", new Vector2(0f, 10f));
        ConceptCardManager.BuildArrow(slot, font, new Vector2(125f, 10f));
        ConceptCardManager.BuildPlateNode(slot, font, "Other\nSubnet", new Vector2(230f, 10f));
    }
}
