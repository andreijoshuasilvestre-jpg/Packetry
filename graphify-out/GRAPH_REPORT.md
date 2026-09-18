# Graph Report - .  (2026-08-18)

## Corpus Check
- 13 files · ~526,372 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1748 nodes · 2980 edges · 105 communities (69 shown, 36 thin omitted)
- Extraction: 98% EXTRACTED · 2% INFERRED · 0% AMBIGUOUS · INFERRED: 74 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Drag & Drop Manager
- CLI Command Template & Highlight
- Cable Curve & Packet Rendering
- Device Model Builder
- Tutorial UI Manager
- Main Menu Bootstrap
- Auto-Save Manager
- Menu Button FX
- Unity Package Manifest
- Tutorial Manager
- Table Manager
- Scene Bootstrap
- Router CLI State Machine
- Cable Manager
- Device Palette
- Device Address Popup
- Interface Picker Popup
- Package Dependency Chain
- Package Dependency Chain
- Level 2 Scene Setup
- Package Dependency Chain
- Level 2 Tutorial Guide
- Package Dependency Chain
- Package Dependency Chain
- Package Dependency Chain
- Level 3 Tutorial Guide
- Packet Type Picker Popup
- Package Dependency Chain
- Package Lock Dependencies
- Level Complete Panel
- Package Dependency Chain
- Cable Remover Manager
- Network Device Model
- Packet Send Manager
- Notification Manager
- Packet Manager
- Package Dependency Chain
- Device Remover Manager
- Package Dependency Chain
- Loading Screen Manager
- Level Carousel UI
- Level Select Manager
- Packet Device Effect
- Ping Tool Manager
- Task UI Manager
- Package Dependency Chain
- Package Dependency Chain
- Level 4 Challenge
- Reachability Ping Flood Logic
- Package Dependency Chain
- Package Dependency Chain
- Pulsing Highlight Effect
- Task Manager
- Package Dependency Chain
- Billboard Label & Level Manager
- Value Equality Helpers
- Packet Visual Glyph Builder
- Smooth Scroll Rect
- Package Dependency Chain
- Audio Manager
- UI Builder Helpers
- Packet Model
- Checkmark Icon Builder
- Plate Sprite Builder
- Packet Visual Profile
- Reachability Simulation Service
- Package Dependency Chain
- Assembly & Solution Config
- Sidebar Button Lock
- Topology Loader
- Packet Flow Visualization Concept
- UI Registration Helpers
- Input System Config
- Main Menu UI Sprites
- Ping Tool Icon
- Blank Button Hover Sprite
- Packetry Game Logo
- Cable Tool Icon
- PC Device Icon
- PC Icon Clicked State
- Ping Icon
- Remove Cable Icon
- Remove Cable Icon Clicked
- Remove Device Icon
- Remove Device Icon Clicked
- Router Device Icon
- Router Icon Clicked State
- Send Packets Icon
- Switch Device Icon
- Switch Icon Clicked State
- Table Prop Icon
- Table Icon Clicked State
- Read-Only List Type
- Hash Set Type
- Rect Transform Type
- Sprite Type
- TextMeshPro UGUI Type
- Texture2D Type
- Assembly-CSharp Target
- Manager Singleton Pattern
- AI Navigation Package
- Input System Package
- Test Framework Package
- Timeline Package
- Visual Scripting Package

## God Nodes (most connected - your core abstractions)
1. `NetworkDevice` - 92 edges
2. `CLIPanelManager` - 63 edges
3. `DragDropManager` - 56 edges
4. `Cable` - 46 edges
5. `TableManager` - 43 edges
6. `TutorialManager` - 41 edges
7. `WorldPulseMarker` - 38 edges
8. `CableManager` - 37 edges
9. `RouterCLI` - 32 edges
10. `NetworkUIManager` - 30 edges

## Surprising Connections (you probably didn't know these)
- `Phase B: Interface Configuration in RouterCLI` --references--> `CLIPanelManager`  [EXTRACTED]
  .claude/plans/what-is-a-good-polymorphic-meadow.md → Assets/Scripts/CLIPanelManager.cs
- `Phase C: Onboarding (Tab-Complete, Did-You-Mean, Help Panel)` --references--> `CLIPanelManager`  [EXTRACTED]
  .claude/plans/what-is-a-good-polymorphic-meadow.md → Assets/Scripts/CLIPanelManager.cs
- `CLI Rework: Simplified Interface Configuration + Onboarding` --references--> `CLIPanelManager`  [EXTRACTED]
  .claude/plans/what-is-a-good-polymorphic-meadow.md → Assets/Scripts/CLIPanelManager.cs
- `Phase D: Camera Focus, Floating World-Space CLI, Cable Highlight` --references--> `CLIPanelManager`  [EXTRACTED]
  .claude/plans/what-is-a-good-polymorphic-meadow.md → Assets/Scripts/CLIPanelManager.cs
- `TextMeshPro` --references--> `CLIPanelManager`  [EXTRACTED]
  CLAUDE.md → Assets/Scripts/CLIPanelManager.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **CLI Rework Plan Phases A-D** — claude_plans_what_is_a_good_polymorphic_meadow_cli_rework, claude_plans_what_is_a_good_polymorphic_meadow_phase_a, claude_plans_what_is_a_good_polymorphic_meadow_phase_b, claude_plans_what_is_a_good_polymorphic_meadow_phase_c, claude_plans_what_is_a_good_polymorphic_meadow_phase_d [EXTRACTED 1.00]
- **Input-Priority Mutual-Exclusion Gate Participants** — assets_scripts_dragdropmanager_dragdropmanager, assets_scripts_clipanelmanager_clipanelmanager, assets_scripts_cablemanager_cablemanager, assets_scripts_cableremovermanager_cableremovermanager [EXTRACTED 1.00]
- **Manager Singletons Implementing the Static-Instance Pattern** — assets_scripts_dragdropmanager_dragdropmanager, assets_scripts_cablemanager_cablemanager, assets_scripts_cableremovermanager_cableremovermanager, assets_scripts_networkuimanager_networkuimanager, assets_scripts_clipanelmanager_clipanelmanager [EXTRACTED 1.00]

## Communities (105 total, 36 thin omitted)

### Community 0 - "Drag & Drop Manager"
Cohesion: 0.05
Nodes (29): bool, Camera, Color, float, GameObject, int, Material, MaterialPropertyBlock (+21 more)

### Community 1 - "CLI Command Template & Highlight"
Cohesion: 0.06
Nodes (30): CLICommandTemplate, bool, Dictionary, List, string, TMP_InputField, Segment, CLIPanelManager (+22 more)

### Community 2 - "Cable Curve & Packet Rendering"
Cohesion: 0.05
Nodes (33): Cable, CatenaryParams, bool, Color, Coroutine, float, IEnumerator, int (+25 more)

### Community 3 - "Device Model Builder"
Cohesion: 0.07
Nodes (30): AssetPostprocessor, Color, float, GameObject, Material, PrimitiveType, Rigidbody, Transform (+22 more)

### Community 4 - "Tutorial UI Manager"
Cohesion: 0.06
Nodes (27): Color, float, GameObject, int, string, Transform, Vector3, TutorialUIManager (+19 more)

### Community 5 - "Main Menu Bootstrap"
Cohesion: 0.06
Nodes (27): Action, bool, Button, Color, float, RectTransform, Sprite, string (+19 more)

### Community 6 - "Auto-Save Manager"
Cohesion: 0.06
Nodes (22): AutoSaveManager, MaterialPropertyBlock, DeviceAddress, Color, GameTheme, CableSave, bool, float (+14 more)

### Community 7 - "Menu Button FX"
Cohesion: 0.05
Nodes (32): Action, bool, Camera, Color, Coroutine, IEnumerator, Image, PointerEventData (+24 more)

### Community 8 - "Unity Package Manifest"
Cohesion: 0.04
Nodes (48): dependencies, com.unity.ai.assistant, com.unity.ai.navigation, com.unity.collab-proxy, com.unity.ide.rider, com.unity.ide.visualstudio, com.unity.inputsystem, com.unity.modules.accessibility (+40 more)

### Community 9 - "Tutorial Manager"
Cohesion: 0.07
Nodes (16): Action, bool, Cable, Color, float, Func, IEnumerator, int (+8 more)

### Community 10 - "Table Manager"
Cohesion: 0.09
Nodes (15): bool, Camera, Color, float, GameObject, Material, MaterialPropertyBlock, Renderer (+7 more)

### Community 11 - "Scene Bootstrap"
Cohesion: 0.08
Nodes (14): SampleScene.unity, Color, float, GameObject, Material, Vector3, GameBootstrap, bool (+6 more)

### Community 12 - "Router CLI State Machine"
Cohesion: 0.08
Nodes (23): bool, int, List, string, InterfaceState, Mode, RouterCLI, Capstone Networking Game Prototype (+15 more)

### Community 13 - "Cable Manager"
Cohesion: 0.09
Nodes (19): Cable Icon (Clicked), CableManager, Action, bool, Cable, Camera, Color, float (+11 more)

### Community 14 - "Device Palette"
Cohesion: 0.08
Nodes (16): float, Transform, Vector3, DevicePalette, DeviceType, ButtonKind, Button, Color (+8 more)

### Community 15 - "Device Address Popup"
Cohesion: 0.12
Nodes (11): Button, Color, GameObject, int, RectTransform, TextMeshProUGUI, TMP_InputField, Transform (+3 more)

### Community 16 - "Interface Picker Popup"
Cohesion: 0.09
Nodes (16): Action, Color, GameObject, int, RectTransform, TextMeshProUGUI, Transform, Vector2 (+8 more)

### Community 17 - "Package Dependency Chain"
Cohesion: 0.07
Nodes (30): dependencies, depth, source, version, dependencies, depth, source, version (+22 more)

### Community 18 - "Package Dependency Chain"
Cohesion: 0.08
Nodes (29): dependencies, depth, source, url, version, dependencies, depth, source (+21 more)

### Community 19 - "Level 2 Scene Setup"
Cohesion: 0.12
Nodes (10): float, GameObject, Vector3, Level2SceneSetup, float, IEnumerator, IReadOnlyList, List (+2 more)

### Community 20 - "Package Dependency Chain"
Cohesion: 0.08
Nodes (26): dependencies, depth, source, version, dependencies, depth, source, version (+18 more)

### Community 21 - "Level 2 Tutorial Guide"
Cohesion: 0.12
Nodes (11): bool, DeviceAddress, float, Func, IEnumerator, int, List, string (+3 more)

### Community 22 - "Package Dependency Chain"
Cohesion: 0.09
Nodes (25): dependencies, depth, source, version, dependencies, depth, source, version (+17 more)

### Community 23 - "Package Dependency Chain"
Cohesion: 0.08
Nodes (25): dependencies, depth, source, version, dependencies, depth, source, version (+17 more)

### Community 24 - "Package Dependency Chain"
Cohesion: 0.09
Nodes (25): depth, source, version, dependencies, depth, source, version, dependencies (+17 more)

### Community 25 - "Level 3 Tutorial Guide"
Cohesion: 0.11
Nodes (10): bool, DeviceAddress, Func, IEnumerator, int, List, PingResult, string (+2 more)

### Community 26 - "Packet Type Picker Popup"
Cohesion: 0.15
Nodes (10): Action, Color, GameObject, int, RectTransform, string, TextMeshProUGUI, Transform (+2 more)

### Community 27 - "Package Dependency Chain"
Cohesion: 0.09
Nodes (23): dependencies, dependencies, depth, source, url, version, dependencies, depth (+15 more)

### Community 28 - "Package Lock Dependencies"
Cohesion: 0.09
Nodes (21): dependencies, depth, source, version, dependencies, depth, source, version (+13 more)

### Community 29 - "Level Complete Panel"
Cohesion: 0.17
Nodes (10): Action, Color, float, GameObject, TextMeshProUGUI, Transform, UnityAction, Vector2 (+2 more)

### Community 30 - "Package Dependency Chain"
Cohesion: 0.10
Nodes (21): dependencies, depth, source, version, dependencies, depth, source, version (+13 more)

### Community 31 - "Cable Remover Manager"
Cohesion: 0.17
Nodes (7): CableRemoverManager, bool, Cable, Camera, Color, float, Vector2

### Community 32 - "Network Device Model"
Cohesion: 0.16
Nodes (10): bool, Cable, GameObject, MaterialPropertyBlock, string, Transform, Vector3, NetworkDevice (+2 more)

### Community 33 - "Packet Send Manager"
Cohesion: 0.18
Nodes (6): bool, Camera, List, MaterialPropertyBlock, PacketSendManager, PacketType

### Community 34 - "Notification Manager"
Cohesion: 0.16
Nodes (10): CanvasGroup, Color, Coroutine, float, IEnumerator, Image, RectTransform, TextMeshProUGUI (+2 more)

### Community 35 - "Packet Manager"
Cohesion: 0.28
Nodes (5): PacketType, HashSet, int, List, PacketManager

### Community 36 - "Package Dependency Chain"
Cohesion: 0.12
Nodes (17): dependencies, depth, source, version, dependencies, depth, source, url (+9 more)

### Community 37 - "Device Remover Manager"
Cohesion: 0.19
Nodes (5): bool, Camera, Color, MaterialPropertyBlock, DeviceRemoverManager

### Community 38 - "Package Dependency Chain"
Cohesion: 0.12
Nodes (16): dependencies, depth, source, version, dependencies, depth, source, version (+8 more)

### Community 39 - "Loading Screen Manager"
Cohesion: 0.19
Nodes (7): CanvasGroup, float, IEnumerator, Image, string, TextMeshProUGUI, LoadingScreenManager

### Community 40 - "Level Carousel UI"
Cohesion: 0.15
Nodes (8): bool, float, List, PointerEventData, ScrollRect, LevelCarousel, IBeginDragHandler, IEndDragHandler

### Community 41 - "Level Select Manager"
Cohesion: 0.22
Nodes (10): Color, float, GameObject, int, List, Sprite, string, TMP_FontAsset (+2 more)

### Community 42 - "Packet Device Effect"
Cohesion: 0.20
Nodes (10): Color, Coroutine, float, GameObject, IEnumerator, MaterialPropertyBlock, Renderer, string (+2 more)

### Community 43 - "Ping Tool Manager"
Cohesion: 0.22
Nodes (4): bool, Camera, MaterialPropertyBlock, PingToolManager

### Community 44 - "Task UI Manager"
Cohesion: 0.16
Nodes (8): Color, float, Image, List, RectTransform, TextMeshProUGUI, TaskUIManager, GameTask

### Community 45 - "Package Dependency Chain"
Cohesion: 0.13
Nodes (15): dependencies, depth, source, url, version, depth, source, version (+7 more)

### Community 46 - "Package Dependency Chain"
Cohesion: 0.13
Nodes (15): dependencies, depth, source, version, dependencies, depth, source, version (+7 more)

### Community 47 - "Level 4 Challenge"
Cohesion: 0.16
Nodes (5): bool, IEnumerator, List, PingResult, Level4Challenge

### Community 48 - "Reachability Ping Flood Logic"
Cohesion: 0.26
Nodes (5): PingResult, HashSet, ReachabilityService, Node, Queue

### Community 49 - "Package Dependency Chain"
Cohesion: 0.17
Nodes (12): dependencies, depth, source, version, dependencies, depth, source, url (+4 more)

### Community 50 - "Package Dependency Chain"
Cohesion: 0.17
Nodes (12): dependencies, depth, source, url, version, dependencies, depth, source (+4 more)

### Community 51 - "Pulsing Highlight Effect"
Cohesion: 0.24
Nodes (5): bool, float, RectTransform, Vector3, PulsingHighlight

### Community 52 - "Task Manager"
Cohesion: 0.20
Nodes (7): bool, Func, IReadOnlyList, List, string, GameTask, TaskManager

### Community 53 - "Package Dependency Chain"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, url, version, dependencies, depth, source (+3 more)

### Community 54 - "Billboard Label & Level Manager"
Cohesion: 0.20
Nodes (5): BillboardLabel, Transform, int, LevelManager, MonoBehaviour

### Community 55 - "Value Equality Helpers"
Cohesion: 0.27
Nodes (6): int, Params, int, Params, IEquatable, Params

### Community 56 - "Packet Visual Glyph Builder"
Cohesion: 0.29
Nodes (6): Color, Dictionary, Material, MaterialPropertyBlock, Transform, PacketVisual

### Community 57 - "Smooth Scroll Rect"
Cohesion: 0.22
Nodes (6): bool, float, PointerEventData, Vector2, SmoothScrollRect, ScrollRect

### Community 58 - "Package Dependency Chain"
Cohesion: 0.20
Nodes (10): dependencies, depth, source, version, dependencies, depth, source, version (+2 more)

### Community 59 - "Audio Manager"
Cohesion: 0.25
Nodes (5): AudioManager, Dictionary, HashSet, SfxId, AudioSource

### Community 60 - "UI Builder Helpers"
Cohesion: 0.33
Nodes (4): Action, Button, RectTransform, Transform

### Community 61 - "Packet Model"
Cohesion: 0.31
Nodes (8): Color, float, int, List, Transform, Packet, PacketBranch, PacketState

### Community 62 - "Checkmark Icon Builder"
Cohesion: 0.29
Nodes (5): CheckmarkIconBuilder, Color32, Dictionary, Sprite, Vector2

### Community 63 - "Plate Sprite Builder"
Cohesion: 0.32
Nodes (4): Color32, Dictionary, Sprite, PlateSpriteBuilder

### Community 64 - "Packet Visual Profile"
Cohesion: 0.29
Nodes (6): float, int, PrimitiveType, string, Vector3, Profile

### Community 65 - "Reachability Simulation Service"
Cohesion: 0.43
Nodes (6): bool, int, List, string, Node, PingResult

### Community 66 - "Package Dependency Chain"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, url, version, com.unity.collab-proxy

### Community 67 - "Assembly & Solution Config"
Cohesion: 0.50
Nodes (4): Assembly-CSharp, netstandard2.1, Assembly-CSharp-Editor, netstandard2.1

### Community 70 - "Packet Flow Visualization Concept"
Cohesion: 0.67
Nodes (3): Send Packet Icon (Clicked), Packet Flow Visualization, Send Packets UI Action

## Knowledge Gaps
- **324 isolated node(s):** `netstandard2.1`, `netstandard2.1`, `ButtonKind`, `Mode`, `com.unity.ai.assistant` (+319 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **36 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `NetworkDevice` connect `Network Device Model` to `Drag & Drop Manager`, `CLI Command Template & Highlight`, `Cable Curve & Packet Rendering`, `Device Model Builder`, `Tutorial UI Manager`, `Auto-Save Manager`, `Tutorial Manager`, `Router CLI State Machine`, `Cable Manager`, `Device Palette`, `Device Address Popup`, `Interface Picker Popup`, `Level 2 Scene Setup`, `Level 2 Tutorial Guide`, `Level 3 Tutorial Guide`, `Packet Send Manager`, `Packet Manager`, `Device Remover Manager`, `Packet Device Effect`, `Ping Tool Manager`, `Level 4 Challenge`, `Reachability Ping Flood Logic`, `Billboard Label & Level Manager`, `Packet Model`, `Reachability Simulation Service`?**
  _High betweenness centrality (0.153) - this node is a cross-community bridge._
- **Why does `CLIPanelManager` connect `CLI Command Template & Highlight` to `Network Device Model`, `Drag & Drop Manager`, `Cable Curve & Packet Rendering`, `Scene Bootstrap`, `Router CLI State Machine`, `Device Palette`, `Billboard Label & Level Manager`?**
  _High betweenness centrality (0.077) - this node is a cross-community bridge._
- **Why does `LevelSelectManager` connect `Level Select Manager` to `Main Menu Bootstrap`, `Level Carousel UI`, `Billboard Label & Level Manager`, `UI Builder Helpers`, `Plate Sprite Builder`?**
  _High betweenness centrality (0.071) - this node is a cross-community bridge._
- **What connects `netstandard2.1`, `netstandard2.1`, `ButtonKind` to the rest of the system?**
  _328 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Drag & Drop Manager` be split into smaller, more focused modules?**
  _Cohesion score 0.052982456140350874 - nodes in this community are weakly interconnected._
- **Should `CLI Command Template & Highlight` be split into smaller, more focused modules?**
  _Cohesion score 0.06316590563165905 - nodes in this community are weakly interconnected._
- **Should `Cable Curve & Packet Rendering` be split into smaller, more focused modules?**
  _Cohesion score 0.0546583850931677 - nodes in this community are weakly interconnected._