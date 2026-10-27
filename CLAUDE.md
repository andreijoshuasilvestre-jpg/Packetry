# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Unity 6 (6000.4.1f1) game prototype using the Universal Render Pipeline (URP). The project targets Windows 64-bit standalone but is pre-configured with a mobile render pipeline as well.

### Game Concept

An educational networking game inspired by Cisco Packet Tracer, but more visual and gamified. Players learn basic networking concepts through interactive simulation.

**Core Features:**
- **Drag & drop devices** — place PCs, switches, and routers on a canvas
- **Cable connections** — draw cables between devices to wire up a network
- **Packet flow visualization** — packets represented as particles traveling along cables (not yet implemented)
- **Simplified CLI** — interact with devices via a minimal command-line interface (implemented for routers; other device types echo-only)
- **Subnet zones** — visual areas that represent subnets for subnetting concepts (not yet implemented)

**Scope (intentionally limited):**
- Devices: PCs, Cables, Routers, Switches only
- Networking concepts: basic connectivity, IP addressing, subnetting (simple)
- Level-based progression with tasks to complete per level (not yet implemented)

**Design Philosophy:**
- Core concepts are fixed (devices, cables, packet flow, CLI, subnet zones, level tasks) but execution and visual representation are open — how things look, feel, and are implemented is flexible
- Quality of life features (UI polish, convenience tools, onboarding, etc.) are welcome additions as the project evolves
- Keep it simple and feasible — this is a capstone prototype, not a full simulator
- Code for flexibility/scalability even at prototype stage — avoid hardcoded special cases, keep systems loosely coupled (e.g. device-type-specific behavior should extend cleanly rather than branch everywhere)

## Development Workflow

This is a Unity project; building and running are done through the **Unity Editor**, not the command line.

- **Open project**: Launch Unity Hub → open `d:\Game Projects\CAPSTONE PROJ Main Proto`
- **Play in editor**: Press Play in the Unity toolbar (Enter Play Mode) — the entire scene is generated at runtime by `GameBootstrap` (see Architecture below), so there is nothing to inspect in the scene hierarchy until Play is pressed
- **Build**: File → Build and Run (targets Windows x86_64 by default)
- **Run tests**: Window → General → Test Runner → Run All (the `com.unity.test-framework` package is installed, but no test assemblies exist yet — a `Tests/` folder with its own `.asmdef` referencing `Assembly-CSharp` would need to be created first)

For C# editing, open the solution in VS Code:
```
code "CAPSTONE PROJ Main Proto.slnx"
```

The `.vscode/launch.json` is pre-configured for Unity debugger attachment (port 56000).

### Graphify

The [graphify](https://github.com/Graphify-Labs/graphify) Claude Code skill is installed (`graphifyy` PyPI package, registered globally in `~/.claude/skills/graphify/`). Run `/graphify .` to build a knowledge graph of this codebase into `graphify-out/`.

## Architecture

### Everything is generated at runtime — there are no pre-placed scene objects

`Assets/Scenes/SampleScene.unity` contains essentially just a `Bootstrap` GameObject with `GameBootstrap.cs` attached. On `Awake()`, `GameBootstrap` procedurally builds the entire scene in order: camera (adds `IsometricCamera`), lighting, play area (ground plane + border strips), manager singletons, then UI/EventSystem. `DeviceModelBuilder` is still the single place that assembles a device (`BuildPC()`, `BuildSwitch()`, `BuildRouter()`) — when modifying device visuals or adding a new device type, edit `DeviceModelBuilder`, not a `.prefab` directly.

PC/Switch/Router now *are* backed by real `.prefab` assets, though: `DevicePrefabBuilder` (`Assets/Scripts/Editor/DevicePrefabBuilder.cs`, menu `Tools > Capstone > Build Device Prefabs (ProBuilder)`) authors each device's geometry with ProBuilder's `ShapeGenerator` — Unity's own in-editor modeling tool, used here instead of an external DCC app like Blender — bakes the parts into `Mesh` assets under `Assets/Meshes/Devices/`, and saves each finished device as `Assets/Resources/DevicePrefabs/{PC,Switch,Router}.prefab`. `DeviceModelBuilder.BuildPC/BuildSwitch/BuildRouter()` load and `Instantiate()` those prefabs via `Resources.Load` first; each falls back to the original `GameObject.CreatePrimitive()`-based construction (now `Build*Procedural()`) only if the prefab hasn't been generated yet, so the project still runs before the Editor tool has been run once. `Table` is deliberately **not** prefab-backed — its footprint is chosen per-instance by the player at placement time, so it stays procedural in `DeviceModelBuilder.BuildTable()`. Re-run the menu item after changing any device's geometry in `DevicePrefabBuilder`; it wipes and regenerates `Assets/Meshes/Devices/` and `Assets/Resources/DevicePrefabs/` but leaves `Assets/Materials/Devices/*.mat` alone so hand-tuned colors survive.

### Level entry sequence

`GameBootstrap.EntrySequence` is the single place a level's entrance is ordered: platform (ground rise + wall drop) → starting topology builds in (`LevelBuildInEffect.Play`) → HUD (`HudRevealStarted`; `NetworkUIManager`/`TaskUIManager` wait on it) → narration heads-up on a narrated level (see Narration below) → guidance (`IntroSequenceReady`; guides/challenges wait on it). Starting topology comes from exactly one of: a resumed save (`TopologyLoader`), a template scene (`TemplateTopologyLoader`, merges a frame or more late — reports in via `sceneLoaded`), or a hand-written `LevelNSceneSetup`; every path ends in `GameBootstrap.OnTopologyLoaded`, which sets `TopologyLoaded` and hides the topology (`LevelBuildInEffect.Prepare`) before it renders. Guides must wait for `TopologyLoaded` before scanning pre-placed devices. Load-time cables use `CableManager.CreatePrebuiltCable` (no player-placement reveal, no `OnCableCreated`). A resumed save's task state goes to `TaskManager.RestoreCompleted` and is applied as each task registers; guided levels set `TaskManager.CompleteInOrder` and start on `CurrentTaskIndex`, never a hard-coded stage 0. A finished run (every saved task complete) of a guided level is replayed fresh, not resumed; challenge levels (objective providers implementing `IKeepsFinishedBuild`) restore the player's finished build instead, without replaying the Level Complete panel — the HUD's Restart is what starts one over. Level → objective provider mapping lives in `GameBootstrap.ObjectiveProviderFor`.

### Manager singletons (static `Instance`, one per scene)

Each manager is a `MonoBehaviour` with a `public static X Instance` set in `Awake()` (self-destroys duplicates). They coordinate through each other's `Instance` and public state rather than events:
- `DragDropManager` — palette → drag → drop → place flow; also re-picks already-placed devices to move them. Owns the single input-priority gate: checks `CLIPanelManager.IsOpen`, `CableManager.IsConnecting`, `CableRemoverManager.IsRemoving` before handling clicks, so only one interaction mode is ever "live" at a time. Any new interaction mode must be added to this mutual-exclusion chain. Overlays that pause *all* gameplay input — the Settings dialog, and any annotation waiting on its own button (tutorial Next steps, concept cards, warning windows; see `AnnotationLock`) — are checked once via `InteractionMode.IsSuspended`; a new blocking overlay goes there, not into each manager.
- `CableManager` — click-source → click-target cable creation, using `InterfacePickerPopup` to choose which free port on each device. Draws the live preview and finished cables via `Cable`'s shared catenary-curve math.
- `CableRemoverManager` — click-a-cable-to-delete mode.
- `NetworkUIManager` — builds the HUD (sidebar buttons that call into `DragDropManager`/`CableManager`/`CableRemoverManager`, status bar). Built entirely in code (`Canvas` → `Image`/`Button`/`TextMeshProUGUI`), no `.prefab`/`.uxml`.
- `CLIPanelManager` — builds and owns the in-game terminal overlay UI (also code-built, using TextMeshPro). Keeps one `RouterCLI` session per placed router in a `Dictionary<NetworkDevice, RouterCLI>` so CLI state (mode, hostname, interface config) persists across panel open/close.

### Device / port / cable model

- `NetworkDevice` (`Assets/Scripts/NetworkDevice.cs`) holds a fixed `Port[]` array set once via `InitPorts(Vector3[] localOffsets)` at build time — ports are local-space attach points on the model, not dynamically added. `ConnectPort`/`DisconnectPort`/`GetFreePorts()` track which ports are wired.
- `Cable` renders as a sagging parabolic curve between two device ports (`Cable.FillCurvePoints` is reused by `CableManager` to draw the in-progress preview with identical shape). A cable holds references to both endpoint `NetworkDevice`s and auto-destroys itself if either device is destroyed.
- `DeviceType` enum (`PC`, `Switch`, `Router`) drives both `DeviceModelBuilder` mesh construction and `CLIPanelManager`'s per-type CLI behavior — currently only `Router` gets a real `RouterCLI` session; other types just echo input.

### RouterCLI — simplified Cisco IOS state machine

`RouterCLI` (plain C# class, not a `MonoBehaviour`) implements a 4-mode command interpreter (`UserExec` → `PrivilegedExec` → `GlobalConfig` → `InterfaceConfig`), mirroring real IOS prompts (`>`, `#`, `(config)#`, `(config-if)#`). `Execute(string)` returns `null` for silent success, `"__CLOSE__"` to signal the panel should close, or output text to display. Interface state (`ip`/`mask`/`shutdown`) is tracked per-port in a private array sized to `device.PortCount`. When extending CLI commands, follow the existing per-mode `Exec*(cmd, p)` switch pattern, and add the command's plain-language explanation to `RouterCommandDocs` (keyed by the exact command-table name): its kind tag (`MOVE`/`CHANGE`/`VIEW`/`HELP`, see `CLICommandDoc.cs`), what it does, its `<params>`, an example, and — via `TargetMode` — where it takes you. `RouterCLI.Describe`/`DescribeMode` serve these to the CLI panel's chip tags, hover cards and mode ladder.

### Self-explaining UI

The game is meant to explain itself without a guide:
- `HoverTooltip.Attach(go, () => new TooltipContent { ... }, side)` gives any UI element an explain-on-hover card (`HoverTooltipView` draws it on its own top canvas; `HoverTooltipTrigger` is the per-target hook). New buttons, tools and panels should attach one.
- `ViewControlsHud` — bottom-right on-screen zoom out/in (press = one step, hold = keep zooming — the way in for a trackpad with no wheel), turn left/right (Q/E), reset view, and 2D buttons; goes through `IsometricCamera.ZoomBy`/`CanZoomFurther`/`RotateStep`/`ResetView`/`CanRotate`. Toasts stack above it via `BottomRightReservedHeight`.
- Tables resize from handles on a selected table's corners and edge midpoints (`TableManager`, picked in screen space — no colliders). The opposite side stays put, a side can't be dragged back past the devices on the table, and an overlapping size falls back to the last one that fit. `DeviceModelBuilder.ResizeTable` is the one place a footprint is applied (`BuildTable` uses the same layout). `TableManager.OnTableResized` feeds `UndoManager` and `AutoSaveManager`. A new interaction mode must also stand down while `TableManager.IsResizing`.
- `DeviceRotateHud` — bottom-center rotate-left/right buttons, shown only while a device is selected (`DragDropManager.CanRotateHighlighted`); each click is `RotateHighlighted(±1)`, an eased turn to the next 45° mark. Hold-R-and-scroll stays as the free-angle alternative.
- Both clusters build their buttons with `HudIconButton` — use it for any new on-screen control so the style stays shared.
- `NetworkDiagramPanel` — the 2D logical (Packet-Tracer-style) diagram: a live, draggable window showing devices, subnet-colored links, and per-port status lights.
- Valid subnet masks run /1 (128.0.0.0) through /30 — see `SubnetMath.MinUsablePrefix`/`MaxUsablePrefix`; don't reintroduce the classful /8 floor.

### Animation speed

Settings → Animation Speed has one 1x–3x multiplier (quarter steps) per `AnimationKind`: `Packets` (pings, packet transfers, PC screen send/receive, cable flow dots), `Build` (the player placing a device, table or cable: `ConstructionRevealEffect`, the table axis-grow, the landing pop) and `Level` (the entrance/exit in `GameBootstrap`, `LevelBuildInEffect`, the HUD reveal, the camera intro zoom). An animation follows its setting by stepping its timer with `AnimationSpeed.Delta`/`UnscaledDelta` instead of `Time.deltaTime`, and shortening fixed waits with `AnimationSpeed.Seconds`. Timestamp-based animation reads `AnimationSpeed.Clock` instead of `Time.time` (see `PCMonitor`). Durations stay authored at 1x. A new animation should pick its kind and use these, not raw `Time.deltaTime`. `PacketDeviceEffect` is shared, so its callers pass the kind.

### Narration (voice-over)

Annotations — tutorial callouts (`TutorialUIManager`), concept cards (`ConceptCardManager`) and warning windows (`AnnotationWarningManager`) — are read aloud by `NarrationManager` while Settings → Tutorial Narration is on (`SettingsData.narrationEnabled`).
- Clips are pre-generated offline with Kokoro TTS (voice `af_heart`), not synthesized at runtime. `Tools/Narration/lines.json` lists every line exactly as the game shows it (`vars`/`cases` templates cover lines Level 4 fills in at runtime); `Tools/Narration/generate_narration.py` voices them into `Assets/Resources/Narration/<key>.ogg`, where key = `NarrationManager.KeyFor(text)`, an FNV-1a hash of the tag-stripped text (the Python `clip_key` must stay identical). **When you add or reword an annotation, update its `lines.json` entry and re-run the generator** (setup is in the script's docstring). A line with no clip just stays silent and logs a `[Narration] No voice clip` warning.
- One voice at a time; each annotation passes itself as the speaker. `TextTypewriter.Reveal(..., speaker)` paces the typing to the voice, and `NarrationWaveform` is the per-panel speaking indicator (hidden while narration is off, so panels keep their default look).
- A level whose objective provider implements `INarratedLevel` opens with a heads-up popup (`NarrationManager.ConfirmForLevel`, run from `EntrySequence`), asked once per level per session. The voice has its own Narration Volume slider (`SettingsData.narrationVolume`, via the static `NarrationManager.SetVolume`), independent of SFX.

### Input

- Uses Unity's **New Input System** (not legacy `Input.GetKey`) — all managers poll `Mouse.current` / `Keyboard.current` directly in `Update()`; no `PlayerInput` action-map bindings are used for the networking gameplay itself
- Action map defined in `Assets/InputSystem_Actions.inputactions` with a `Player` map containing: `Move` (Vector2), `Look` (Vector2), `Attack` (Button), `Interact` (Hold), `Crouch` (Button) — currently unused by the networking gameplay; left over from the default template

### Rendering
- **URP** with separate render pipeline assets for PC (`PC_RPAsset.asset`) and Mobile (`Mobile_RPAsset.asset`) under `Assets/Settings/`
- Post-processing is handled via `DefaultVolumeProfile.asset` — add volume overrides there, not per-scene
- Devices and cables use runtime-generated `Material` instances (`Universal Render Pipeline/Lit` for solids, `Universal Render Pipeline/Particles/Unlit` for cable lines) rather than material assets

### Key Packages
| Package | Version | Purpose |
|---|---|---|
| `com.unity.render-pipelines.universal` | 17.4.0 | URP rendering |
| `com.unity.inputsystem` | 1.19.0 | New Input System |
| `com.unity.ai.navigation` | 2.0.11 | NavMesh / AI pathfinding |
| `com.unity.timeline` | 1.8.11 | Cutscenes / animation sequencing |
| `com.unity.test-framework` | 1.6.0 | Play mode & edit mode tests (no tests written yet) |
| `com.unity.visualscripting` | 1.9.11 | Visual scripting graphs |
| `com.unity.probuilder` | 6.1.2 | In-editor mesh modeling — authors the PC/Switch/Router prefabs (see `DevicePrefabBuilder`) |
| TextMeshPro | (bundled) | All in-game UI text (`CLIPanelManager`, `NetworkUIManager`, `InterfacePickerPopup`) |

### Scene Structure
- Single scene: `Assets/Scenes/SampleScene.unity` — holds only the `Bootstrap` GameObject; everything else is created by `GameBootstrap` at runtime

### Assembly Layout
- `Assembly-CSharp` — all runtime game scripts (everything under `Assets/` except `Editor/` folders)
- `Assembly-CSharp-Editor` — editor-only scripts (inside `Assets/**/Editor/` folders); includes `UIIconImporter` (texture import settings for `Assets/Resources/UIIcons/`) and `DevicePrefabBuilder` (ProBuilder-based device prefab generation)

## Conventions

- Runtime scripts live in `Assets/Scripts/` (flat, no subfolders yet)
- One `MonoBehaviour` per file, with the file named after the class — Unity silently never calls `Update`/`OnDisable` on a MonoBehaviour added from a file with a different name
- Editor utilities go in `Assets/Scripts/Editor/`
- URP shader graphs belong in `Assets/Shaders/` (doesn't exist yet)
- `Library/`, `Temp/`, `Logs/`, and `UserSettings/` are Unity-generated — do not edit them
- New device-construction code goes in `DeviceModelBuilder`; new interaction modes should register themselves in `DragDropManager`'s mutual-exclusion checks (see Architecture above) so they don't fight with drag/cable/remove modes
