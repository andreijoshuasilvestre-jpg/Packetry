# CLI Rework: Simplified Interface Configuration + Onboarding

## Context

The CLI ([RouterCLI.cs](Assets/Scripts/RouterCLI.cs) + [CLIPanelManager.cs](Assets/Scripts/CLIPanelManager.cs)) is Cisco-IOS-styled but currently has no concept of "interfaces" at all — a router is just a hostname with mode-switching. That's a real gap since interfaces (not the whole router) are what carry IP addresses in real networking, and future subnetting work will need that per-interface addressing to hang off of. At the same time, the CLI offers zero forgiveness (no autocomplete, no typo tolerance) and no in-panel guidance, and there's no task/level system yet to teach commands externally.

**Scope note:** this plan is interfaces only — naming/selecting them, cabling them to a specific port, and assigning a bare IP address via `ip address <ip> <mask>` with no more than a dotted-quad shape check. No subnet math, validation, or subnet-zone concepts are introduced here; that's a separate future effort building on top of this per-interface addressing.

The simplification principle: **keep the mode-navigation hierarchy** (User EXEC → Privileged EXEC → Global Config → Interface Config) because that hierarchy — "go to a specific place to configure a specific thing" — is itself the lesson. Cut everything IOS has that doesn't serve that lesson (no `speed`/`duplex`/`description`, no realistic-but-arbitrary interface names like `GigabitEthernet0/0`). Interfaces are auto-named `eth0`, `eth1`, ... and map 1:1 to a **fixed number of physical ports already sculpted onto the device models** — `BuildRouter()` and `BuildSwitch()` in [DeviceModelBuilder.cs](Assets/Scripts/DeviceModelBuilder.cs) already draw 3 and 5 port-slot decorations respectively; they're just cosmetic today. This plan makes them functional, ties cabling to specific ports, and lets the CLI address those same ports as interfaces — reinforcing the visual/gamified angle over abstract text.

Bundled into the same effort: Tab-completion and a fuzzy "did you mean" suggestion (mirroring real IOS Tab-complete, since the CLI already imitates IOS), plus an always-visible help panel listing the current mode's commands — this is how players learn the CLI without a separate tutorial system.

Also bundled: when a player is inside `interface ethN` config, the cable plugged into that physical port highlights, so they can always see which cable they're configuring even if the CLI panel covers part of the screen. This pairs with a camera focus — opening a Router's CLI zooms the camera in and floats the CLI panel above the device (in world space) instead of a fixed centered overlay, so the device and its ports stay visible in frame instead of being hidden behind a full CLI panel.

## Phase A — Functional per-device ports + Packet-Tracer-style interface picker

Currently `NetworkDevice` ([NetworkDevice.cs](Assets/Scripts/NetworkDevice.cs)) has one fixed connection point (`GetConnectionPoint()`) and an unordered `List<NetworkDevice> connections` with no capacity limit — this is also why a cable currently just connects at the device's center regardless of which port. Only [CableManager.cs](Assets/Scripts/CableManager.cs) and [Cable.cs](Assets/Scripts/Cable.cs) touch this API (confirmed — no other call sites), so it's safe to rework.

- **`NetworkDevice.cs`**: replace the flat connection list with a fixed-size port array set at construction (`Router`=3, `Switch`=5, `PC`=1 — matching/extending the existing decorative slot counts). Each port tracks its local attach offset, the device connected to it (or null = free), and the `Cable` occupying it (used later by Phase D's highlight).
  - `GetPortWorldPoint(int index)` — world position of a port, for cable rendering.
  - `GetFreePorts()` — indices of unoccupied ports, for populating the picker popup.
  - `ConnectPort(int index, NetworkDevice other, Cable cable)`.
  - Keep `GetConnectionPoint()` as a general fallback anchor (device center) — used only for the in-progress drag-preview line before the source port has been chosen.
- **`DeviceModelBuilder.cs`**: after constructing each device's `NetworkDevice` component, pass it the same local offsets already used to draw the port-slot cubes in `BuildRouter()`/`BuildSwitch()`, so the functional ports line up with the visible slots. Add one small port decoration + a single-port init to `BuildPC()` (currently has none).
- **New `InterfacePickerPopup.cs`**: a small self-contained popup widget (own `ScreenSpaceOverlay` canvas, built once at startup, hidden by default) — mirrors Cisco Packet Tracer's connection dialog. Public API: `Show(NetworkDevice device, Vector2 screenPos, Action<int> onPicked)` — lists the device's free interfaces (`eth0`, `eth1`, ...) as clickable buttons at `screenPos` (reuse the `MakeRect`/`MakeTMP`/button-building pattern already established in [CLIPanelManager.cs](Assets/Scripts/CLIPanelManager.cs)), or a non-selectable "No available interfaces" row if `GetFreePorts()` is empty. Dismissing without a pick (click elsewhere / right-click) just hides it.
- **`CableManager.cs`**: replace the immediate two-click connect flow in `TrySelect()` with a picker-driven one:
  - Clicking a device while no source is chosen yet → open the popup for that device; picking an interface commits `sourceDevice`/`sourcePort`, highlights the device, and the drag-preview line now runs from `sourceDevice.GetPortWorldPoint(sourcePort)`.
  - Clicking a different device next → open the popup for that device; picking an interface calls `CreateCable(sourceDevice, sourcePort, device, chosenPort)` and cancels the operation (same cleanup `Cancel()` already does).
  - While the popup is open, `Update()` skips its own raycast/click handling (new `IsPickerOpen`-style guard, same pattern as the existing `IsOpen`/`IsConnecting` checks already used between this class, `DragDropManager`, and `CLIPanelManager`) so clicks go to the popup buttons instead of the 3D scene.
  - Right-click still cancels the whole operation, including closing an open popup.
- **`Cable.cs`**: store the two chosen port indices and use `GetPortWorldPoint(portIndex)` instead of `GetConnectionPoint()` in `Refresh()`, so cables visually run from the exact chosen interface, not the device's center.

This also means the interface name a player picks while cabling (`eth0`, `eth1`, ...) is the exact name they'll reference later in the CLI (`interface eth0` in Phase B) — reinforcing rather than duplicating the lesson.

## Phase B — Interface configuration in `RouterCLI.cs`

- Add `Mode.InterfaceConfig` (prompt: `Router(config-if)#`).
- `RouterCLI` is constructed with the owning `NetworkDevice` (not just a hostname string) so it knows the port count and can read `deviceName`. Update the one call site in `CLIPanelManager.GetOrCreateSession`.
- Internal per-port state: IP address, subnet mask, and shutdown flag (default **shutdown = true**, matching real IOS — "interfaces are down until configured" is a deliberate, worthwhile lesson to keep).
- `Global Config` gains `interface eth<N>` → validates N against the device's port count, enters `InterfaceConfig`, remembers which port is being edited.
- New `ExecInterfaceCfg`: `ip address <ip> <mask>` (basic dotted-quad shape check, not full validation), `shutdown`, `no shutdown`, `exit` (back to Global Config), `?`/`help`.
- `Privileged EXEC` gains `show ip interface brief` — lists `eth0..ethN-1` with IP and up/administratively-down status, reusing the per-port state. Cheap addition once the state exists, and a standard real-world sanity-check command.

## Phase C — Onboarding: Tab-complete, "did you mean", always-visible help

- Extract each mode's command list (User EXEC, Privileged EXEC, Global Config, **and the new Interface Config**) into small `(string name, string desc)[]` tables in `RouterCLI.cs`, and render the existing `?`/`help` output from them instead of hardcoded strings — one source of truth instead of duplicated lists.
- Add `CurrentCommands` (table for `_mode`) and `MatchPrefix(string prefix)` (case-insensitive prefix match within the current mode) to `RouterCLI`.
- Convert `NotRecognized` to an instance method; add a Levenshtein-distance suggestion against `CurrentCommands` (only suggest when the edit distance is small relative to token length, to avoid nonsense matches). Scope: completion/suggestion only apply to the first token (the command itself), not sub-arguments like the `<ip> <mask>` in `ip address`.
- **`CLIPanelManager.cs`**:
  - Disable default Tab focus-navigation on the input field (`Navigation.Mode.None`); poll `Keyboard.current.tabKey` in `Update()` while the panel is open and focused, and complete the first token via `MatchPrefix` (0 matches → no-op, 1 → autofill + trailing space, >1 → print candidates to the output area without touching input).
  - Build a second panel beside the CLI panel (reuse the existing `MakeRect`/`MakeTMP`/`StretchToFill` helpers already in this file) showing `RouterCLI.CurrentCommands` for the active device/mode, or a placeholder for non-router devices. Refresh it everywhere `SetPrompt()` is already called (`Open()`, and after every mode transition in `OnInputSubmit`) so it updates live, including on entering/leaving Interface Config.

No `GameBootstrap.cs` changes — the help panel and world-space canvas live inside the already-registered `CLIPanelManager`, and port logic extends the existing `NetworkDevice`/`CableManager`/`Cable` trio.

## Phase D — Camera focus, floating world-space CLI, cable highlight (Router only)

Scoped to Router for now, since PC/Switch CLI has no real commands yet (per decision above); extend later once they do. Camera pan/zoom is locked while the CLI is open, mirroring the existing pattern where `DragDropManager.Update()` already disables drag input while `CLIPanelManager.Instance.IsOpen` ([DragDropManager.cs:43](Assets/Scripts/DragDropManager.cs#L43)).

- **`IsometricCamera.cs`**: currently only `orbitDistance` is lerped toward a target each frame (`HandleZoom`); pan (`currentLookTarget`) is applied instantly. Add a `targetLookTarget` that also lerps every frame (so both manual zoom and scripted focus animate the same way), and split input-reading from lerp-application so input can be skipped while locked without breaking the animation:
  - `Update()`: read scroll/pan input only when `CLIPanelManager.Instance == null || !CLIPanelManager.Instance.IsOpen`; always apply the `orbitDistance`/`currentLookTarget` lerp toward their targets.
  - Add a singleton `Instance` (matching the `CableManager`/`DragDropManager` pattern already used elsewhere) and `public void FocusOn(Vector3 lookTarget, float distance)` plus getters for the current target values so `CLIPanelManager` can save the pre-focus state and restore it via the same method on close.
- **Cable highlight**: give `Cable.cs` a `SetHighlight(bool on)` using the same `MaterialPropertyBlock` technique `CableManager.HighlightDevice` already uses ([CableManager.cs:151-169](Assets/Scripts/CableManager.cs#L151-L169)), applied to the `LineRenderer`. `NetworkDevice`'s port struct (from Phase A) additionally stores the `Cable` reference connecting that port, set by `CableManager.CreateCable`. `RouterCLI` exposes the currently-open interface's port index (already tracked for the `(config-if)#` prompt); `CLIPanelManager` calls `device.GetPortCable(index)?.SetHighlight(...)` on entering/leaving Interface Config and on panel close.
- **Floating world-space panel**: rather than duplicating all the panel-building code for a second UI, keep one set of widgets and reparent the existing content between two canvas hosts:
  - `BuildPanel()` wraps the CLI panel and the Phase C help panel in one container transform (`_contentRoot`) under the current `ScreenSpaceOverlay` canvas (unchanged default, still used for PC/Switch).
  - Add a second, initially-empty `WorldSpace` canvas (`worldCamera = Camera.main`) built once at startup.
  - In `Open()`, if the device is a Router: reparent `_contentRoot` under the world canvas, position the world canvas above the device (`device.transform.position + Vector3.up * offset`), apply a small canvas `localScale` so the pixel-sized panel reads at a sensible world size, and call `IsometricCamera.Instance.FocusOn(device.transform.position, focusDistance)` (saving the prior look target/distance first). For PC/Switch, reparent `_contentRoot` back under the screen-space canvas (original anchored-center behavior) — no camera change.
  - In `Close()`, if a focus was applied, restore the camera via `FocusOn(savedLookTarget, savedDistance)` and clear the cable highlight.

## Files touched
- [Assets/Scripts/NetworkDevice.cs](Assets/Scripts/NetworkDevice.cs)
- [Assets/Scripts/DeviceModelBuilder.cs](Assets/Scripts/DeviceModelBuilder.cs)
- [Assets/Scripts/CableManager.cs](Assets/Scripts/CableManager.cs)
- [Assets/Scripts/Cable.cs](Assets/Scripts/Cable.cs)
- `Assets/Scripts/InterfacePickerPopup.cs` (new)
- [Assets/Scripts/RouterCLI.cs](Assets/Scripts/RouterCLI.cs)
- [Assets/Scripts/CLIPanelManager.cs](Assets/Scripts/CLIPanelManager.cs)
- [Assets/Scripts/IsometricCamera.cs](Assets/Scripts/IsometricCamera.cs)

## Verification

In the Unity Editor (Play mode):
1. Start a cable connection and click a Router — a popup lists `eth0`/`eth1`/`eth2`; picking one starts the drag-preview from that exact port. Click a second device — its own popup appears; picking an interface completes the cable at that exact port (not the device center). Connecting a 4th cable to a 3-port device shows "No available interfaces" instead of connecting.
2. Cables visually attach at the router's individual port-slot decorations, matching whichever interface was picked.
3. Open the Router's CLI → camera smoothly zooms/focuses on the router, CLI panel floats above it in world space, help panel shows User EXEC commands. `enable` → `configure terminal` → help panel updates each time. Manual scroll/middle-mouse pan do nothing while open.
4. `interface eth0` → prompt becomes `Router(config-if)#`, help panel shows `ip address`, `shutdown`, `no shutdown`, `exit`, and the cable plugged into port 0 highlights.
5. `ip address 192.168.1.1 255.255.255.0` then `no shutdown` then `exit` back to Global Config — cable highlight clears. `end` to Privileged EXEC, `show ip interface brief` → shows `eth0` up with that IP, `eth1`/`eth2` administratively down.
6. Type `e` + Tab in User EXEC → ambiguous (`enable`/`exit`) → candidates listed, input untouched. `en` + Tab → completes to `enable `.
7. Type `enabl` + Enter → "did you mean 'enable'?" instead of a flat unrecognized error.
8. Close the CLI → camera smoothly returns to its prior pan/zoom position, manual controls work again.
9. Open CLI on a PC/Switch → original centered screen-space overlay behavior (no camera change), help panel shows the placeholder, no errors.
