# Graph Report - D:/Game Projects/CAPSTONE PROJ Main Proto  (2026-09-19)

## Corpus Check
- 145 files · ~974,183 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2577 nodes · 4310 edges · 248 communities (122 shown, 126 thin omitted)
- Extraction: 98% EXTRACTED · 2% INFERRED · 0% AMBIGUOUS · INFERRED: 74 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- CLI Terminal Panel UI
- Notification Toast System
- Compound Sidebar Flyout Menu
- Level 4 Tutorial Guide
- Auto-Save Manager
- Device Address & Level 5 Challenge
- Drag & Drop Manager
- Unity Package Manifest
- Scene Bootstrap
- Packet Flow Manager
- Ping Packet Visual Effect
- Device Prefab Builder (Editor)
- Cable Curve & Rendering
- Construction Spark Effect
- Cable Manager & Connection Flow
- Level Complete Panel
- Task Manager & Star Rating
- Level Select Manager
- Package Lock Dependencies
- Device Address Popup
- Interface Picker Popup
- Ping Checkmark Effect
- Tutorial Manager Core
- Package Lock Dependencies
- Package Lock Dependencies
- Level 2 Tutorial Guide
- Tutorial UI Manager
- Open-Ended Challenge Levels Plan
- Table Manager
- Topology Debug Overlay & Detector
- Main Menu Bootstrap
- Package Lock Dependencies
- Package Lock Dependencies
- Confirm Dialog Manager
- Network Device Core Model
- Packet Type Picker Popup
- Device Model Builder (PC/Router)
- Selection Indicator
- Package Lock Dependencies
- Main Menu Shell Builder
- World Pulse Marker
- Package Lock Dependencies
- Package Lock Dependencies
- Package Lock Dependencies
- Level 3 Tutorial Guide
- Table Placement Model
- Device Remover Manager
- Menu Button FX
- Packet Send Manager
- Sidebar Icon Button FX
- Slide Panel Animation
- Package Lock Dependencies
- Interaction Mode & Table Remover
- Cable Remover Manager
- Construction Reveal Effect
- Device Model Texture Builder
- Hologram Material
- Level Carousel UI
- Level Select Shell Builder
- Packet Device Effect
- Package Lock Dependencies
- Reachability/Ping Simulation
- Level 7 Scene Setup
- Level 3 Scene Setup
- Loading Screen Manager
- UI Spark Particles
- Device Ghost & Pulse Highlight
- Cross Icon Builder
- Device Icon Fallback Builder
- Level Template Baker (Editor)
- Main Menu UI Baker (Editor)
- Grid/Globe Icon Builder
- Package Lock Dependencies
- Package Lock Dependencies
- Audio Manager (SFX)
- Level Template Preview & Billboard Label
- CLI Command Template Parser
- Level Template Authoring Window (Editor)
- Level 2 Scene Setup
- Packet Travel Effect
- Plate Sprite Builder
- Pulsing Highlight
- Tutorial World Marker & Callout
- Package Lock Dependencies
- Icon Builder Params Struct
- Folder Icon Builder
- Music Manager
- Packet Data Model
- Smooth Scroll Rect
- Package Lock Dependencies
- Package Lock Dependencies
- Package Lock Dependencies
- Level Select Shell View
- Subnet Math Utility
- Table Footprint Placement Math
- Tutorial Marker Position Helpers
- Project Concept & Tech Stack Overview
- Checkmark Icon Builder
- Device Palette
- Tutorial Device Restriction
- Main Menu Canvas Builder
- Radial Glow Sprite
- Reachability Service Core
- Icon Texture Baking Utility
- Misc Start Hooks
- Palette Item
- Right-Click Handler
- Rounded Glow Outline Sprite
- Package Lock Dependencies
- Cable Connect Effect
- Sidebar Button Lock
- Level Catalog
- Level Manager
- Server Device (Unused Feature)
- Send Packet UI Action
- Unity Input System Config
- Main Menu Blank Button
- Ping Diagnostic Tool
- Blank Button Hover Sprite
- Blank Button Square Hover
- Packetry Logo
- Cable Icon
- Exit Icon
- Exit Icon (Hovered)
- Home Icon
- Home Hovered Icon
- PC Icon
- PC Icon (Clicked)
- Ping Icon
- Remove Cable Icon
- Remove Cable Icon (Clicked)
- Remove Device Icon
- Remove Device Icon (Clicked)
- Remove Table Icon
- Remove Table Icon (Hovered)
- Return Icon
- Return Icon (Hovered)
- Router Icon
- Router Icon (Clicked)
- Send Packets Icon
- Settings Icon
- Settings Icon (Hovered)
- Switch Icon
- Switch Icon (Clicked)
- Table Icon
- Table Icon (Clicked)
- Cable
- Cable
- PrimitiveType
- MaterialPropertyBlock
- MaterialPropertyBlock
- DeviceAddress
- DeviceAddress
- PingResult
- string
- Table
- bool
- IEnumerator
- List
- PingResult
- Button
- float
- RectTransform
- Sprite
- Transform
- Vector2
- Camera
- float
- List
- RectTransform
- Vector2
- Dictionary
- PrimitiveType
- string
- Vector3
- bool
- MaterialPropertyBlock
- Table
- Cable
- Table
- Clear Button
- Clear Button Hover
- CLI Clear Button (In-Game)
- CLI Design Test
- CLI Drag Modules Test
- CLI Drag Modules Test (Green)
- CLI Exit Button
- CLI Full Layout Test
- CLI Grey Gradient Test
- CLI Panel InGame Texture
- CLI Run Button (In-Game)
- CLI Sidebar InGame
- Exit Button Square
- Panel Template From Game (Screenshot)
- Panel Template From Game (Panel Size Reference)
- Panel Template Green
- Panel Template Green Button
- Panel Template Red
- Panel Template Red Button
- Panel Template v3 (Image)
- Panel Template v4 (Image)
- Panel Template v7
- Run Button
- Run Button Hover Texture
- SidebarBg Swatch
- AudioSource
- Bounds
- Canvas
- center
- Assembly-CSharp
- Manager Singleton Pattern (static Instance)
- com.unity.ai.navigation 2.0.11
- com.unity.inputsystem 1.19.0
- com.unity.test-framework 1.6.0
- com.unity.timeline 1.8.11
- com.unity.visualscripting 1.9.11
- EventTriggerType
- HashSet
- InterfaceState
- IReadOnlyList
- MenuItem
- Mesh
- PacketType
- RectTransform
- Hint/Tutorial Screenshot
- Hint Shot 2 Screenshot (Blank)
- Hint Shot 3 (Tutorial Welcome Step)
- Tutorial Welcome Screenshot
- Star Shot 2 Screenshot
- Level Complete Dialog Screenshot
- Shader
- Sprite
- TextMeshProUGUI
- Texture2D

## God Nodes (most connected - your core abstractions)
1. `CLIPanelManager` - 73 edges
2. `Cable` - 62 edges
3. `DragDropManager` - 57 edges
4. `TableManager` - 44 edges
5. `TutorialManager` - 43 edges
6. `NetworkUIManager` - 41 edges
7. `Table` - 39 edges
8. `CableManager` - 38 edges
9. `SettingsPanelManager` - 38 edges
10. `WorldPulseMarker` - 38 edges

## Surprising Connections (you probably didn't know these)
- `Phase B: Interface Configuration in RouterCLI` --references--> `CLIPanelManager`  [EXTRACTED]
  .claude/plans/what-is-a-good-polymorphic-meadow.md → Assets/Scripts/CLIPanelManager.cs
- `Phase C: Onboarding (Tab-Complete, Did-You-Mean, Help Panel)` --references--> `CLIPanelManager`  [EXTRACTED]
  .claude/plans/what-is-a-good-polymorphic-meadow.md → Assets/Scripts/CLIPanelManager.cs
- `ProBuilder Device Prefab Pipeline` --rationale_for--> `DeviceModelBuilder`  [EXTRACTED]
  CLAUDE.md → Assets/Scripts/DeviceModelBuilder.cs
- `Assembly-CSharp-Editor` --references--> `UIIconImporter`  [EXTRACTED]
  CLAUDE.md → Assets/Scripts/Editor/UIIconImporter.cs
- `CLI Rework: Simplified Interface Configuration + Onboarding` --references--> `Cable`  [EXTRACTED]
  .claude/plans/what-is-a-good-polymorphic-meadow.md → Assets/Scripts/Cable.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Level challenge task-completion flow (Level5Challenge template)** — claude_plans_open_ended_challenge_levels_level5challenge, claude_plans_open_ended_challenge_levels_handletaskschanged, claude_plans_open_ended_challenge_levels_savemanager_marklevelcompleted, claude_plans_open_ended_challenge_levels_levelcompletepanel_show [EXTRACTED 1.00]
- **Level 9 multi-subnet cross-reachability task pattern** — claude_plans_open_ended_challenge_levels_level9_multi_department_network, claude_plans_open_ended_challenge_levels_subnetmath_networkaddress, claude_plans_open_ended_challenge_levels_level7challenge_twosubnetsoftwo, claude_plans_open_ended_challenge_levels_allpairsreachable [EXTRACTED 1.00]
- **DHCP/Server feature explicitly excluded from level design scope** — claude_plans_open_ended_challenge_levels_devicetype_server, claude_plans_open_ended_challenge_levels_serverpoolconfig, claude_plans_open_ended_challenge_levels_serverpoolpopup, claude_plans_open_ended_challenge_levels_dhcp_not_a_feature_rationale [EXTRACTED 1.00]
- **CLI Rework Plan Phases A-D** — claude_plans_what_is_a_good_polymorphic_meadow_cli_rework, claude_plans_what_is_a_good_polymorphic_meadow_phase_a, claude_plans_what_is_a_good_polymorphic_meadow_phase_b, claude_plans_what_is_a_good_polymorphic_meadow_phase_c, claude_plans_what_is_a_good_polymorphic_meadow_phase_d [EXTRACTED 1.00]
- **Input-Priority Mutual-Exclusion Gate Participants** — assets_scripts_dragdropmanager_dragdropmanager, assets_scripts_clipanelmanager_clipanelmanager, assets_scripts_cablemanager_cablemanager, assets_scripts_cableremovermanager_cableremovermanager [EXTRACTED 1.00]
- **Manager Singletons Implementing the Static-Instance Pattern** — assets_scripts_dragdropmanager_dragdropmanager, assets_scripts_cablemanager_cablemanager, assets_scripts_cableremovermanager_cableremovermanager, assets_scripts_networkuimanager_networkuimanager, assets_scripts_clipanelmanager_clipanelmanager [EXTRACTED 1.00]

## Communities (248 total, 126 thin omitted)

### Community 0 - "CLI Terminal Panel UI"
Cohesion: 0.05
Nodes (34): CLIPanelManager, bool, Camera, Canvas, Color, Dictionary, EventTriggerType, float (+26 more)

### Community 1 - "Notification Toast System"
Cohesion: 0.06
Nodes (28): CanvasGroup, Color, Coroutine, float, IEnumerator, Image, RectTransform, TextMeshProUGUI (+20 more)

### Community 2 - "Compound Sidebar Flyout Menu"
Cohesion: 0.05
Nodes (27): CompoundSidebarGroup, bool, Button, Coroutine, Dictionary, float, GameObject, IEnumerator (+19 more)

### Community 3 - "Level 4 Tutorial Guide"
Cohesion: 0.05
Nodes (29): bool, DeviceType, Func, IEnumerator, int, List, NetworkDevice, Packet (+21 more)

### Community 4 - "Auto-Save Manager"
Cohesion: 0.06
Nodes (26): AutoSaveManager, NetworkDevice, Color, GameTheme, CableSave, bool, float, int (+18 more)

### Community 5 - "Device Address & Level 5 Challenge"
Cohesion: 0.05
Nodes (22): float, MaterialPropertyBlock, string, DeviceAddress, bool, IEnumerator, List, NetworkDevice (+14 more)

### Community 6 - "Drag & Drop Manager"
Cohesion: 0.08
Nodes (20): bool, Camera, center, Color, DeviceType, float, footprintXZ, GameObject (+12 more)

### Community 7 - "Unity Package Manifest"
Cohesion: 0.04
Nodes (48): dependencies, com.unity.ai.assistant, com.unity.ai.navigation, com.unity.collab-proxy, com.unity.ide.rider, com.unity.ide.visualstudio, com.unity.inputsystem, com.unity.modules.accessibility (+40 more)

### Community 8 - "Scene Bootstrap"
Cohesion: 0.06
Nodes (16): SampleScene.unity, Color, float, GameObject, Material, Vector3, GameBootstrap, bool (+8 more)

### Community 9 - "Packet Flow Manager"
Cohesion: 0.10
Nodes (22): float, HashSet, IEnumerator, int, List, NetworkDevice, Packet, PacketType (+14 more)

### Community 10 - "Ping Packet Visual Effect"
Cohesion: 0.10
Nodes (21): Color, float, IEnumerator, List, Material, NetworkDevice, Transform, Vector3 (+13 more)

### Community 11 - "Device Prefab Builder (Editor)"
Cohesion: 0.11
Nodes (21): AssetPostprocessor, float, GameObject, int, Material, MenuItem, Mesh, NetworkDevice (+13 more)

### Community 12 - "Cable Curve & Rendering"
Cohesion: 0.09
Nodes (17): Cable, CatenaryParams, bool, Color, Coroutine, float, IEnumerator, int (+9 more)

### Community 13 - "Construction Spark Effect"
Cohesion: 0.11
Nodes (16): ConstructionSparks, Color, float, Material, Vector3, Bounds, Color, float (+8 more)

### Community 14 - "Cable Manager & Connection Flow"
Cohesion: 0.09
Nodes (18): Cable Icon (Clicked), NetworkDevice, CableManager, Action, bool, Camera, Color, float (+10 more)

### Community 15 - "Level Complete Panel"
Cohesion: 0.08
Nodes (21): Action, Color, Color32, float, GameObject, Image, int, Sprite (+13 more)

### Community 16 - "Task Manager & Star Rating"
Cohesion: 0.07
Nodes (21): bool, Func, int, IReadOnlyList, List, string, GameTask, TaskManager (+13 more)

### Community 17 - "Level Select Manager"
Cohesion: 0.10
Nodes (18): Action, bool, Button, Color, Dictionary, float, GameObject, int (+10 more)

### Community 18 - "Package Lock Dependencies"
Cohesion: 0.07
Nodes (34): dependencies, depth, source, url, version, dependencies, depth, source (+26 more)

### Community 19 - "Device Address Popup"
Cohesion: 0.14
Nodes (14): Button, Color, GameObject, int, NetworkDevice, RectTransform, Sprite, string (+6 more)

### Community 20 - "Interface Picker Popup"
Cohesion: 0.11
Nodes (17): Action, Color, EventTriggerType, GameObject, int, NetworkDevice, RectTransform, Sprite (+9 more)

### Community 21 - "Ping Checkmark Effect"
Cohesion: 0.10
Nodes (15): Camera, Color, float, IEnumerator, MaterialPropertyBlock, NetworkDevice, Renderer, Sprite (+7 more)

### Community 22 - "Tutorial Manager Core"
Cohesion: 0.09
Nodes (9): Color, float, HashSet, IEnumerator, int, List, NetworkDevice, TutorialManager (+1 more)

### Community 23 - "Package Lock Dependencies"
Cohesion: 0.07
Nodes (30): dependencies, depth, source, version, dependencies, depth, source, version (+22 more)

### Community 24 - "Package Lock Dependencies"
Cohesion: 0.07
Nodes (30): dependencies, depth, source, version, dependencies, depth, source, version (+22 more)

### Community 25 - "Level 2 Tutorial Guide"
Cohesion: 0.12
Nodes (11): bool, float, Func, IEnumerator, int, List, NetworkDevice, string (+3 more)

### Community 26 - "Tutorial UI Manager"
Cohesion: 0.11
Nodes (16): Color, Coroutine, float, GameObject, IEnumerator, int, NetworkDevice, RectTransform (+8 more)

### Community 27 - "Open-Ended Challenge Levels Plan"
Cohesion: 0.13
Nodes (28): AllPairsReachable() helper, Build order: 8 and 9 first (no engine changes beyond AllPairsReachable), 10 and 11 need ReachabilityService/star-rating additions and have open questions, Keep star-rating override level-local rather than changing TaskManager globally, DragDropManager.OnDeviceDropped, GameBootstrap, GameBootstrap.SetupLevelObjectives, GameBootstrap.SetupUI, HandleTasksChanged (+20 more)

### Community 28 - "Table Manager"
Cohesion: 0.13
Nodes (9): bool, Camera, Color, float, GameObject, Material, Renderer, Transform (+1 more)

### Community 29 - "Topology Debug Overlay & Detector"
Cohesion: 0.14
Nodes (15): float, IEnumerator, TextMeshProUGUI, TopologyDebugOverlay, Dictionary, HashSet, int, List (+7 more)

### Community 30 - "Main Menu Bootstrap"
Cohesion: 0.11
Nodes (10): Action, bool, Canvas, Color, GameObject, int, SlidePanel, string (+2 more)

### Community 31 - "Package Lock Dependencies"
Cohesion: 0.08
Nodes (26): dependencies, depth, source, version, dependencies, depth, source, version (+18 more)

### Community 32 - "Package Lock Dependencies"
Cohesion: 0.09
Nodes (25): depth, source, version, dependencies, depth, source, version, dependencies (+17 more)

### Community 33 - "Confirm Dialog Manager"
Cohesion: 0.12
Nodes (14): ConfirmDialogManager, Action, Color, Color32, float, GameObject, RectTransform, SlidePanel (+6 more)

### Community 34 - "Network Device Core Model"
Cohesion: 0.12
Nodes (12): bool, Color, GameObject, List, MaterialPropertyBlock, string, Transform, Vector3 (+4 more)

### Community 35 - "Packet Type Picker Popup"
Cohesion: 0.15
Nodes (11): Action, Color, GameObject, int, PacketType, RectTransform, string, TextMeshProUGUI (+3 more)

### Community 36 - "Device Model Builder (PC/Router)"
Cohesion: 0.19
Nodes (8): DeviceType, GameObject, NetworkDevice, Rigidbody, Transform, Vector3, DeviceType, PrimitiveType

### Community 37 - "Selection Indicator"
Cohesion: 0.15
Nodes (12): bool, Camera, float, LineRenderer, List, Material, MaterialPropertyBlock, Renderer (+4 more)

### Community 38 - "Package Lock Dependencies"
Cohesion: 0.09
Nodes (23): dependencies, dependencies, depth, source, url, version, dependencies, depth (+15 more)

### Community 39 - "Main Menu Shell Builder"
Cohesion: 0.14
Nodes (15): Color, float, RectTransform, Sprite, string, TMP_FontAsset, Transform, Vector2 (+7 more)

### Community 40 - "World Pulse Marker"
Cohesion: 0.14
Nodes (14): bool, Camera, Color, float, Func, GameObject, int, LineRenderer (+6 more)

### Community 41 - "Package Lock Dependencies"
Cohesion: 0.09
Nodes (21): dependencies, depth, source, version, dependencies, depth, source, version (+13 more)

### Community 42 - "Package Lock Dependencies"
Cohesion: 0.09
Nodes (22): dependencies, depth, source, version, dependencies, depth, source, url (+14 more)

### Community 43 - "Package Lock Dependencies"
Cohesion: 0.10
Nodes (22): dependencies, depth, source, version, dependencies, depth, source, version (+14 more)

### Community 44 - "Level 3 Tutorial Guide"
Cohesion: 0.13
Nodes (10): bool, Func, IEnumerator, int, List, NetworkDevice, string, GuideStage (+2 more)

### Community 45 - "Table Placement Model"
Cohesion: 0.17
Nodes (6): float, IEnumerator, IReadOnlyList, List, Vector3, Table

### Community 46 - "Device Remover Manager"
Cohesion: 0.16
Nodes (9): bool, Camera, center, Color, footprintXZ, NetworkDevice, Vector2, Vector3 (+1 more)

### Community 47 - "Menu Button FX"
Cohesion: 0.13
Nodes (13): Action, bool, Camera, Color, Coroutine, IEnumerator, Image, PointerEventData (+5 more)

### Community 48 - "Packet Send Manager"
Cohesion: 0.17
Nodes (7): bool, Camera, List, MaterialPropertyBlock, NetworkDevice, PacketType, PacketSendManager

### Community 49 - "Sidebar Icon Button FX"
Cohesion: 0.15
Nodes (11): bool, Camera, Color, Coroutine, float, IEnumerator, Image, PointerEventData (+3 more)

### Community 50 - "Slide Panel Animation"
Cohesion: 0.18
Nodes (9): Action, Coroutine, float, Func, IEnumerator, RectTransform, Vector2, SlideEdge (+1 more)

### Community 51 - "Package Lock Dependencies"
Cohesion: 0.10
Nodes (20): dependencies, depth, source, version, dependencies, depth, source, version (+12 more)

### Community 52 - "Interaction Mode & Table Remover"
Cohesion: 0.16
Nodes (6): InteractionMode, bool, Camera, Color, Vector3, TableRemoverManager

### Community 53 - "Cable Remover Manager"
Cohesion: 0.18
Nodes (6): CableRemoverManager, bool, Camera, Color, float, Vector2

### Community 54 - "Construction Reveal Effect"
Cohesion: 0.23
Nodes (10): ConstructionRevealEffect, Action, Bounds, Color, float, GameObject, IEnumerator, Material (+2 more)

### Community 55 - "Device Model Texture Builder"
Cohesion: 0.22
Nodes (9): Color, float, int, Material, Mesh, Texture2D, DeviceModelBuilder, Color (+1 more)

### Community 56 - "Hologram Material"
Cohesion: 0.22
Nodes (9): Color, Color32, Dictionary, float, int, Material, Texture2D, Vector3 (+1 more)

### Community 57 - "Level Carousel UI"
Cohesion: 0.14
Nodes (10): bool, CanvasGroup, float, List, PointerEventData, RectTransform, ScrollRect, LevelCarousel (+2 more)

### Community 58 - "Level Select Shell Builder"
Cohesion: 0.27
Nodes (9): Color, float, int, RectTransform, string, TMP_FontAsset, Transform, Vector2 (+1 more)

### Community 59 - "Packet Device Effect"
Cohesion: 0.20
Nodes (10): Color, Coroutine, float, GameObject, IEnumerator, MaterialPropertyBlock, Renderer, string (+2 more)

### Community 60 - "Package Lock Dependencies"
Cohesion: 0.13
Nodes (15): dependencies, depth, source, url, version, depth, source, version (+7 more)

### Community 61 - "Reachability/Ping Simulation"
Cohesion: 0.25
Nodes (6): PingResult, Cable, HashSet, ReachabilityService, Node, Queue

### Community 62 - "Level 7 Scene Setup"
Cohesion: 0.30
Nodes (6): float, GameObject, NetworkDevice, string, Vector3, Level7SceneSetup

### Community 63 - "Level 3 Scene Setup"
Cohesion: 0.21
Nodes (5): float, GameObject, NetworkDevice, Vector3, Level3SceneSetup

### Community 64 - "Loading Screen Manager"
Cohesion: 0.22
Nodes (7): CanvasGroup, float, IEnumerator, Image, string, TextMeshProUGUI, LoadingScreenManager

### Community 65 - "UI Spark Particles"
Cohesion: 0.24
Nodes (9): Camera, Color, float, Image, int, RectTransform, Vector2, UISparkParticle (+1 more)

### Community 66 - "Device Ghost & Pulse Highlight"
Cohesion: 0.22
Nodes (3): Bounds, NetworkDevice, Vector3

### Community 67 - "Cross Icon Builder"
Cohesion: 0.20
Nodes (7): CrossIconBuilder, Color32, Dictionary, int, Sprite, Vector2, Params

### Community 68 - "Device Icon Fallback Builder"
Cohesion: 0.26
Nodes (6): Color32, Dictionary, int, Sprite, DeviceIconFallbackBuilder, Params

### Community 69 - "Level Template Baker (Editor)"
Cohesion: 0.26
Nodes (6): Color, float, Material, LevelTemplateBaker, PlayModeStateChange, Scene

### Community 70 - "Main Menu UI Baker (Editor)"
Cohesion: 0.26
Nodes (6): Color, GameObject, int, MenuItem, string, MainMenuUIBaker

### Community 71 - "Grid/Globe Icon Builder"
Cohesion: 0.20
Nodes (7): Color32, Dictionary, int, Sprite, Vector2, GridGlobeIconBuilder, Params

### Community 73 - "Package Lock Dependencies"
Cohesion: 0.17
Nodes (12): dependencies, depth, source, version, dependencies, depth, source, url (+4 more)

### Community 74 - "Package Lock Dependencies"
Cohesion: 0.17
Nodes (12): dependencies, depth, source, url, version, dependencies, depth, source (+4 more)

### Community 75 - "Audio Manager (SFX)"
Cohesion: 0.20
Nodes (6): AudioManager, AudioSource, Dictionary, float, HashSet, SfxId

### Community 76 - "Level Template Preview & Billboard Label"
Cohesion: 0.18
Nodes (5): BillboardLabel, Transform, TemplatePreviewOnly, TemplateTopologyLoader, MonoBehaviour

### Community 77 - "CLI Command Template Parser"
Cohesion: 0.22
Nodes (8): CLICommandTemplate, bool, Dictionary, List, string, TMP_InputField, Segment, Segment

### Community 78 - "Level Template Authoring Window (Editor)"
Cohesion: 0.20
Nodes (5): int, MenuItem, string, LevelTemplateAuthoringWindow, EditorWindow

### Community 79 - "Level 2 Scene Setup"
Cohesion: 0.33
Nodes (5): float, GameObject, NetworkDevice, Vector3, Level2SceneSetup

### Community 80 - "Packet Travel Effect"
Cohesion: 0.22
Nodes (6): bool, float, IEnumerator, Transform, Vector3, PacketTravelEffect

### Community 81 - "Plate Sprite Builder"
Cohesion: 0.24
Nodes (6): Color32, Dictionary, int, Sprite, Params, PlateSpriteBuilder

### Community 82 - "Pulsing Highlight"
Cohesion: 0.24
Nodes (5): bool, float, RectTransform, Vector3, PulsingHighlight

### Community 84 - "Package Lock Dependencies"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, url, version, dependencies, depth, source (+3 more)

### Community 85 - "Icon Builder Params Struct"
Cohesion: 0.27
Nodes (6): int, Params, int, Params, IEquatable, Params

### Community 86 - "Folder Icon Builder"
Cohesion: 0.22
Nodes (6): Color32, Dictionary, int, Sprite, FolderIconBuilder, Params

### Community 87 - "Music Manager"
Cohesion: 0.24
Nodes (5): AudioSource, float, HashSet, MusicId, MusicManager

### Community 88 - "Packet Data Model"
Cohesion: 0.29
Nodes (9): Color, float, int, List, Transform, Packet, PacketBranch, PacketState (+1 more)

### Community 89 - "Smooth Scroll Rect"
Cohesion: 0.22
Nodes (6): bool, float, PointerEventData, Vector2, SmoothScrollRect, ScrollRect

### Community 90 - "Package Lock Dependencies"
Cohesion: 0.20
Nodes (10): dependencies, depth, source, version, dependencies, depth, source, version (+2 more)

### Community 91 - "Package Lock Dependencies"
Cohesion: 0.20
Nodes (10): dependencies, depth, source, version, dependencies, depth, source, version (+2 more)

### Community 92 - "Package Lock Dependencies"
Cohesion: 0.20
Nodes (10): depth, source, version, dependencies, depth, source, url, version (+2 more)

### Community 93 - "Level Select Shell View"
Cohesion: 0.22
Nodes (8): GameObject, List, RectTransform, SlidePanel, TextMeshProUGUI, LevelSelectShellView, SmoothScrollRect, VerticalLayoutGroup

### Community 95 - "Table Footprint Placement Math"
Cohesion: 0.28
Nodes (4): center, Vector3, length, width

### Community 97 - "Project Concept & Tech Stack Overview"
Cohesion: 0.22
Nodes (9): Capstone Networking Game Prototype, Educational Networking Game Concept & Design Philosophy, graphify Claude Code Skill, com.unity.render-pipelines.universal 17.4.0, CLI Rework: Simplified Interface Configuration + Onboarding, CLI Mode-Navigation Hierarchy (User EXEC -> Privileged EXEC -> Global Config -> Interface Config), Unity 6 (6000.4.1f1), Universal Render Pipeline (URP) (+1 more)

### Community 98 - "Checkmark Icon Builder"
Cohesion: 0.29
Nodes (5): CheckmarkIconBuilder, Color32, Dictionary, Sprite, Vector2

### Community 99 - "Device Palette"
Cohesion: 0.36
Nodes (4): float, Transform, Vector3, DevicePalette

### Community 100 - "Tutorial Device Restriction"
Cohesion: 0.25
Nodes (6): Action, bool, DeviceType, Func, string, TutorialStep

### Community 101 - "Main Menu Canvas Builder"
Cohesion: 0.43
Nodes (4): Canvas, Color, Transform, MainMenuCanvasBuilder

### Community 102 - "Radial Glow Sprite"
Cohesion: 0.43
Nodes (3): Material, Sprite, RadialGlowSprite

### Community 103 - "Reachability Service Core"
Cohesion: 0.43
Nodes (6): bool, int, List, string, Node, PingResult

### Community 106 - "Palette Item"
Cohesion: 0.33
Nodes (3): Color, Renderer, PaletteItem

### Community 107 - "Right-Click Handler"
Cohesion: 0.33
Nodes (4): PointerEventData, UnityAction, RightClickHandler, IPointerClickHandler

### Community 108 - "Rounded Glow Outline Sprite"
Cohesion: 0.40
Nodes (4): float, int, Sprite, RoundedGlowOutlineSprite

### Community 110 - "Package Lock Dependencies"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, url, version, com.unity.collab-proxy

### Community 111 - "Cable Connect Effect"
Cohesion: 0.40
Nodes (4): CableConnectEffect, Color, float, int

### Community 113 - "Level Catalog"
Cohesion: 0.50
Nodes (3): List, LevelCatalog, ScriptableObject

### Community 115 - "Server Device (Unused Feature)"
Cohesion: 0.83
Nodes (4): DeviceType.Server, DHCP/Server exists in code but is not an intended feature, ServerPoolConfig, ServerPoolPopup

### Community 116 - "Send Packet UI Action"
Cohesion: 0.67
Nodes (3): Send Packet Icon (Clicked), Packet Flow Visualization, Send Packets UI Action

## Knowledge Gaps
- **371 isolated node(s):** `com.unity.ai.assistant`, `com.unity.ai.navigation`, `com.unity.collab-proxy`, `com.unity.ide.rider`, `com.unity.ide.visualstudio` (+366 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **126 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `CLIPanelManager` connect `CLI Terminal Panel UI` to `Project Concept & Tech Stack Overview`, `Network Device Core Model`, `Level 4 Tutorial Guide`, `Drag & Drop Manager`, `Scene Bootstrap`, `Cable Curve & Rendering`, `Level Template Preview & Billboard Label`, `Interface Picker Popup`?**
  _High betweenness centrality (0.075) - this node is a cross-community bridge._
- **Why does `NetworkDevice` connect `Network Device Core Model` to `CLI Terminal Panel UI`, `Project Concept & Tech Stack Overview`, `Reachability Service Core`, `Cable Curve & Rendering`, `Level Template Preview & Billboard Label`, `Cable Manager & Connection Flow`, `Interface Picker Popup`, `Packet Data Model`, `Packet Device Effect`, `Reachability/Ping Simulation`?**
  _High betweenness centrality (0.059) - this node is a cross-community bridge._
- **Why does `Cable` connect `Cable Curve & Rendering` to `CLI Terminal Panel UI`, `Project Concept & Tech Stack Overview`, `Network Device Core Model`, `Auto-Save Manager`, `Selection Indicator`, `Scene Bootstrap`, `Packet Flow Manager`, `Ping Packet Visual Effect`, `Level Template Preview & Billboard Label`, `Cable Manager & Connection Flow`, `Interface Picker Popup`, `Cable Remover Manager`, `Construction Reveal Effect`, `Tutorial Manager Core`?**
  _High betweenness centrality (0.055) - this node is a cross-community bridge._
- **What connects `com.unity.ai.assistant`, `com.unity.ai.navigation`, `com.unity.collab-proxy` to the rest of the system?**
  _375 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `CLI Terminal Panel UI` be split into smaller, more focused modules?**
  _Cohesion score 0.051526251526251524 - nodes in this community are weakly interconnected._
- **Should `Notification Toast System` be split into smaller, more focused modules?**
  _Cohesion score 0.057971014492753624 - nodes in this community are weakly interconnected._
- **Should `Compound Sidebar Flyout Menu` be split into smaller, more focused modules?**
  _Cohesion score 0.052214452214452214 - nodes in this community are weakly interconnected._