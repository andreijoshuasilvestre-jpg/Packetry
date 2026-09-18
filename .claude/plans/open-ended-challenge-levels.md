# Open-Ended Challenge Levels (8–11)

## Context

The current 7 levels (see [LevelSelectManager.cs](Assets/Scripts/LevelSelectManager.cs)) end with three "Challenges" — [Level5Challenge.cs](Assets/Scripts/Level5Challenge.cs), [Level6Challenge.cs](Assets/Scripts/Level6Challenge.cs), [Level7Challenge.cs](Assets/Scripts/Level7Challenge.cs) — that already drop the guided-stage/sidebar-locking style of Levels 2–4 in favor of a blank canvas plus a [TaskManager.cs](Assets/Scripts/TaskManager.cs) checklist. But their task conditions still hardcode an exact shape (`placedPCs.Count >= 2 && placedSwitch != null`, `cabled >= 2`) — "unguided" but not truly open-ended, since there's only one correct shape to build.

Goal for 8–11: write task conditions against **network properties** (reachability, subnet count, redundancy, efficiency) instead of prescribed device/cable counts, so any topology the player designs that satisfies the property passes. Follow the exact `Level5Challenge.cs` template: `InitNextFrame` seeding of already-placed devices (for save-restore), `DragDropManager.OnDeviceDropped`/`PingToolManager.OnPingCompleted` hooks, `HandleTasksChanged` → `SaveManager.MarkLevelCompleted` → `LevelCompletePanel.Show`. Each new level also needs a `LevelSelectManager` entry and `GameBootstrap.SetupLevelObjectives`/`SetupUI` wiring (see `GameBootstrap.cs` lines ~233–252 for the existing `level == N` branches).

**Scope note:** DHCP (`DeviceType.Server`, [ServerPoolConfig.cs](Assets/Scripts/ServerPoolConfig.cs), `ServerPoolPopup.cs`) already exists in code but is explicitly **not** an intended feature — don't build a level around it. It's a candidate for later removal if a cleanup pass happens, but leave it alone unless asked.

## Level 8 — Free Build: Office LAN

Goal: every PC placed on the table ends up mutually reachable — no minimum count, any topology.

- Task: "Every PC can reach every other PC." Needs a new `AllPairsReachable()` helper — loop `ReachabilityService.Ping` over all `(pc, pc)` pairs among currently-placed PCs; true once there are ≥2 PCs and every pair succeeds.
- Deliberately no prescribed switch/router count or arrangement.

## Level 9 — Multi-Department Network

Goal: player builds at least 3 subnets ("departments"), each internally reachable and cross-reachable via router(s).

- Task: "At least 3 distinct subnets exist" — same `SubnetMath.NetworkAddress` grouping pattern as `Level7Challenge.TwoSubnetsOfTwo`, just uncapped (`>= 3` groups instead of `>= 2`).
- Task: "Every subnet can reach every other subnet" — cross-group all-pairs reachability, reusing the Level 8 helper.
- No prescribed PC-per-department count or router count/arrangement (hub-and-spoke vs. router-chain both pass).

## Level 10 — Resilience Challenge

Goal: no single cable is a point of failure between departments.

- Task: "Every PC pair stays reachable after any one cable is cut." Needs a new `ReachabilityService` capability: iterate cables, virtually exclude one at a time, re-run the reachability BFS, restore.
- **Open risk to check before committing:** `PacketManager` has no STP/loop-safety yet — confirm redundant-link topologies (loops) don't double-flood packets before this level ships. May need to scope this to router-to-router redundancy only if switch loops aren't safe yet.

## Level 11 — Budget Build

Goal: hit a reachability target (same shape as Level 8) using the fewest cables/devices — an optimization puzzle rather than a checklist.

- Needs a level-local override of the star rating: `TaskManager.ComputeStars()` is currently purely hint-based (3★ = no hints used); this level wants an efficiency-based tier instead (e.g. 3★ under N cables). Keep it level-local rather than changing `TaskManager` globally.

## Build order

8 and 9 need no engine changes beyond `AllPairsReachable()` and an uncapped subnet-count check — build these first. 10 and 11 need small `ReachabilityService`/star-rating additions and have open questions (loop safety, efficiency scoring) worth resolving before implementation.
