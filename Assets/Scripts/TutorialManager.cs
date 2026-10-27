using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Guided onboarding: trims the sidebar down to PC, Switch, Table, and Cable
/// (everything else hidden outright), then — one action at a time, with every
/// other core button locked out until the current one is done — walks the
/// player through placing and resizing a table, placing and rotating a PC, placing a
/// second PC and a Switch, learning what ports are, and cabling everything
/// together. Once that switch-to-PCs topology exists, a second phase teaches
/// undoing it: Remove Cable (already the remove button's default face, so no
/// flyout needed), then Remove Device and Remove Table, both reached via the
/// remove button's flyout (see the TeachRemoveFlyout steps and
/// FlyoutRevealGate/FlyoutItemTarget, which highlight the button's arrow tab
/// first and retarget that same highlight onto the specific flyout icon once
/// it opens — TeachDeviceFlyout's Switch step does the same two-beat
/// highlight for placing devices). Once both phases are done, the rest of the toolset
/// reappears and the tutorial gets out of the way.
///
/// Reuses TaskManager's Func&lt;bool&gt; CheckCondition polling idiom rather than
/// inventing a parallel one, and reuses DragDropManager.OnDeviceDropped /
/// CableManager.OnCableCreated / OnCableRemoved / TableManager.OnTablePlaced /
/// OnTableResized / DeviceRemoverManager.OnDeviceRemoved / TableRemoverManager.OnTableRemoved
/// for step detection instead of scanning the scene. Deliberately does NOT
/// register with InteractionMode.CancelOthers — it never claims an exclusive
/// input mode of its own, it only locks individual sidebar buttons and draws
/// non-blocking overlays, so it has nothing for that mutual-exclusion gate to
/// cancel.
/// </summary>
public class TutorialManager : MonoBehaviour, INarratedLevel
{
    public static TutorialManager Instance { get; private set; }

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

    // IsometricCamera.TargetYaw when the "turn the view" step began — any turn
    // away from it (button or Q/E, either direction) completes the step.
    private float? yawBaseline;
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
        // Reveals (if not already revealed) and forces the remove compound
        // button's face to this specific tool for this step — see
        // ApplyStepButtonLock and removeRevealed. Only ever set to
        // RemoveToolKind.Cable (Device/Table are reached via TeachRemoveFlyout
        // instead, below, since forcing a face doesn't teach the flyout).
        public RemoveToolKind? HighlightRemove;
        // True only for the step that teaches NetworkUIManager's devices/cable
        // compound button's flyout (see BuildDevicesCableCompound):
        // leaves that button fully unrestricted (any of the four tools reachable,
        // flyout included) instead of forcing HighlightDevice/HighlightCable's
        // tool directly, and tells TutorialUIManager.Show to clear the flyout's
        // row (see its clearFlyoutBelow param) so the two never overlap. The
        // step's callout points at the button's arrow tab (see FlyoutArrowTab),
        // which only shows while the flyout is unlocked, so this step is also
        // where the arrow first appears.
        public bool TeachDeviceFlyout;
        // Same idea as TeachDeviceFlyout, for the remove compound button's own
        // flyout (Remove Device/Remove Table) — see
        // ApplyStepButtonLock/SetRemoveToolUnrestricted.
        public bool TeachRemoveFlyout;
        // Two-beat UI highlight for an "open the flyout with its arrow, then
        // click this specific item" step — parallel to WorldMarkerBGate/
        // TextAfterGate but retargeting the sidebar highlight beacon (see
        // TutorialUIManager.Show's `target`) instead of a world-space marker.
        // Unlike WorldMarkerBGate (a one-shot latch), FlyoutRevealGate is
        // polled every frame FOR AS LONG AS THE STEP IS ACTIVE (typically
        // DevicesToolReachable/RemoveToolReachable, built on NetworkUIManager.
        // IsDevicesFlyoutOpen/IsRemoveFlyoutOpen, which are only true once the
        // flyout's pop-in animation has actually settled — see
        // CompoundSidebarGroup.FlyoutSettled) and the highlight
        // is toggled both ways: onto FlyoutItemTarget (with TextAfterFlyoutOpen)
        // whenever it's true, back onto the step's own arrow-tab target
        // (with Text) whenever it goes false again — e.g. the player
        // clicking away to cancel, or closing and reopening the flyout. A
        // one-shot latch here would leave the highlight's continuous
        // scale-pulse (see PulsingHighlight) fighting the flyout's own
        // pop-in/pop-out scale animation on every subsequent open/close.
        public Func<bool> FlyoutRevealGate;
        // Any other UI element to point at (instead of a sidebar button) —
        // e.g. ViewControlsHud's turn buttons. Wins over the Highlight* flags.
        public Func<RectTransform> HighlightUi;
        public Func<RectTransform> FlyoutItemTarget;
        public string TextAfterFlyoutOpen;
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
        // For a step whose two spots are two separate clicks in a row (the
        // table's two corners): only the NEXT spot is ever marked — marker B
        // replaces marker A when WorldMarkerBGate opens, instead of joining
        // it. And unlike the default one-shot latch, the gate stays live both
        // ways for the whole step (same two-way handling as FlyoutRevealGate):
        // if it closes again — the player cancelled after the first click —
        // marker A and Text come back, so the callout never asks for a second
        // click the game is no longer waiting on.
        public bool WorldMarkersInSequence;
        // What each marker's mouse acts out (see MouseGesture) — a plain
        // click unless the step says otherwise: Hold on a device to pick up,
        // Release on a drop spot.
        public MouseGesture MarkerAGesture = MouseGesture.Click;
        public MouseGesture MarkerBGesture = MouseGesture.Click;
        // Tints this step's markers (null = WorldPulseMarker's own gold) — a
        // HighlightTable step is always TableMarkerColor without asking.
        public Color? MarkerColor;
        // "Drag from here to there" arrow trail (see DragPathGuide) — built
        // fresh on step entry, its ends re-resolved live, so it simply stays
        // hidden until both ends exist (e.g. a flyout item before the flyout
        // opens). A world end is marked by WorldMarkers, not doubled here.
        // On a step with WorldMarkerBGate the trail leads to marker B, so it's
        // revealed together with it rather than at step entry.
        public Func<DragGuideRoute> Drag;
        // Shows Drag from step entry anyway, and keeps it through both beats —
        // for a drag short enough that waiting for marker B leaves it no room
        // to show (a table edge already halfway to its spot), where seeing
        // the whole way before grabbing is the point.
        public bool DragFromStepStart;
    }

    public bool IsActive { get; private set; }

    private List<TutorialStep> steps;
    private int stepIndex = -1;

    private bool tablePlaced;
    private Table placedTable;

    // The "resize your table" step: which edge it suggests dragging (see
    // PickResizeTarget), the spot it suggests dragging that edge to (picked
    // as the step starts, then held still so it doesn't run ahead of the
    // edge being dragged), and whether a resize has made the table bigger yet.
    private Vector2Int resizeSide = new Vector2Int(1, 0);
    private Vector3? resizeTarget;
    private bool tableGrownThisStep;
    private readonly List<ScreenOccupancy.Entry> resizeOccupied = new();
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

    // Set true once the matching removal-teaching step's action has actually
    // happened — see HandleCableRemoved/HandleDeviceRemoved/HandleTableRemoved.
    // Each is reset false on its own step's OnEnter. Deliberately permissive
    // about WHICH cable/device gets removed (there's more than one of each by
    // the time these steps run) rather than tracking one specific target.
    private bool cableRemovedThisStep;
    private bool deviceRemovedThisStep;
    private bool tableRemovedThisStep;

    // True once the remove compound button has been revealed at least once
    // (see the "remove a cable" step) — from then on it stays visible for the
    // rest of the tutorial (same idea as Table, which is visible the whole
    // time), just locked/unlocked per step like every other tool.
    private bool removeRevealed;

    // Tracks which side of TutorialStep.FlyoutRevealGate's toggle the
    // highlight is currently showing — false = the step's own face-button
    // target (the default, set by EnterStep), true = FlyoutItemTarget. See
    // Update()/RevealFlyoutTarget/RestoreFaceTarget.
    private bool flyoutTargetRevealed;

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
        // other level (see LevelManager) gets the full toolset from frame one
        // and supplies its own guided objectives via TaskManager instead (see
        // Level2Guide, Level3Guide).
        if (LevelManager.Instance != null && LevelManager.Instance.CurrentLevel != 1)
        {
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
        if (CableManager.Instance != null) CableManager.Instance.OnCableRemoved += HandleCableRemoved;
        if (TableManager.Instance != null) TableManager.Instance.OnTablePlaced += HandleTablePlaced;
        if (TableManager.Instance != null) TableManager.Instance.OnTableResized += HandleTableResized;
        if (DeviceRemoverManager.Instance != null) DeviceRemoverManager.Instance.OnDeviceRemoved += HandleDeviceRemoved;
        if (TableRemoverManager.Instance != null) TableRemoverManager.Instance.OnTableRemoved += HandleTableRemoved;

        IsActive = true;
        // Runs here — one frame after scene load, well before NetworkUIManager
        // actually queues its button reveal-flash (see StaggerRevealFlashChildren/
        // QueueRevealFlash, deferred two more frames past construction) — so a
        // tool this tutorial hasn't unlocked yet is already inactive by the time
        // that queueing decision runs, instead of flashing in and then vanishing.
        HideUnneededButtons();

        // Hold off on the tutorial's first annotation/highlight until the
        // whole opening sequence (blank background → platform → walls →
        // HUD buttons) has actually finished — see GameBootstrap.
        // IntroSequenceReady.
        while (!GameBootstrap.IntroSequenceReady) yield return null;

        EnterStep(0);
    }

    void OnDestroy()
    {
        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped -= HandleDeviceDropped;
        if (CableManager.Instance != null) CableManager.Instance.OnCableCreated -= HandleCableCreated;
        if (CableManager.Instance != null) CableManager.Instance.OnCableRemoved -= HandleCableRemoved;
        if (TableManager.Instance != null) TableManager.Instance.OnTablePlaced -= HandleTablePlaced;
        if (TableManager.Instance != null) TableManager.Instance.OnTableResized -= HandleTableResized;
        if (DeviceRemoverManager.Instance != null) DeviceRemoverManager.Instance.OnDeviceRemoved -= HandleDeviceRemoved;
        if (TableRemoverManager.Instance != null) TableRemoverManager.Instance.OnTableRemoved -= HandleTableRemoved;
    }

    void Update()
    {
        if (!IsActive || steps == null || stepIndex < 0 || stepIndex >= steps.Count) return;

        var step = steps[stepIndex];

        // Checked before the gates below: the action that completes a step
        // can also close its gate on the same frame (placing the table ends
        // placement mode, clearing its first corner), and the step is done —
        // there's nothing to revert.
        if (step.CheckCondition != null && step.CheckCondition())
        {
            AdvanceStep();
            return;
        }

        // Marker B's reveal (and the matching callout text swap) can be held
        // back behind a live condition instead of appearing the instant the
        // step starts — see WorldMarkerBGate. Polled every frame until it
        // fires once; after that it stays revealed for the rest of the step,
        // unless the step's markers are a sequence (WorldMarkersInSequence),
        // which puts marker A back if the gate closes again.
        if (step.WorldMarkerBGate != null)
        {
            if (!worldMarkerBRevealed && step.WorldMarkerBGate())
                RevealWorldMarkerB(step);
            else if (worldMarkerBRevealed && step.WorldMarkersInSequence && !step.WorldMarkerBGate())
                RestoreWorldMarkerA(step);
        }

        // Same idea, for the sidebar highlight instead of a world marker —
        // see TutorialStep.FlyoutRevealGate. Two-way (unlike WorldMarkerBGate
        // above): toggles the highlight back onto the face button if the
        // flyout closes again before the step's CheckCondition is satisfied.
        if (step.FlyoutRevealGate != null)
        {
            bool open = step.FlyoutRevealGate();
            if (open && !flyoutTargetRevealed) { flyoutTargetRevealed = true; RevealFlyoutTarget(step); }
            else if (!open && flyoutTargetRevealed) { flyoutTargetRevealed = false; RestoreFaceTarget(step); }
        }
    }

    // Deferred reveal of marker B — see WorldMarkerBGate on TutorialStep and
    // the call site in Update(). Re-invokes WorldMarkers/WorldMarkerDevices
    // rather than reusing whatever EnterStep captured, since the destination
    // may depend on state (e.g. a table's FinalCenter) that wasn't settled
    // yet at step-entry time.
    void RevealWorldMarkerB(TutorialStep step)
    {
        worldMarkerBRevealed = true;

        if (step.WorldMarkersInSequence) TutorialUIManager.Instance?.HideWorldMarker(0);

        var markers = step.WorldMarkers?.Invoke();
        if (markers.HasValue && markers.Value.b.HasValue)
        {
            var devices = step.WorldMarkerDevices?.Invoke();
            TutorialUIManager.Instance?.ShowWorldMarker(1, markers.Value.b.Value, devices?.b, step.MarkerBGesture);
        }

        if (step.Drag != null && !step.DragFromStepStart) TutorialUIManager.Instance?.ShowDragGuide(step.Drag());

        if (step.TextAfterGate != null)
            TutorialUIManager.Instance?.UpdateText(step.TextAfterGate);
    }

    // The reverse of RevealWorldMarkerB, for a WorldMarkersInSequence step
    // whose gate closed again (see the call site in Update()) — back to the
    // step's first beat exactly as EnterStep showed it.
    void RestoreWorldMarkerA(TutorialStep step)
    {
        worldMarkerBRevealed = false;

        TutorialUIManager.Instance?.HideWorldMarker(1);
        if (!step.DragFromStepStart) TutorialUIManager.Instance?.HideDragGuide();

        var markers = step.WorldMarkers?.Invoke();
        if (markers.HasValue && markers.Value.a.HasValue)
        {
            var devices = step.WorldMarkerDevices?.Invoke();
            TutorialUIManager.Instance?.ShowWorldMarker(0, markers.Value.a.Value, devices?.a, step.MarkerAGesture);
        }

        if (step.TextAfterGate != null)
            TutorialUIManager.Instance?.UpdateText(step.Text);
    }

    // Retargets the sidebar highlight beacon onto a specific flyout item —
    // see TutorialStep.FlyoutRevealGate/FlyoutItemTarget/TextAfterFlyoutOpen
    // and the call site in Update(). Re-invokes FlyoutItemTarget rather than
    // caching it, same reasoning as RevealWorldMarkerB re-invoking WorldMarkers.
    void RevealFlyoutTarget(TutorialStep step)
    {
        var rect = step.FlyoutItemTarget?.Invoke();
        if (rect != null && step.TextAfterFlyoutOpen != null)
            TutorialUIManager.Instance?.RetargetHighlight(rect, step.TextAfterFlyoutOpen);
    }

    // Mirror of RevealFlyoutTarget for the other direction — the flyout
    // closed again (cancelled, or closed-and-not-yet-reopened) before the
    // step's own action happened, so the highlight moves back onto the same
    // face-button target/text EnterStep originally showed for this step.
    void RestoreFaceTarget(TutorialStep step)
    {
        RectTransform rect = ResolveStepTarget(step);
        TutorialUIManager.Instance?.RetargetHighlight(rect, step.Text);
    }

    // Shared by EnterStep (initial target) and RestoreFaceTarget (reverting
    // after a cancelled flyout) — which single sidebar button, if any, this
    // step's callout points at.
    RectTransform ResolveStepTarget(TutorialStep step)
    {
        if (step.HighlightUi != null) return step.HighlightUi();

        var ui = NetworkUIManager.Instance;
        if (ui == null) return null;

        // A flyout-teaching step points at the arrow tab that opens the
        // flyout (see FlyoutArrowTab), not the button itself.
        if (step.TeachDeviceFlyout) return ui.DevicesFlyoutArrowRect;
        if (step.TeachRemoveFlyout) return ui.RemoveFlyoutArrowRect;

        if (step.HighlightDevice.HasValue) return ui.GetDeviceButtonRect(step.HighlightDevice.Value);
        if (step.HighlightCable) return ui.CableButtonRect;
        if (step.HighlightTable) return ui.TableButtonRect;
        if (step.HighlightRemove.HasValue || step.TeachRemoveFlyout) return ui.RemoveButtonRect;
        return null;
    }

    // ── Step definitions ─────────────────────────────────────────────────────

    void BuildSteps()
    {
        steps = new List<TutorialStep>
        {
            // 0 — Welcome
            new() { Text = "Welcome! Let's build your first network together, one step at a time. Tip: rest your mouse on any button to see what it does. Click Next to begin." },

            // 1 — Zoom in: no sidebar action, pure camera control, taught before
            // anything else since every later step is easier to see and click
            // accurately once the player isn't stuck fully zoomed out. Points at
            // ViewControlsHud's zoom buttons — the way in for a trackpad, which
            // has no wheel to scroll.
            new() { Text = "Let's get a closer look first. Zoom in on the play area: scroll your mouse wheel, or click the zoom-in button in the bottom-right corner.",
                     CameraOnly = true,
                     HighlightUi = () => ViewControlsHud.Instance != null ? ViewControlsHud.Instance.ZoomButtonsRect : null,
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

            // 2 — Turn the view: no sidebar action, pure camera control. Points
            // at ViewControlsHud's on-screen turn buttons — Q/E used to be the
            // only way, and keys alone were hard to remember or read ("turn
            // left" spins the scene clockwise). The buttons stay on screen with
            // the keys printed on them, so one turn either way is enough here.
            new() { Text = "You can also turn the view to see things from another side. Click a turn button in the bottom-right corner (or press <key>Q</key> / <key>E</key>).",
                     CameraOnly = true,
                     HighlightUi = () => ViewControlsHud.Instance != null ? ViewControlsHud.Instance.TurnButtonsRect : null,
                     OnEnter = () => yawBaseline = null,
                     CheckCondition = CheckCameraRotated },

            // 3 — Place a table: two separate clicks, one per corner, taught as
            // two beats so neither reads as a drag. Only the corner that's
            // next is ever marked (WorldMarkersInSequence): the first until
            // TableManager has fixed it, then the opposite one — placed off
            // wherever that first click actually landed (see
            // SecondTableCornerPos), with the text saying the first corner is
            // done. No drag trail between the corners either — a trail is
            // this game's "drag from here to there" sign, which is exactly
            // the misreading this step has to avoid. Cancelling after the
            // first click (right-click, or Table again) puts beat one back.
            new() { Text = "Every device needs somewhere to sit. Click <b>Table</b>, then click the glowing circle <b>once</b> to set the table's first corner.",
                     TextAfterGate = "First corner set! Now move your mouse to the new glowing circle — the table stretches to follow — and click <b>once</b> more to set the opposite corner.",
                     HighlightTable = true,
                     CheckCondition = () => tablePlaced,
                     WorldMarkers = () => (TableCornerA, SecondTableCornerPos()),
                     WorldMarkerBGate = () => TableManager.Instance != null && TableManager.Instance.HasOrigin,
                     WorldMarkersInSequence = true },

            // 3b — Select the table, while it's still empty, so its resize
            // handles show.
            new() { Text = "Tables can be resized. Click your table <b>once</b> to select it.",
                     CheckCondition = () => TableManager.Instance != null && TableManager.Instance.ShowsResizeHandles,
                     WorldMarkers = () => (PlacedTableMarkerPos(), null),
                     MarkerColor = TableMarkerColor },

            // 3c — Drag one edge's handle out to the marked spot, the same
            // hold → release beats as the "move your PC" step: the hold marker
            // sits on the handle itself, and once the edge is being dragged it
            // gives way to the release marker. A straight trail (an edge only
            // moves in a straight line) runs from the handle to the spot from
            // the start, so the whole drag shows before it's begun. Letting go
            // short of growing the table puts the hold marker back. Only
            // growing it counts, so the PCs and switch still have room after it.
            new() { Text = "Handles appeared on its corners and edges. Press and hold the marked edge handle.",
                     TextAfterGate = "Keep holding, drag the edge out to the glowing circle, then let go to make the table bigger.",
                     OnEnter = () => { tableGrownThisStep = false; PickResizeTarget(); },
                     CheckCondition = () => tableGrownThisStep,
                     WorldMarkers = () => (ResizeHandlePos(), resizeTarget),
                     WorldMarkerBGate = () => TableManager.Instance != null && TableManager.Instance.IsResizing,
                     WorldMarkersInSequence = true,
                     MarkerAGesture = MouseGesture.Hold,
                     MarkerBGesture = MouseGesture.Release,
                     MarkerColor = TableMarkerColor,
                     // Follows the edge as it's dragged — so no InProgress fade.
                     Drag = () => new DragGuideRoute(GuideAnchor.World(ResizeHandlePos), GuideAnchor.World(() => resizeTarget),
                         color: TableMarkerColor, straight: true),
                     DragFromStepStart = true },

            // 4 — Place the first PC. Starts with the table deselected — the
            // resize steps leave it selected, handles and all, which would only
            // clutter the drop.
            new() { Text = "Press and hold <b>PC</b>, drag it over the table, then let go to drop it there. It must land inside the table's glowing outline.",
                     OnEnter = () => TableManager.Instance?.ClearHighlight(),
                     HighlightDevice = DeviceType.PC,
                     CheckCondition = () => placedPCCount >= 1,
                     WorldMarkers = () => (PcMarkerPos(-1), null),
                     MarkerAGesture = MouseGesture.Release,
                     Drag = () => PaletteRoute(CompoundToolKind.PC, () => PcMarkerPos(-1)) },

            // 6 — Rotate it: no sidebar action. The highlight sits on
            // DeviceRotateHud's buttons, which only appear once the PC is
            // selected — so it shows up as the second beat by itself.
            new() { Text = "Now rotate it: left-click your PC to select it (it glows cyan), then click a rotate button at the bottom of the screen. (Holding <key>R</key> and scrolling works too.)",
                     HighlightUi = () => DeviceRotateHud.Instance != null ? DeviceRotateHud.Instance.ButtonsRect : null,
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
                     TextAfterGate = "Keep holding, drag it to the glowing spot, then let go to place it down.",
                     OnEnter = () => dragBaselinePos = null,
                     CheckCondition = CheckDeviceDragged,
                     WorldMarkers = () => (FirstPlacedPcMarkerPos(), DragDestinationMarkerPos()),
                     WorldMarkerDevices = () => (firstPlacedPC, null),
                     MarkerAGesture = MouseGesture.Hold,
                     MarkerBGesture = MouseGesture.Release,
                     WorldMarkerBGate = () => DragDropManager.Instance != null && DragDropManager.Instance.IsRepositioning,
                     // Revealed with marker B (mid-drag by definition), and follows the
                     // PC as it's carried — so no InProgress fade on this one.
                     Drag = () => WorldRoute(FirstPlacedPcMarkerPos, DragDestinationMarkerPos) },

            // 8 — Second PC
            new() { Text = "Drag another <b>PC</b> from the sidebar onto the table.",
                     HighlightDevice = DeviceType.PC,
                     CheckCondition = () => placedPCCount >= 2,
                     WorldMarkers = () => (PcMarkerPos(1), null),
                     MarkerAGesture = MouseGesture.Release,
                     Drag = () => PaletteRoute(CompoundToolKind.PC, () => PcMarkerPos(1)) },

            // 9 — Switch. Two-beat: first points at the arrow tab beside the
            // button (the arrow's first appearance — every earlier step locks
            // the button, which tucks it away), then, once the flyout is
            // actually open, retargets the same highlight onto the Switch
            // icon within it — see FlyoutRevealGate/FlyoutItemTarget/
            // TextAfterFlyoutOpen. Also stays on Switch once it's become the
            // face (picked, but the drag was dropped short of the table), so
            // the highlight doesn't send the player back to the arrow for a
            // tool that's already in hand.
            new() { Text = "That button holds more than PCs. Click the <b>arrow</b> beside it to see the other tools. (Right-clicking the button works too.)",
                     HighlightDevice = DeviceType.Switch,
                     TeachDeviceFlyout = true,
                     CheckCondition = () => placedSwitchCount >= 1,
                     WorldMarkers = () => (SwitchMarkerPos(), null),
                     MarkerAGesture = MouseGesture.Release,
                     FlyoutRevealGate = () => DevicesToolReachable(CompoundToolKind.Switch),
                     FlyoutItemTarget = () => NetworkUIManager.Instance?.GetCompoundToolRect(CompoundToolKind.Switch),
                     TextAfterFlyoutOpen = "Now drag <b>Switch</b> out onto the table — it's what connects your PCs together.",
                     // The Switch button only exists on screen once the flyout is open
                     // (or once it's become the face), so the trail waits for it by itself.
                     Drag = () => PaletteRoute(CompoundToolKind.Switch, SwitchMarkerPos) },

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
                     WorldMarkerDevices = () => (placedSwitch, null),
                     // From whichever device the cable actually started on, to the Switch.
                     Drag = () => WorldRoute(() => CableManager.Instance?.SourceDevice?.VisualTopPoint(), PlacedSwitchMarkerPos) },

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
                     WorldMarkerDevices = () => (secondPlacedPC, placedSwitch),
                     Drag = () => WorldRoute(SecondPlacedPcMarkerPos, PlacedSwitchMarkerPos) },

            // 12 — Remove Cable. Runs once the switch-to-PCs topology actually
            // exists (right after the second cable completes it), and reuses
            // the remove button's own default face (Cable — see
            // NetworkUIManager.BuildRemoveCompound) rather than teaching the
            // flyout: this is the one removal tool reachable with a single
            // click, no flyout required. Deliberately permissive about
            // which cable gets clicked — there are two by this point, and
            // either is a valid answer — so no world marker points at one.
            new() { Text = "Your network is built! Networks change too — let's learn to undo some of it. Click <b>Remove</b>, then click one of your cables to delete it.",
                     HighlightRemove = RemoveToolKind.Cable,
                     OnEnter = () => cableRemovedThisStep = false,
                     CheckCondition = () => cableRemovedThisStep },

            // 13 — Remove Device, via the remove button's flyout. Same
            // two-beat highlight pattern as the Switch step above (arrow tab,
            // then the flyout item), just pointed at NetworkUIManager's remove
            // compound group instead of the devices one. Permissive about
            // which device gets removed for the same reason as step 12.
            new() { Text = "<b>Remove</b> has an arrow too. Click the arrow to see the other removal tools.",
                     TeachRemoveFlyout = true,
                     OnEnter = () => deviceRemovedThisStep = false,
                     CheckCondition = () => deviceRemovedThisStep,
                     FlyoutRevealGate = () => RemoveToolReachable(RemoveToolKind.Device),
                     FlyoutItemTarget = () => NetworkUIManager.Instance?.GetRemoveToolRect(RemoveToolKind.Device),
                     TextAfterFlyoutOpen = "Click <b>Remove Device</b>, then click one of your devices to delete it." },

            // 14 — Remove Table, same flyout, now reached from the Device face
            // step 13 left behind (SelectFace swapped it when Device was
            // picked) — so this still needs the arrow again to reopen it.
            new() { Text = "Click the arrow beside <b>Remove</b> again to find the last tool.",
                     TeachRemoveFlyout = true,
                     OnEnter = () => tableRemovedThisStep = false,
                     CheckCondition = () => tableRemovedThisStep,
                     FlyoutRevealGate = () => RemoveToolReachable(RemoveToolKind.Table),
                     FlyoutItemTarget = () => NetworkUIManager.Instance?.GetRemoveToolRect(RemoveToolKind.Table),
                     TextAfterFlyoutOpen = "Click <b>Remove Table</b>, then click your table to delete it." },

            // 15 — Completion
            new() { Text = "Great work — you've learned to build a network, and take it apart when you need to.",
                     OnEnter = CompleteTutorial },
        };
    }

    // FlyoutRevealGate for the flyout-teaching steps: the tool the step wants
    // is within reach once its flyout has opened and settled, or once it's
    // already become the button's face (picked from the flyout).
    static bool DevicesToolReachable(CompoundToolKind tool)
    {
        var ui = NetworkUIManager.Instance;
        return ui != null && (ui.IsDevicesFlyoutOpen || ui.DevicesFace == tool);
    }

    static bool RemoveToolReachable(RemoveToolKind tool)
    {
        var ui = NetworkUIManager.Instance;
        return ui != null && (ui.IsRemoveFlyoutOpen || ui.RemoveFace == tool);
    }

    bool CheckCameraRotated()
    {
        var cam = IsometricCamera.Instance;
        if (cam == null) return false;
        if (yawBaseline == null) { yawBaseline = cam.TargetYaw; return false; }
        return Mathf.Abs(cam.TargetYaw - yawBaseline.Value) >= 1f;
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

    // ── Drag-guide routes (see TutorialStep.Drag) ────────────────────────────

    // Sidebar tool → world drop spot. Fades while the player is actually
    // dragging a fresh device out (the destination marker keeps pointing).
    static DragGuideRoute PaletteRoute(CompoundToolKind tool, Func<Vector3?> dropSpot) => new DragGuideRoute(
        GuideAnchor.Ui(() => NetworkUIManager.Instance != null ? NetworkUIManager.Instance.GetCompoundToolRect(tool) : null),
        GuideAnchor.World(dropSpot),
        inProgress: () => DragDropManager.Instance != null && DragDropManager.Instance.IsDragging);

    // World spot → world spot (a device being carried, a cable's two ends).
    // color matches the trail to its ends' markers when those are tinted.
    static DragGuideRoute WorldRoute(Func<Vector3?> from, Func<Vector3?> to, Color? color = null) =>
        new DragGuideRoute(GuideAnchor.World(from), GuideAnchor.World(to), color: color);

    // ── World-marker placement math ──────────────────────────────────────────
    // Fixed opposite corners for the table step (comfortably inside the play
    // area's ±7.5 half-extent, centered at the origin so later device markers
    // — computed from the table's actual footprint once placed — land near
    // the middle of the screen too). Widened from the original 6x4 so the
    // diagonal PC/switch layout below (see TableMarkerPos) has comfortable
    // room to spread the two PCs apart instead of crowding the middle.
    static readonly Vector3 TableCornerA = new Vector3(-3.5f, 0f, 2.5f);
    static readonly Vector3 TableCornerB = new Vector3(3.5f, 0f, -2.5f);

    // Clearance kept between the suggested second corner and the play area's
    // edge, so its marker never sits on (or past) the border strip.
    const float TableCornerEdgeMargin = 1f;

    // The second-corner marker, placed once the first corner is fixed: the
    // same offset from wherever that first click actually landed as
    // TableCornerB is from TableCornerA — so a first click that missed its
    // circle still gets a full-size table rather than a sliver (or the "Table
    // Too Small" warning) — flipped on any axis that would run off the play
    // area. Just TableCornerB before then (only read once the gate opens).
    static Vector3? SecondTableCornerPos()
    {
        var tables = TableManager.Instance;
        if (tables == null || !tables.HasOrigin) return TableCornerB;

        Vector3 origin = tables.OriginPoint;
        Vector3 offset = TableCornerB - TableCornerA;
        float half = (DragDropManager.Instance != null ? DragDropManager.Instance.PlayAreaHalfExtent : 9f) - TableCornerEdgeMargin;

        float x = origin.x + offset.x;
        if (Mathf.Abs(x) > half) x = origin.x - offset.x;
        float z = origin.z + offset.z;
        if (Mathf.Abs(z) > half) z = origin.z - offset.z;
        return new Vector3(x, 0f, z);
    }

    // How much bigger the resize step suggests making the table (world units):
    // at least a clear drag, not a nudge — and further when the camera is
    // zoomed out, so the drag stays long enough on screen for DragPathGuide to
    // fit its chevrons between the two markers. It stops short of each world
    // end by about ResizeTrailEndUnits and needs ResizeTrailPixels (at 1080p)
    // in between.
    const float MinTableGrow = 2f, MaxTableGrow = 5f;
    const float ResizeTrailEndUnits = 1.4f;
    const float ResizeTrailPixels = 100f;

    // What the resize step's drag shouldn't run under: HUD chrome, open
    // panels, and the callout's own home spot (see PickResizeTarget).
    const OccupantLayer ResizeAvoidLayers = OccupantLayer.Hud | OccupantLayer.Panel | OccupantLayer.Reservation;
    const int ResizePathSamples = 6;

    // The four edge handles the resize step can suggest (see TableManager.HandlePosition).
    static readonly Vector2Int[] TableEdgeSides =
        { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    // The resize step's first marker: on the tabletop, where clicking selects it.
    Vector3? PlacedTableMarkerPos()
    {
        if (placedTable == null) return null;
        Vector3 center = placedTable.FinalCenter;
        return new Vector3(center.x, placedTable.topSurfaceY + 0.02f, center.z);
    }

    // Of the edges with room past them to grow into (clear of the play area's
    // border, like SecondTableCornerPos), the one that moves most on screen
    // per unit dragged from the current view — an edge dragged straight into
    // the screen barely moves, and reads as nothing happening — and whose
    // drag doesn't run under the callout or other UI, which would hide the
    // destination marker and the trail. Picked once, as the drag step
    // starts, so the spot holds still while the edge is dragged to it.
    Vector3? PickResizeTarget()
    {
        resizeTarget = null;
        if (placedTable == null) return null;

        Camera cam = Camera.main;
        resizeOccupied.Clear();
        ScreenOccupancy.Collect(resizeOccupied, ResizeAvoidLayers);

        float half = (DragDropManager.Instance != null ? DragDropManager.Instance.PlayAreaHalfExtent : 9f) - TableCornerEdgeMargin;
        float best = -1f;
        foreach (Vector2Int side in TableEdgeSides)
        {
            Vector3 edge = TableManager.HandlePosition(placedTable, side);
            Vector3 outward = new Vector3(side.x, 0f, side.y);
            float pxPerUnit = cam != null ? Vector2.Distance(cam.WorldToScreenPoint(edge), cam.WorldToScreenPoint(edge + outward)) : 0f;

            float grow = pxPerUnit > 0f
                ? ResizeTrailEndUnits + ResizeTrailPixels * Screen.height / 1080f / pxPerUnit
                : MinTableGrow;
            grow = Mathf.Clamp(Mathf.Ceil(grow * 2f) / 2f, MinTableGrow, MaxTableGrow); // half-unit steps

            // Short of the border, grow only as far as there's room.
            float room = half - (side.x != 0 ? edge.x * side.x : edge.z * side.y);
            grow = Mathf.Min(grow, room);
            if (grow < MinTableGrow) continue;

            Vector3 target = edge + outward * grow;
            float score = cam != null ? pxPerUnit * (1f - CoveredFraction(cam, edge, target)) : 0f;
            if (score > best) { best = score; resizeSide = side; resizeTarget = target; }
        }
        return resizeTarget;
    }

    // How much of the edge → target drag (sampled along it, on screen) sits
    // under something in resizeOccupied.
    float CoveredFraction(Camera cam, Vector3 from, Vector3 to)
    {
        int covered = 0;
        for (int i = 0; i <= ResizePathSamples; i++)
        {
            Vector3 sp = cam.WorldToScreenPoint(Vector3.Lerp(from, to, i / (float)ResizePathSamples));
            foreach (var e in resizeOccupied)
                if (e.Rect.Contains(new Vector2(sp.x, sp.y))) { covered++; break; }
        }
        return covered / (float)(ResizePathSamples + 1);
    }

    // The suggested edge's handle, live — the drag trail starts from it.
    Vector3? ResizeHandlePos() =>
        placedTable != null ? TableManager.HandlePosition(placedTable, resizeSide) : null;

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

    // Any handle counts, as long as the table came out bigger along either axis.
    void HandleTableResized(Table table, Vector3 oldCenter, float oldWidth, float oldLength)
    {
        if (table.width > oldWidth + 0.01f || table.length > oldLength + 0.01f) tableGrownThisStep = true;
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

    // The removal-teaching steps run after the topology is already built and
    // nothing later in the tutorial reads placedPCCount/firstPlacedPC/
    // secondPlacedPC/placedSwitch again, so unlike HandleDeviceDropped this
    // doesn't need to track WHICH device was removed — any one satisfies the
    // step (see its Text, which says "one of your devices").
    void HandleDeviceRemoved(NetworkDevice device) => deviceRemovedThisStep = true;

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

    // Same permissiveness as HandleDeviceRemoved — any cable satisfies the
    // "remove a cable" step, not one specific one.
    void HandleCableRemoved(Cable cable) => cableRemovedThisStep = true;

    // Same idea, for the "remove a table" step. This level only ever has one
    // table, but the handler stays permissive for consistency with the other
    // two removal steps.
    void HandleTableRemoved(Table table) => tableRemovedThisStep = true;

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

        RectTransform target = ResolveStepTarget(step);

        string buttonLabel = step.CheckCondition == null
            ? (stepIndex == steps.Count - 1 ? "Continue" : "Next")
            : null;

        TutorialUIManager.Instance?.Show(step.Text, target, buttonLabel,
            clearFlyoutBelow: step.TeachDeviceFlyout || step.TeachRemoveFlyout,
            progress: (stepIndex + 1, steps.Count));

        TutorialUIManager.Instance?.HideWorldMarkers();
        Color? markerColor = step.MarkerColor ?? (step.HighlightTable ? TableMarkerColor : (Color?)null);
        if (markerColor.HasValue) TutorialUIManager.Instance?.SetMarkerColor(markerColor.Value);
        else TutorialUIManager.Instance?.ResetMarkerColors();

        worldMarkerBRevealed = step.WorldMarkerBGate == null;
        // Always starts false (showing the face-button target `target` just
        // resolved above) regardless of whether this step even has a
        // FlyoutRevealGate — see Update()'s toggle and the field's own comment.
        flyoutTargetRevealed = false;

        var markers = step.WorldMarkers?.Invoke();
        if (markers.HasValue)
        {
            var devices = step.WorldMarkerDevices?.Invoke();
            if (markers.Value.a.HasValue) TutorialUIManager.Instance?.ShowWorldMarker(0, markers.Value.a.Value, devices?.a, step.MarkerAGesture);
            // Marker B is held back when WorldMarkerBGate is set — Update()
            // reveals it (and swaps in TextAfterGate) once the gate fires.
            if (worldMarkerBRevealed && markers.Value.b.HasValue)
                TutorialUIManager.Instance?.ShowWorldMarker(1, markers.Value.b.Value, devices?.b, step.MarkerBGesture);
        }

        // Same gating as marker B — see TutorialStep.Drag/DragFromStepStart.
        if (step.Drag != null && (worldMarkerBRevealed || step.DragFromStepStart)) TutorialUIManager.Instance?.ShowDragGuide(step.Drag());
        else TutorialUIManager.Instance?.HideDragGuide();
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
    //  1. Ping/Send Packet are hidden outright for the whole tutorial — the
    //     sidebar itself stays uncluttered. Router never appears either, but
    //     it can't be hidden on its own anymore: it now shares
    //     NetworkUIManager's single devices/cable compound button with
    //     PC/Switch/Cable (see BuildDevicesCableCompound), so Router simply
    //     never becomes the forced tool in any step below. The remove
    //     compound button (Remove Cable/Device/Table, one button — see
    //     BuildRemoveCompound) starts hidden the same way, but isn't
    //     permanent: the "remove a cable" step (run once the switch-to-PCs
    //     topology is built) reveals it (see ApplyStepButtonLock/
    //     removeRevealed) and it stays visible from then on, same as Table
    //     below.
    //  2. Of the buttons that DO stay visible once revealed (compound
    //     devices/cable, Table, compound remove) — only the one relevant to
    //     the CURRENT step is actually usable. Each compound button is
    //     forced to exactly one tool (its flyout disabled, so the player
    //     can't pick anything else) rather than dimmed, since dimming would
    //     also hide/disable the other tools folded into it.

    void HideUnneededButtons()
    {
        // Ctrl+Z is a tool too — see UndoManager.AvailableInThisLevel.
        if (UndoManager.Instance != null) UndoManager.Instance.AvailableInThisLevel = false;

        var ui = NetworkUIManager.Instance;
        if (ui == null) return;

        SidebarButtonLock.SetActive(ui.PingButton, false);
        SidebarButtonLock.SetActive(ui.SendPacketButton, false);
        SidebarButtonLock.SetActive(ui.RemoveDeviceButton, false); // same physical button as RemoveCableButton/RemoveTableButton (removeGroup) — revealed later by the "remove a cable" step
        // The flyout arrows first appear on the steps that teach them, not
        // with the HUD — see ApplyStepButtonLock and the NetworkUIManager doc.
        ui.DisableCompoundFlyouts();
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

        // Remove starts hidden (see HideUnneededButtons) and stays that way
        // until the step that teaches it — from then on it stays visible for
        // the rest of the tutorial, same as Table, just locked/unlocked per
        // step like every other tool. TeachRemoveFlyout mirrors
        // TeachDeviceFlyout above: leaves the flyout reachable (Device/Table)
        // instead of forcing one tool directly.
        if (step.HighlightRemove.HasValue || step.TeachRemoveFlyout) removeRevealed = true;
        if (removeRevealed)
        {
            SidebarButtonLock.SetActive(ui.RemoveDeviceButton, true);
            if (step.TeachRemoveFlyout) ui.SetRemoveToolUnrestricted();
            else ui.SetRemoveToolLock(step.HighlightRemove);
        }
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
                LoadingScreenManager.ExitToLevelSelect();
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
