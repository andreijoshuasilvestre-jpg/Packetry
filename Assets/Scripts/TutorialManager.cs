using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Guided onboarding: trims the sidebar down to PC, Switch, Table, and Cable
/// (everything else hidden outright), then — one action at a time, with every
/// other core button locked out until the current one is done — walks the
/// player through placing a table, placing and rotating a PC, placing a
/// second PC and a Switch, learning what ports are, and cabling everything
/// together. Once the mini-network exists, the rest of the toolset reappears
/// and the tutorial gets out of the way.
///
/// Reuses TaskManager's Func&lt;bool&gt; CheckCondition polling idiom rather than
/// inventing a parallel one, and reuses DragDropManager.OnDeviceDropped /
/// CableManager.OnCableCreated / TableManager.OnTablePlaced for step detection
/// instead of scanning the scene. Deliberately does NOT register with
/// InteractionMode.CancelOthers — it never claims an exclusive input mode of
/// its own, it only locks individual sidebar buttons and draws non-blocking
/// overlays, so it has nothing for that mutual-exclusion gate to cancel.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    // Set by MainMenuBootstrap's temporary "Skip Tutorial (Dev)" button before
    // loading the gameplay scene — a developer convenience for testing new
    // features without playing through the tutorial every run. Static fields
    // survive the menu -> gameplay scene load within the same Play session.
    // Meant to be removed along with that button once no longer needed.
    public static bool SkipOnNextLoad;

    // Minimum rotation (degrees) the "rotate the device" step requires before
    // it counts as done — enough to prove a deliberate R+scroll gesture
    // happened, not just an accidental nudge.
    const float RotationThresholdDegrees = 25f;

    // How far toward IsometricCamera.MinDistance the player must scroll before
    // the "zoom in" step counts as done — 0 = no requirement (started at
    // MaxDistance-ish), 1 = fully zoomed to MinDistance. 0.5 asks for a real,
    // deliberate zoom rather than one scroll notch.
    const float ZoomRequiredFraction = 0.5f;

    // Minimum distance (world units) the "drag a placed device" step requires
    // the first PC to have moved before it counts as done — half a grid cell
    // (DragDropManager.GridCellSize is 1 by default), enough to prove a real
    // drag happened rather than counting position noise from the rotate step.
    const float DragThresholdDistance = 0.5f;

    // Minimum distance (world units) the camera's look target must move before
    // the "pan with WASD" step counts as done — same idea as
    // DragThresholdDistance, just measured against IsometricCamera's
    // TargetLookTarget instead of a device's position. At the default
    // keyboardPanSpeed (8 units/sec) this is under half a second of holding a
    // key, enough to prove a deliberate press without feeling like a chore.
    const float PanThresholdDistance = 2f;

    // How far below the devices/cable compound button's own row the callout gets
    // nudged for the one step that leaves that button's flyout open to the player
    // (TutorialStep.TeachDeviceFlyout) — clears the flyout's row height (one
    // button tall) so the callout never sits underneath/behind the flyout it's
    // describing. See TutorialUIManager.Show's offsetY param.
    const float TeachFlyoutCalloutOffsetY = -200f;

    private bool qPressedThisStep;
    private bool ePressedThisStep;
    private Vector3? panBaselinePos;
    // True once the current step's marker B (and TextAfterGate, if any) has
    // been revealed — see TutorialStep.WorldMarkerBGate / RevealWorldMarkerB.
    // Reset every EnterStep so each step gets its own fresh reveal.
    private bool worldMarkerBRevealed;

    public class TutorialStep
    {
        public string Text;
        public DeviceType? HighlightDevice; // sidebar device button to highlight/unlock, if any
        public bool HighlightCable;          // highlight/unlock the Cable button instead
        public bool HighlightTable;          // highlight/unlock the Table button instead
        // True only for the step that teaches NetworkUIManager's devices/cable
        // compound button's right-click flyout (see BuildDevicesCableCompound):
        // leaves that button fully unrestricted (any of the four tools reachable,
        // flyout included) instead of forcing HighlightDevice/HighlightCable's
        // tool directly, and nudges the callout below the flyout's row so the
        // two never overlap — see EnterStep's offsetY.
        public bool TeachDeviceFlyout;
        // Pure camera-control steps (zoom/rotate the view) — nothing is ever
        // placed yet this early, but this also locks out selecting/dragging
        // any already-placed device, so a step whose whole point is "learn to
        // move the camera" can't be sidetracked into anything else. See
        // DragDropManager.Update's IsCameraOnlyStep gate.
        public bool CameraOnly;
        public Func<bool> CheckCondition;    // null = step needs an explicit Next/Continue click
        public Action OnEnter;
        // Evaluated fresh every time this step is entered (not baked at
        // BuildSteps time) since device/table positions only exist once the
        // player has actually placed them — see EnterStep.
        public Func<(Vector3? a, Vector3? b)> WorldMarkers;
        // Parallel to WorldMarkers — set only on steps whose marker(s) point
        // at an already-placed device, so ShowWorldMarker can turn that
        // marker's pulse into a translucent echo of the device's own model
        // instead of a generic circle. Null entries (the default for every
        // other step, e.g. table corners or a not-yet-placed device's drop
        // hint) stay circular.
        public Func<(NetworkDevice a, NetworkDevice b)> WorldMarkerDevices;
        // Optional gate on WHEN marker B (the WorldMarkers.b spot) actually
        // reveals itself. Null (the default) reveals it immediately at step
        // entry, same as before. Set this when marker B is a destination the
        // player shouldn't be shown until they've actually started the
        // gesture it belongs to (e.g. "drag your PC" — the drop-target pulse
        // should appear once the device is actually picked up, not before,
        // so pickup and "here's where to go" read as two distinct beats
        // instead of dumping everything on screen at once). Re-polled every
        // frame in Update() until it returns true, then stays revealed for
        // the rest of the step.
        public Func<bool> WorldMarkerBGate;
        // Callout text to swap in the instant WorldMarkerBGate flips true —
        // null means the callout keeps showing Text throughout. Lets a step
        // read as two beats ("pick it up" -> once picked up, "here's where
        // to drop it") without needing two separate TutorialStep entries.
        public string TextAfterGate;
    }

    public bool IsActive { get; private set; }

    private List<TutorialStep> steps;
    private int stepIndex = -1;

    private bool tablePlaced;
    private Table placedTable;
    private int placedPCCount;
    private int placedSwitchCount;
    private int cablesConnectedToSwitch;
    private readonly HashSet<Cable> countedCables = new();
    private float? rotationBaselineY;

    // The first PC the player places — captured off OnDeviceDropped's first
    // fresh (non-reposition) PC so the "you can drag devices too" step can
    // point its world-marker indicator at it, and its position can be polled
    // to detect the actual drag.
    private NetworkDevice firstPlacedPC;
    private Vector3? dragBaselinePos;

    // The second PC and the Switch — captured the same way as firstPlacedPC
    // so the cabling steps can point their world-marker at each device's
    // actual placed (and possibly since-dragged) position instead of the
    // fixed table-relative drop zone used while it was still being placed
    // (see TableMarkerPos, which those spots reuse before the device exists).
    private NetworkDevice secondPlacedPC;
    private NetworkDevice placedSwitch;

    // Step-aware, not just "is this device type taught at all this tutorial" —
    // DevicePalette's in-world PC/Switch/Router models sit in the scene the
    // whole time and raycast straight into DragDropManager.StartDragging,
    // bypassing the sidebar buttons entirely (see ApplyStepButtonLock, which
    // only dims/disables those buttons). Without this being step-aware, a
    // player could world-palette-click a PC during e.g. the "place a table"
    // or "rotate your PC" steps even though the sidebar's PC button was
    // locked — this is the one gate both entry points share, so it has to be
    // the one that actually enforces "only the current step's device."
    public bool IsDeviceAllowed(DeviceType t)
    {
        if (!IsActive) return true;
        if (steps == null || stepIndex < 0 || stepIndex >= steps.Count) return false;
        var step = steps[stepIndex];
        // The flyout-teaching step leaves NetworkUIManager's compound button
        // genuinely unrestricted (see ApplyStepButtonLock) — this gate has to
        // agree, or picking PC/Router from the flyout would swap the button's
        // face but silently fail to start a ghost (StartDragging no-ops here).
        if (step.TeachDeviceFlyout) return true;
        return step.HighlightDevice == t;
    }

    // True while the current step is a pure camera-control step (see
    // TutorialStep.CameraOnly) — read by DragDropManager to lock out palette
    // picks and placed-device select/drag for the duration, so "learn to zoom
    // and rotate" can't be sidetracked into placing or dragging something.
    public bool IsCameraOnlyStep =>
        IsActive && steps != null && stepIndex >= 0 && stepIndex < steps.Count && steps[stepIndex].CameraOnly;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // This guided walkthrough is Level 1's onboarding specifically — any
        // other level (see LevelManager) gets the full toolset from frame one,
        // same as the dev-only SkipOnNextLoad flow, and supplies its own
        // guided objectives via TaskManager instead (see Level2Guide, Level3Guide).
        bool skip = SkipOnNextLoad
                 || (LevelManager.Instance != null && LevelManager.Instance.CurrentLevel != 1);
        if (skip)
        {
            SkipOnNextLoad = false;
            IsActive = false;
            return; // full toolset from frame one, nothing restricted, nothing built
        }

        StartCoroutine(InitNextFrame());
    }

    // NetworkUIManager builds its sidebar buttons in its own Start() (not
    // Awake()) — deferring one frame guarantees it has finished before this
    // reads button RectTransforms, instead of depending on unspecified
    // Start()-call ordering between components created in the same
    // GameBootstrap.Awake() call.
    IEnumerator InitNextFrame()
    {
        yield return null;

        BuildSteps();

        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped += HandleDeviceDropped;
        if (CableManager.Instance != null) CableManager.Instance.OnCableCreated += HandleCableCreated;
        if (TableManager.Instance != null) TableManager.Instance.OnTablePlaced += HandleTablePlaced;

        IsActive = true;
        HideUnneededButtons();
        EnterStep(0);
    }

    void OnDestroy()
    {
        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped -= HandleDeviceDropped;
        if (CableManager.Instance != null) CableManager.Instance.OnCableCreated -= HandleCableCreated;
        if (TableManager.Instance != null) TableManager.Instance.OnTablePlaced -= HandleTablePlaced;
    }

    void Update()
    {
        if (!IsActive || steps == null || stepIndex < 0 || stepIndex >= steps.Count) return;

        var step = steps[stepIndex];

        // Marker B's reveal (and the matching callout text swap) can be held
        // back behind a live condition instead of appearing the instant the
        // step starts — see WorldMarkerBGate. Polled every frame until it
        // fires once; after that it stays revealed for the rest of the step.
        if (!worldMarkerBRevealed && step.WorldMarkerBGate != null && step.WorldMarkerBGate())
            RevealWorldMarkerB(step);

        if (step.CheckCondition != null && step.CheckCondition())
            AdvanceStep();
    }

    // Deferred reveal of marker B — see WorldMarkerBGate on TutorialStep and
    // the call site in Update(). Re-invokes WorldMarkers/WorldMarkerDevices
    // rather than reusing whatever EnterStep captured, since the destination
    // may depend on state (e.g. a table's FinalCenter) that wasn't settled
    // yet at step-entry time.
    void RevealWorldMarkerB(TutorialStep step)
    {
        worldMarkerBRevealed = true;

        var markers = step.WorldMarkers?.Invoke();
        if (markers.HasValue && markers.Value.b.HasValue)
        {
            var devices = step.WorldMarkerDevices?.Invoke();
            TutorialUIManager.Instance?.ShowWorldMarker(1, markers.Value.b.Value, devices?.b);
        }

        if (step.TextAfterGate != null)
            TutorialUIManager.Instance?.UpdateText(step.TextAfterGate);
    }

    // ── Step definitions ─────────────────────────────────────────────────────

    void BuildSteps()
    {
        steps = new List<TutorialStep>
        {
            // 0 — Welcome
            new() { Text = "Welcome! Let's build your first network together, one step at a time. Click Next to begin." },

            // 1 — Zoom in: no sidebar action, pure camera control, taught before
            // anything else since every later step is easier to see and click
            // accurately once the player isn't stuck fully zoomed out.
            new() { Text = "Let's get a closer look first. Scroll your mouse wheel to zoom in on the play area.",
                     CameraOnly = true,
                     CheckCondition = CheckZoomedIn },

            // 1b — Pan tip: no sidebar action, pure camera control. Gated the
            // same way as the zoom/rotate steps (see CheckPanned) — every
            // control-teaching step requires the player to actually do the
            // thing rather than click past it, so nothing here defaults to a
            // "Next" button.
            new() { Text = "You can also pan the camera around. Try it — hold <key>WASD</key> to move the view.",
                     CameraOnly = true,
                     OnEnter = () => panBaselinePos = null,
                     CheckCondition = CheckPanned },

            // 2 — Rotate the view: no sidebar action, pure camera control. Requires
            // BOTH keys (not just one pressed twice) so the player deliberately
            // tries rotating each direction, not just discovers one by accident.
            new() { Text = "You can also rotate your view. Press <key>Q</key> to turn left and <key>E</key> to turn right — try both.",
                     CameraOnly = true,
                     OnEnter = () => { qPressedThisStep = false; ePressedThisStep = false; },
                     CheckCondition = CheckCameraRotated },

            // 3 — Place a table (two clicks: opposite corners), with pulsing
            // click-zone circles marking exactly where to click.
            new() { Text = "Every device needs somewhere to sit. Click <b>Table</b>, then click near the first glowing circle, then the second, to size a table between them.",
                     HighlightTable = true,
                     CheckCondition = () => tablePlaced,
                     WorldMarkers = () => (TableCornerA, TableCornerB) },

            // 4 — Place the first PC
            new() { Text = "Click <b>PC</b>, move your mouse over the table, then left-click to drop it there. It must land inside the table's glowing outline.",
                     HighlightDevice = DeviceType.PC,
                     CheckCondition = () => placedPCCount >= 1,
                     WorldMarkers = () => (PcMarkerPos(-1), null) },

            // 5 — Exit sticky placement mode: dropping a PC leaves you still
            // "holding" the next one. Two ways out, both taught here and both
            // satisfied by the same condition (DragDropManager.IsDragging goes
            // false either way): right-click cancels the pending ghost outright,
            // or left-clicking the placed PC directly cancels it AND hands that
            // same click straight to the select gesture (see DragDropManager.
            // Update's isDragging branch), skipping straight to "selected."
            new() { Text = "You're still holding another PC. Right-click anywhere to cancel it — or left-click your placed PC directly to select it in one step.",
                     CheckCondition = () => DragDropManager.Instance != null && !DragDropManager.Instance.IsDragging },

            // 6 — Rotate it: no sidebar action, pure 3D-viewport control. If the
            // previous step was resolved by left-clicking the PC directly, it's
            // already selected/highlighted here — the wording covers both cases.
            new() { Text = "Now rotate it: left-click your PC to select it if it isn't already glowing cyan, then hold <key>R</key> and scroll the mouse wheel.",
                     OnEnter = () => rotationBaselineY = null,
                     CheckCondition = CheckRotationDone },

            // 7 — Dragging placed devices: same click-and-hold gesture that picks a
            // device up for repositioning (see DragDropManager.HandlePlacedDevicePress
            // / PickUpPlacedDevice), taught here with the marker pulsing right on
            // the PC just rotated so there's no ambiguity which device to grab.
            // The destination marker (WorldMarkers.b) is deliberately held back
            // until the player has actually picked the device up — see
            // WorldMarkerBGate — so "grab it" and "here's where to drop it" land
            // as two separate beats instead of both showing up immediately.
            new() { Text = "Placed devices can be moved too. Click and hold your PC to pick it up.",
                     TextAfterGate = "Now drag it to the glowing spot, then click again to place it down.",
                     OnEnter = () => dragBaselinePos = null,
                     CheckCondition = CheckDeviceDragged,
                     WorldMarkers = () => (FirstPlacedPcMarkerPos(), DragDestinationMarkerPos()),
                     WorldMarkerDevices = () => (firstPlacedPC, null),
                     WorldMarkerBGate = () => DragDropManager.Instance != null && DragDropManager.Instance.IsRepositioning },

            // 8 — Second PC
            new() { Text = "Click <b>PC</b> again, then place a second one on the table.",
                     HighlightDevice = DeviceType.PC,
                     CheckCondition = () => placedPCCount >= 2,
                     WorldMarkers = () => (PcMarkerPos(1), null) },

            // 9 — Switch. No cancel step needed here: clicking the Switch button
            // while a PC ghost is still sticky-placing switches straight to it
            // (StartDragging cancels the stale ghost instead of no-oping) in one
            // click, so right-click is only ever optional here, not required.
            new() { Text = "That button now holds every device. Right-click it to flip through the others, then pick <b>Switch</b> and place one on the table too — it's what connects your PCs together.",
                     HighlightDevice = DeviceType.Switch,
                     TeachDeviceFlyout = true,
                     CheckCondition = () => placedSwitchCount >= 1,
                     WorldMarkers = () => (SwitchMarkerPos(), null) },

            // 9 — Ports, pure info step.
            new() { Text = "Every device has one or more <b>ports</b> — the small connection points cables plug into. If a device has more than one, a popup will ask which one to use when you connect a cable." },

            // 10 — First cable, broken into one tip per click instead of one
            // "do all of this" blob — see CableManager.SourceDevice /
            // PickerOpenForDevice, exposed specifically so each sub-step here
            // can tell which part of the click-device -> pick-port ->
            // click-device -> pick-port sequence the player has actually
            // reached. HighlightCable stays true through every sub-step (not
            // just the first) so the Cable button stays lit and clickable the
            // whole time — if the player right-clicks to cancel partway
            // through, they can click it again to restart without the
            // sidebar lock (see ApplyStepButtonLock) leaving it dimmed.
            //
            // 10a — Click the Cable button.
            new() { Text = "Click <b>Cable</b> to start wiring your network.",
                     HighlightCable = true,
                     CheckCondition = () => CableManager.Instance != null && CableManager.Instance.IsConnecting },

            // 10b — Click the first PC. True the instant its port list pops
            // up (PickerOpenForDevice set, SourceDevice not yet — see
            // CableManager.ShowPicker), so this doesn't require the player to
            // click firstPlacedPC specifically: clicking any device opens its
            // picker the same way, and the very next sub-step's condition
            // still only fires once a source is genuinely chosen, so nothing
            // here can be satisfied by accident.
            new() { Text = "Now click your first PC — that's where the cable starts.",
                     HighlightCable = true,
                     CheckCondition = () => CableManager.Instance != null
                                          && CableManager.Instance.PickerOpenForDevice != null
                                          && CableManager.Instance.SourceDevice == null,
                     WorldMarkers = () => (FirstPlacedPcMarkerPos(), null),
                     WorldMarkerDevices = () => (firstPlacedPC, null) },

            // 10c — Pick a port from the popup. No world marker: the popup
            // itself is the focus, and CableManager already lights up every
            // free port on the model gold for the duration (see
            // NetworkDevice.HighlightFreePorts) — a marker here would just be
            // redundant clutter over it.
            new() { Text = "A list of free ports pops up — click one (like <b>eth0</b>) to plug the cable in there.",
                     HighlightCable = true,
                     CheckCondition = () => CableManager.Instance != null && CableManager.Instance.SourceDevice != null },

            // 10d — Click the Switch. Source is already chosen at this point,
            // so a second picker opening now can only mean the target device
            // was just clicked.
            new() { Text = "Now click the <b>Switch</b> to finish the connection.",
                     HighlightCable = true,
                     CheckCondition = () => CableManager.Instance != null
                                          && CableManager.Instance.PickerOpenForDevice != null
                                          && CableManager.Instance.SourceDevice != null,
                     WorldMarkers = () => (PlacedSwitchMarkerPos(), null),
                     WorldMarkerDevices = () => (placedSwitch, null) },

            // 10e — Pick a port on the Switch, completing the cable.
            new() { Text = "Pick a free port on the Switch the same way to complete the cable.",
                     HighlightCable = true,
                     CheckCondition = () => cablesConnectedToSwitch >= 1 },

            // 11 — Second cable: same four clicks, taught already, so this
            // stays one step — but still marks both real devices to click
            // (not the old fixed table drop-zone) so there's no ambiguity
            // about which PC "your second PC" means once it's had a chance to
            // be dragged around.
            new() { Text = "Click <b>Cable</b> again, then repeat: click your second PC, pick a port, click the Switch, and pick a port there too.",
                     HighlightCable = true,
                     CheckCondition = () => cablesConnectedToSwitch >= 2,
                     WorldMarkers = () => (SecondPlacedPcMarkerPos(), PlacedSwitchMarkerPos()),
                     WorldMarkerDevices = () => (secondPlacedPC, placedSwitch) },

            // 12 — Completion
            new() { Text = "Your network is live! Nicely done.",
                     OnEnter = CompleteTutorial },
        };
    }

    bool CheckCameraRotated()
    {
        if (Keyboard.current == null) return false;
        if (Keyboard.current.qKey.wasPressedThisFrame) qPressedThisStep = true;
        if (Keyboard.current.eKey.wasPressedThisFrame) ePressedThisStep = true;
        return qPressedThisStep && ePressedThisStep;
    }

    bool CheckZoomedIn()
    {
        var cam = IsometricCamera.Instance;
        if (cam == null) return false;

        float threshold = Mathf.Lerp(cam.MaxDistance, cam.MinDistance, ZoomRequiredFraction);
        return cam.TargetDistance <= threshold;
    }

    bool CheckPanned()
    {
        var cam = IsometricCamera.Instance;
        if (cam == null) return false;

        Vector3 current = cam.TargetLookTarget;
        if (panBaselinePos == null) { panBaselinePos = current; return false; }

        return Vector3.Distance(current, panBaselinePos.Value) >= PanThresholdDistance;
    }

    bool CheckRotationDone()
    {
        var dd = DragDropManager.Instance;
        if (dd == null || !dd.HasHighlightedDevice) return false;

        float currentY = dd.HighlightedDevice.transform.eulerAngles.y;
        if (rotationBaselineY == null) { rotationBaselineY = currentY; return false; }

        float delta = Mathf.Abs(Mathf.DeltaAngle(rotationBaselineY.Value, currentY));
        return delta >= RotationThresholdDegrees;
    }

    bool CheckDeviceDragged()
    {
        if (firstPlacedPC == null) return false;

        Vector3 currentPos = firstPlacedPC.transform.position;
        if (dragBaselinePos == null) { dragBaselinePos = currentPos; return false; }

        // Mid-drag, the device's live position tracks the cursor every frame
        // and crosses DragThresholdDistance almost the instant the player
        // starts moving the mouse — polling that alone would advance the
        // tutorial while the PC is still stuck to the cursor, before it's
        // actually been dropped anywhere. Waiting for IsRepositioning to go
        // back false means the player has genuinely clicked/released to
        // place it down first.
        var dd = DragDropManager.Instance;
        if (dd != null && dd.IsRepositioning) return false;

        return Vector3.Distance(currentPos, dragBaselinePos.Value) >= DragThresholdDistance;
    }

    // Points the drag-tip step's marker straight at the first PC itself
    // (WorldPulseMarker.Show already lifts it clear of the tabletop) rather
    // than a fixed table-relative slot, so it tracks wherever that PC
    // actually ended up after the earlier place/rotate steps.
    Vector3? FirstPlacedPcMarkerPos() => firstPlacedPC != null ? firstPlacedPC.VisualTopPoint() : null;

    // Same idea as FirstPlacedPcMarkerPos, for the cabling steps — these point
    // at wherever the device actually ended up, not a fixed table slot, so the
    // marker still lands correctly even if the player dragged it after placing.
    Vector3? SecondPlacedPcMarkerPos() => secondPlacedPC != null ? secondPlacedPC.VisualTopPoint() : null;

    // Destination hint for the "drag your PC" step — a fixed spot on the
    // table, clearly apart from both the PC's own starting corner (so the
    // drag actually has to go somewhere) and the second PC's future corner
    // (see TableMarkerPos sign=+1, used two steps later), so this marker and
    // that one never sit on top of each other.
    Vector3? DragDestinationMarkerPos()
    {
        if (placedTable == null) return null;
        Vector3 center = placedTable.FinalCenter;
        float y = placedTable.topSurfaceY + 0.02f;
        float offsetX = placedTable.width * 0.3f;
        float offsetZ = placedTable.length * 0.12f;
        return new Vector3(center.x - offsetX, y, center.z - offsetZ);
    }
    Vector3? PlacedSwitchMarkerPos() => placedSwitch != null ? placedSwitch.VisualTopPoint() : null;

    // ── World-marker placement math ──────────────────────────────────────────
    // Fixed opposite corners for the table step (comfortably inside the play
    // area's ±7.5 half-extent, centered at the origin so later device markers
    // — computed from the table's actual footprint once placed — land near
    // the middle of the screen too). Widened from the original 6x4 so the
    // diagonal PC/switch layout below (see TableMarkerPos) has comfortable
    // room to spread the two PCs apart instead of crowding the middle.
    static readonly Vector3 TableCornerA = new Vector3(-3.5f, 0f, 2.5f);
    static readonly Vector3 TableCornerB = new Vector3(3.5f, 0f, -2.5f);

    // The table-corner markers get a distinct color from the device-drop
    // markers (WorldPulseMarker's own warm-gold default) — a different color
    // reads as "a different kind of target," reinforcing that this click
    // starts a table rather than dropping a device.
    static readonly Color TableMarkerColor = new Color(0.25f, 0.9f, 1f, 1f);

    // Three drop-hint spots laid out diagonally instead of in one straight
    // row: the switch sits toward the near edge, and the two PCs sit at the
    // opposite two corners — sign < 0 = far-left PC, > 0 = far-right PC, 0 =
    // switch. Putting the switch on its own row means the two finished
    // cables (PC → switch) run along two distinct diagonals instead of
    // overlapping along one straight line the way three colinear points
    // would, so both cables actually read as separate cables once connected.
    // Sits just above the tabletop surface (not the ground) so it reads as
    // "drop it here on the table," not "drop it on the floor."
    Vector3? PcMarkerPos(int sign) => TableMarkerPos(sign);
    Vector3? SwitchMarkerPos() => TableMarkerPos(0);

    Vector3? TableMarkerPos(int sign)
    {
        if (placedTable == null) return null;
        // FinalCenter, not transform.position — OnTablePlaced (which set
        // placedTable) fires while the table's build-reveal animation is still
        // moving transform.position from the clicked corner toward its resting
        // spot, so reading transform.position here froze the marker at that
        // transient corner position instead of the table's actual center.
        Vector3 center = placedTable.FinalCenter;
        float y = placedTable.topSurfaceY + 0.02f;

        float offsetX = placedTable.width * 0.3f;
        float offsetZ = placedTable.length * 0.3f;

        if (sign == 0) // switch — near edge, centered
            return new Vector3(center.x, y, center.z - offsetZ);

        // PCs — far corners, diagonally opposite the switch
        return new Vector3(center.x + sign * offsetX, y, center.z + offsetZ);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    void HandleTablePlaced(Table table)
    {
        tablePlaced = true;
        placedTable = table;
    }

    void HandleDeviceDropped(NetworkDevice device, bool isNewDevice)
    {
        // Only a fresh palette placement counts toward "placed a PC/Switch" —
        // repositioning something already on the field (isNewDevice = false)
        // fires this same event but must not double-count it, or dragging the
        // first PC around (see the new "you can drag devices" step) would
        // silently satisfy the "place a second PC" step without one actually
        // existing.
        if (!isNewDevice) return;

        if (device.deviceType == DeviceType.PC)
        {
            placedPCCount++;
            if (firstPlacedPC == null) firstPlacedPC = device;
            else if (secondPlacedPC == null) secondPlacedPC = device;
        }
        else if (device.deviceType == DeviceType.Switch)
        {
            placedSwitchCount++;
            if (placedSwitch == null) placedSwitch = device;
        }
    }

    void HandleCableCreated(Cable cable)
    {
        if (cable == null || !countedCables.Add(cable)) return;

        NetworkDevice a = cable.DeviceA;
        NetworkDevice b = cable.DeviceB;
        if (a == null || b == null) return;

        bool isPcSwitchPair = (a.deviceType == DeviceType.PC && b.deviceType == DeviceType.Switch)
                           || (a.deviceType == DeviceType.Switch && b.deviceType == DeviceType.PC);
        if (isPcSwitchPair) cablesConnectedToSwitch++;
    }

    // ── Step flow ─────────────────────────────────────────────────────────────

    void EnterStep(int index)
    {
        stepIndex = index;
        if (stepIndex < 0 || stepIndex >= steps.Count)
        {
            TutorialUIManager.Instance?.Hide();
            return;
        }

        var step = steps[stepIndex];
        step.OnEnter?.Invoke();
        // OnEnter may itself end the tutorial (see the completion step calling
        // CompleteTutorial, which sets IsActive false and hands off to
        // LevelCompletePanel) — bail out entirely rather than fall through to
        // re-display this step's own callout (with its own "Continue"
        // button) on top of the Level Complete hand-off already in flight.
        // Previously only the button-lock call below was skipped, so the
        // "Your network is live!" callout — and a stray "Continue" button
        // that duplicated LevelCompletePanel's — would still flash up for
        // the ~1.5s delay before ShowLevelCompleteAfterDelay hid it.
        if (!IsActive) return;

        ApplyStepButtonLock(step);

        RectTransform target = null;
        var ui = NetworkUIManager.Instance;
        if (ui != null)
        {
            if (step.HighlightDevice.HasValue) target = ui.GetDeviceButtonRect(step.HighlightDevice.Value);
            else if (step.HighlightCable) target = ui.CableButtonRect;
            else if (step.HighlightTable) target = ui.TableButtonRect;
        }

        string buttonLabel = step.CheckCondition == null
            ? (stepIndex == steps.Count - 1 ? "Continue" : "Next")
            : null;

        float calloutOffsetY = step.TeachDeviceFlyout ? TeachFlyoutCalloutOffsetY : 0f;
        TutorialUIManager.Instance?.Show(step.Text, target, buttonLabel, offsetY: calloutOffsetY);

        TutorialUIManager.Instance?.HideWorldMarkers();
        if (step.HighlightTable) TutorialUIManager.Instance?.SetMarkerColor(TableMarkerColor);
        else TutorialUIManager.Instance?.ResetMarkerColors();

        worldMarkerBRevealed = step.WorldMarkerBGate == null;

        var markers = step.WorldMarkers?.Invoke();
        if (markers.HasValue)
        {
            var devices = step.WorldMarkerDevices?.Invoke();
            if (markers.Value.a.HasValue) TutorialUIManager.Instance?.ShowWorldMarker(0, markers.Value.a.Value, devices?.a);
            // Marker B is held back when WorldMarkerBGate is set — Update()
            // reveals it (and swaps in TextAfterGate) once the gate fires.
            if (worldMarkerBRevealed && markers.Value.b.HasValue)
                TutorialUIManager.Instance?.ShowWorldMarker(1, markers.Value.b.Value, devices?.b);
        }
    }

    void AdvanceStep() => EnterStep(stepIndex + 1);

    /// <summary>Called by TutorialUIManager's Next/Continue button — a no-op unless the
    /// current step actually has no auto-advance condition, so a stray double-click
    /// can't skip past a step that's still waiting on the player to do something.</summary>
    public void AdvanceFromButton()
    {
        if (steps != null && stepIndex >= 0 && stepIndex < steps.Count && steps[stepIndex].CheckCondition == null)
            AdvanceStep();
    }

    // ── Sidebar locking ───────────────────────────────────────────────────────
    // Two layers:
    //  1. Ping/Send Packet/Remove Cable/Remove Device are hidden outright for
    //     the whole tutorial — the sidebar itself stays uncluttered. Router
    //     never appears either, but it can't be hidden on its own anymore: it
    //     now shares NetworkUIManager's single devices/cable compound button
    //     with PC/Switch/Cable (see BuildDevicesCableCompound), so Router
    //     simply never becomes the forced tool in any step below.
    //  2. Of the compound button and Table — the two that DO stay visible —
    //     only the one relevant to the CURRENT step is actually usable. The
    //     compound button is forced to exactly one tool (its flyout disabled,
    //     so the player can't pick anything else) rather than dimmed, since
    //     dimming would also hide/disable the other two tools folded into it.

    void HideUnneededButtons()
    {
        var ui = NetworkUIManager.Instance;
        if (ui == null) return;

        SidebarButtonLock.SetActive(ui.PingButton, false);
        SidebarButtonLock.SetActive(ui.SendPacketButton, false);
        SidebarButtonLock.SetActive(ui.RemoveCableButton, false);
        SidebarButtonLock.SetActive(ui.RemoveDeviceButton, false);
    }

    static CompoundToolKind? DeriveCompoundTool(TutorialStep step)
    {
        if (step.HighlightDevice == DeviceType.PC) return CompoundToolKind.PC;
        if (step.HighlightDevice == DeviceType.Switch) return CompoundToolKind.Switch;
        if (step.HighlightCable) return CompoundToolKind.Cable;
        return null;
    }

    void ApplyStepButtonLock(TutorialStep step)
    {
        var ui = NetworkUIManager.Instance;
        if (ui == null) return;

        if (step.TeachDeviceFlyout) ui.SetCompoundToolUnrestricted();
        else ui.SetCompoundToolLock(DeriveCompoundTool(step));
        SidebarButtonLock.SetLocked(ui.TableButton, !step.HighlightTable);
    }

    // Small pause between the final step's "network is live" message and the
    // Level Complete modal taking over — same idea as Level2Guide's
    // PingCelebrationDelay/Level3Guide's own completion delay, just here
    // it's giving the last callout a moment to read instead of a device/ping
    // effect a moment to play.
    const float LevelCompleteDelaySeconds = 1.5f;

    // No longer unlocks the sidebar in place — finishing the tutorial now
    // hands off to a Level Complete prompt instead of dropping the player
    // straight into the full toolset (see LevelCompletePanel). Progress is
    // saved here so Level 2 shows unlocked next time the Level Select screen
    // is built (see SaveManager.IsLevelUnlocked).
    void CompleteTutorial()
    {
        IsActive = false;

        SaveManager.MarkLevelCompleted(1);
        SaveManager.SetStars(1, TaskManager.Instance != null ? TaskManager.Instance.ComputeStars() : 3);
        AutoSaveManager.Instance?.SaveNow();

        StartCoroutine(ShowLevelCompleteAfterDelay());
    }

    IEnumerator ShowLevelCompleteAfterDelay()
    {
        yield return new WaitForSeconds(LevelCompleteDelaySeconds);

        // Hidden here (right before the modal appears) rather than
        // immediately in CompleteTutorial — EnterStep's own Show() call
        // (right after OnEnter returns) would otherwise re-display the
        // final step's callout — with its own "Continue" button — on top of
        // whatever this Hide() just did, so hiding has to happen last.
        TutorialUIManager.Instance?.Hide();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "Next up: IP Allocation.",
            "Back to Level Select",
            () =>
            {
                MainMenuBootstrap.OpenLevelSelectOnLoad = true;
                LoadingScreenManager.LoadScene("MainMenu", "LOADING LEVEL SELECT");
            },
            TaskManager.Instance != null ? TaskManager.Instance.ComputeStars() : 3);
    }

    /// <summary>Player-facing "Skip Tutorial" button — treats the tutorial as finished
    /// regardless of which step is active, going through the same completion flow
    /// (save + Level Complete prompt) as reaching the last step naturally.</summary>
    public void SkipTutorial()
    {
        CompleteTutorial();
        stepIndex = steps?.Count ?? 0; // out of range so nothing re-enters a mid-walkthrough step afterward
    }
}
