using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Level 3's ("Router CLI Basics") guided walkthrough. Only instantiated by
/// GameBootstrap.SetupLevelObjectives when LevelManager.CurrentLevel == 3 —
/// TaskManager itself stays level-agnostic, so a future level just adds its
/// own equivalent of this component instead of branching inside TaskManager.
///
/// A pure mode-navigation drill on the single Router Level3SceneSetup
/// already placed on the table — no IP/subnet vocabulary at all, just
/// learning to move enable -> configure terminal -> interface eth0 -> end
/// and back, with CLIPanelManager's pulsing outline highlights pointing at
/// exactly what to type next. The final stage is an unaided checkpoint —
/// same moves, no highlights, a different interface number — so finishing
/// the level means the player actually retained the sequence, not just
/// followed the outlines. Nothing to place, cable, or address here — that's
/// Level 4 (see Level4Guide), which picks up right where this level's
/// mode-navigation drill leaves off and applies it to a real addressing task.
///
/// Reuses TutorialUIManager's callout/highlight and SidebarButtonLock's
/// dim/lock helpers rather than duplicating them, same as TutorialManager's
/// guided onboarding (Level 1) and Level2Guide's addressing walkthrough.
/// Stages are revealed and gated one at a time, and stage advancement rides
/// directly on TaskManager's own IsComplete tracking (registration order
/// below == stage order) instead of re-polling the same conditions twice.
/// </summary>
public class Level3Guide : MonoBehaviour, INarratedLevel
{
    public static Level3Guide Instance { get; private set; }

    class GuideStage
    {
        public System.Func<string> Text;
        public System.Func<Vector3?> WorldMarker;      // world-space device to point at, if any
        // Set alongside WorldMarker when it points at an already-placed
        // device, so its pulse becomes a translucent echo of that device's
        // own model instead of a generic circle.
        public System.Func<NetworkDevice> WorldMarkerDevice;
        // What the marker's mouse acts out at that spot (see MouseGesture) —
        // RightClick where a device's popup/CLI opens that way.
        public MouseGesture MarkerGesture = MouseGesture.Click;

        // Pulses an outline around the command to type/click next (falls
        // back to the input field itself if that command isn't in the
        // current mode's palette) plus a second outline framing the prompt
        // label, via CLIPanelManager.SetHighlightedCommand/HighlightPrompt.
        public bool ShowCliHighlight;
        public string HighlightCommand;
    }

    NetworkDevice placedRouter;
    bool completionHandled;

    // Latches once the player has entered Interface Config at least once, so
    // the "type end" stage's condition (CurrentMode == PrivilegedExec) can't
    // fire the instant they type "enable" — PrivilegedExec is revisited both
    // right after enable AND after end, only the second one should count.
    bool reachedInterfaceConfigOnce;

    List<GuideStage> stages;
    int stageIndex = -1;

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

        // Level3SceneSetup's single Router (or a restored save's) — waits for
        // GameBootstrap to report the starting topology in first, same as
        // every guide, in case this level ever gets a template scene that
        // merges in a frame or more after load.
        while (!GameBootstrap.TopologyLoaded) yield return null;
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
        {
            if (device.deviceType == DeviceType.Router && placedRouter == null) placedRouter = device;
        }

        if (CLIPanelManager.Instance != null) CLIPanelManager.Instance.OnPanelOpened += HandleCliOpened;

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.OnTasksChanged += HandleTasksChanged;

            // Registration order == guide stage order (see BuildStages), and
            // completed strictly in that order — see TaskManager.CompleteInOrder.
            tm.CompleteInOrder = true;
            // Always starts the drill over rather than resuming it: every
            // stage here is "get the CLI into mode X", and the CLI's mode is
            // session-only — a resumed save would ask for e.g. `end` from a
            // CLI that's back at `Router>`.
            tm.DiscardRestoredProgress();
            tm.RegisterTask("Open the Router's CLI", () =>
                CLIPanelManager.Instance != null && CLIPanelManager.Instance.CurrentDevice == placedRouter,
                hintNudge: "The router is the boxy device already on the table.",
                hintSpecific: "Right-click it — left-click is for picking it up and moving it.",
                hintAnswer: "Right-click the placed Router.");
            tm.RegisterTask("Enter Privileged EXEC mode", () => RouterMode() is RouterCLI.Mode mode && mode != RouterCLI.Mode.UserExec,
                hintNudge: "Check what mode you're in — the prompt tells you (`>` vs `#`).",
                hintSpecific: "Type `enable` at the `Router>` prompt.",
                hintAnswer: "`enable`");
            tm.RegisterTask("Enter Global Configuration mode", () =>
                RouterMode() is RouterCLI.Mode.GlobalConfig or RouterCLI.Mode.InterfaceConfig,
                hintNudge: "From Privileged EXEC, there's a command that starts with \"configure\".",
                hintSpecific: "Type `configure terminal` (the short form `conf t` also works).",
                hintAnswer: "`configure terminal`");
            tm.RegisterTask("Select the first interface", () => RouterInterfacePort() == 0,
                hintNudge: "From Global Config, type `interface` followed by a port name.",
                hintSpecific: "The first port is named `eth0`.",
                hintAnswer: "`interface eth0`");
            tm.RegisterTask("Step back out to Privileged EXEC", () =>
                reachedInterfaceConfigOnce && RouterMode() == RouterCLI.Mode.PrivilegedExec,
                hintNudge: "One command jumps all the way back to Privileged EXEC from anywhere in config.",
                hintSpecific: "It's not `exit` (that only goes up one level at a time) — try `end`.",
                hintAnswer: "`end`");
            // Checkpoint: no outline this time (see BuildStages) and a
            // different interface number than the one just practiced, so
            // this only completes if the player genuinely retained the
            // configure terminal -> interface ethN sequence.
            tm.RegisterTask("Checkpoint: reach interface eth1 on your own", () => RouterInterfacePort() == 1,
                hintNudge: "You're in Privileged EXEC — the same spot you were in right after `enable`. Retrace your steps from there.",
                hintSpecific: "Go back into Global Config, then pick an interface — same as last time, just port 1 instead of port 0.",
                hintAnswer: "`configure terminal`, then `interface eth1`");
        }

        // Hold off on this level's first guide annotation until the whole
        // opening sequence has actually finished — see GameBootstrap.
        // IntroSequenceReady.
        while (!GameBootstrap.IntroSequenceReady) yield return null;

        // Via HandleTasksChanged rather than EnterStage(0) directly, same as
        // Level2Guide/Level4Guide — stage 0 in practice here, since this
        // level never resumes progress (see DiscardRestoredProgress above).
        HandleTasksChanged();
    }

    void OnDestroy()
    {
        if (TaskManager.Instance != null) TaskManager.Instance.OnTasksChanged -= HandleTasksChanged;
        if (CLIPanelManager.Instance != null) CLIPanelManager.Instance.OnPanelOpened -= HandleCliOpened;
    }

    void Update()
    {
        if (reachedInterfaceConfigOnce || placedRouter == null || CLIPanelManager.Instance == null) return;
        if (RouterMode() == RouterCLI.Mode.InterfaceConfig) reachedInterfaceConfigOnce = true;
    }

    // ── Guide stages ──────────────────────────────────────────────────────────

    void BuildStages()
    {
        stages = new List<GuideStage>
        {
            // 0 — Just open the CLI. Not a sidebar action (right-click opens
            // it) — a world marker points at the pre-placed router.
            new() { Text = () => "Right-click the Router to open its command line.",
                     WorldMarker = () => placedRouter != null ? placedRouter.VisualTopPoint() : (Vector3?)null,
                     WorldMarkerDevice = () => placedRouter,
                     MarkerGesture = MouseGesture.RightClick },

            // 1 — enable
            new() { Text = () => "Type <b>enable</b> (or click it in the <b>Commands</b> list and press <b>Run</b>) to enter Privileged EXEC mode. Most commands are locked until you do — it's how a real router keeps casual users from breaking its config. (Tip: rest your mouse on any command to see what it does.)",
                     ShowCliHighlight = true, HighlightCommand = "enable" },

            // 2 — configure terminal
            new() { Text = () => "Type <b>configure terminal</b> to enter Global Config — settings you make here apply to the whole router. A single port's own settings live one level deeper, inside Interface Config.",
                     ShowCliHighlight = true, HighlightCommand = "configure terminal" },

            // 3 — interface eth0
            new() { Text = () => "Type <b>interface eth0</b> to configure the first port. (Tip: press <key>Tab</key> to auto-complete, or click the command in the <b>Commands</b> list.)",
                     ShowCliHighlight = true, HighlightCommand = "interface eth<N>" },

            // 4 — end, back out.
            new() { Text = () => "Type <b>end</b> to jump back to Privileged EXEC. You've now walked the whole map: User EXEC → Privileged EXEC → Global Config → Interface Config.",
                     ShowCliHighlight = true, HighlightCommand = "end" },

            // 5 — Checkpoint: no outline highlight (ShowCliHighlight left
            // false) and a different interface number, so the player has to
            // genuinely recall the sequence rather than follow the glow.
            new() { Text = () => "<b>Checkpoint</b> — no outlines this time. You're back in Privileged EXEC — get the Router into <b>interface eth1</b>'s configuration mode on your own.",
                     WorldMarker = () => placedRouter != null ? placedRouter.VisualTopPoint() : (Vector3?)null,
                     WorldMarkerDevice = () => placedRouter,
                     MarkerGesture = MouseGesture.RightClick },
        };
    }

    // Nothing on the sidebar is needed for this level — the Router is
    // already placed by Level3SceneSetup and there's no cabling or
    // addressing to do, just CLI navigation. Mirrors TutorialManager.
    // HideUnneededButtons.
    void HideUnneededButtons()
    {
        // Ctrl+Z is a tool too — see UndoManager.AvailableInThisLevel.
        if (UndoManager.Instance != null) UndoManager.Instance.AvailableInThisLevel = false;

        var ui = NetworkUIManager.Instance;
        if (ui == null) return;

        SidebarButtonLock.SetActive(ui.GetDeviceButton(DeviceType.PC), false);
        SidebarButtonLock.SetActive(ui.GetDeviceButton(DeviceType.Switch), false);
        SidebarButtonLock.SetActive(ui.GetDeviceButton(DeviceType.Router), false);
        SidebarButtonLock.SetActive(ui.TableButton, false);
        SidebarButtonLock.SetActive(ui.CableButton, false);
        SidebarButtonLock.SetActive(ui.SendPacketButton, false);
        SidebarButtonLock.SetActive(ui.RemoveCableButton, false);
        SidebarButtonLock.SetActive(ui.RemoveDeviceButton, false);
        SidebarButtonLock.SetActive(ui.PingButton, false);
    }

    void EnterStage(int index)
    {
        stageIndex = index;

        bool inRange = stages != null && index >= 0 && index < stages.Count;
        var stage = inRange ? stages[index] : null;

        if (stage == null)
        {
            // All stages done — HandleTasksChanged hands off to
            // LevelCompletePanel; nothing left to point at.
            TutorialUIManager.Instance?.Hide();
            CLIPanelManager.Instance?.ClearHighlights();
            return;
        }

        // This level hides the whole sidebar (see HideUnneededButtons), so the
        // callout doesn't need the default sidebar-clearing gap — pull it in
        // closer to the left edge instead of leaving that space empty.
        TutorialUIManager.Instance?.Show(stage.Text.Invoke(), null, buttonLabel: null, offsetX: 220f, progress: (index + 1, stages.Count));

        TutorialUIManager.Instance?.HideWorldMarkers();
        Vector3? marker = stage.WorldMarker?.Invoke();
        if (marker.HasValue) TutorialUIManager.Instance?.ShowWorldMarker(0, marker.Value, stage.WorldMarkerDevice?.Invoke(), stage.MarkerGesture);

        ApplyCliHighlight(stage);
    }

    // Outlines + a chip → Command Queue drag trail for the stage's command.
    // The trail resolves itself live (see CLIPanelManager.HighlightedCommandDragRoute),
    // so it only appears while that command actually has a chip to drag.
    // Also re-run whenever the panel (re)opens — Open() clears highlights, so
    // closing and reopening the CLI mid-stage would otherwise lose them.
    void ApplyCliHighlight(GuideStage stage)
    {
        var cli = CLIPanelManager.Instance;
        if (stage != null && stage.ShowCliHighlight)
        {
            cli?.SetHighlightedCommand(stage.HighlightCommand);
            cli?.HighlightPrompt(true);
            if (cli != null) TutorialUIManager.Instance?.ShowDragGuide(cli.HighlightedCommandDragRoute);
        }
        else
        {
            cli?.ClearHighlights();
            TutorialUIManager.Instance?.HideDragGuide();
        }
    }

    void HandleCliOpened(NetworkDevice device)
    {
        if (stages != null && stageIndex >= 0 && stageIndex < stages.Count) ApplyCliHighlight(stages[stageIndex]);
    }

    // ── Stage advance + completion ───────────────────────────────────────────
    // Rides directly on TaskManager's own IsComplete tracking rather than
    // re-evaluating the same Func<bool> conditions a second time here —
    // registration order in InitNextFrame matches stage order in BuildStages,
    // so "N tasks complete" IS "we're on stage N".

    void HandleTasksChanged()
    {
        // Task registration fires this long before the entrance has
        // finished — stage callouts wait for it (see InitNextFrame).
        if (!GameBootstrap.IntroSequenceReady) return;

        var tasks = TaskManager.Instance.Tasks;
        int stage = TaskManager.Instance.CurrentTaskIndex;
        if (stage != stageIndex) EnterStage(stage);

        if (completionHandled) return;
        if (tasks.Count == 0 || stage < tasks.Count) return;

        completionHandled = true;
        TutorialUIManager.Instance?.Hide();
        CLIPanelManager.Instance?.ClearHighlights();

        int stars = TaskManager.Instance.ComputeStars();
        SaveManager.MarkLevelCompleted(3);
        SaveManager.SetStars(3, stars);
        AutoSaveManager.Instance?.SaveNow();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "You navigated the router's command line from memory, no hints needed. Next, you'll use these same moves to actually address one.",
            "Back to Level Select",
            () =>
            {
                LoadingScreenManager.ExitToLevelSelect();
            },
            stars);
    }

    RouterCLI.Mode? RouterMode()
    {
        if (placedRouter == null || CLIPanelManager.Instance == null) return null;
        return CLIPanelManager.Instance.GetOrCreateSession(placedRouter).CurrentMode;
    }

    // Null unless currently inside Interface Config — used for both stage 3
    // (expects eth0) and the checkpoint stage (expects eth1), so neither one
    // fires on the other's interface.
    int? RouterInterfacePort()
    {
        if (placedRouter == null || CLIPanelManager.Instance == null) return null;
        RouterCLI cli = CLIPanelManager.Instance.GetOrCreateSession(placedRouter);
        return cli.CurrentMode == RouterCLI.Mode.InterfaceConfig ? cli.CurrentInterfacePort : (int?)null;
    }
}
