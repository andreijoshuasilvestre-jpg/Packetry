using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Plays a level's starting topology (tables, devices, cables already placed
/// in the scene by TopologyLoader/TemplateTopologyLoader/a hand-written
/// LevelNSceneSetup) building itself in the moment the scene appears,
/// instead of everything just popping into existence — same visual language
/// as GameBootstrap's ground-rise/wall-drop entrance (scale up from near-
/// zero, ease-out-quad, a white ConstructionDust puff on landing), not the
/// player-placement blueprint-sweep (see ConstructionRevealEffect), which
/// stays reserved for the player's own drag/drop actions. A pre-placed
/// device isn't something the player just built by hand; it reads more
/// naturally as part of the room materializing than as an individual
/// construction event.
///
/// Two steps, both driven by GameBootstrap's entry sequence:
///  - Prepare() — called the instant the level's topology is in the scene
///    (inline in GameBootstrap.Awake for a saved/scripted topology, from
///    SceneManager.sceneLoaded for a template scene). Snaps every table and
///    device to invisible (scale MinGrowScale) and every cable to zero-drawn
///    (RevealProgress 0) synchronously, before Unity renders a frame, so
///    nothing sits fully visible while the room is still assembling.
///  - Play() — called once the platform (ground + walls) has finished.
///    Tables first, then devices ordered Router > Switch > PC (see
///    DevicePriority — a level missing one of those types just skips it,
///    order is a priority, not a requirement), then cables. Cables get no
///    scale/dust reveal of their own — a cable's own rendering already
///    follows its two devices' port positions live every frame, so a
///    gradual grow wouldn't read as anything different from an instant pop
///    anyway — but they DO wait their turn and only pop fully in, instantly,
///    once devices have finished.
///
/// PlayExitForScene is the reverse, played by GameBootstrap.
/// ExitSequenceRoutine before the platform itself shrinks away: cables
/// vanish immediately, then devices shrink PC > Switch > Router (the exact
/// reverse of the entrance priority), then table(s) shrink last — topology
/// clears off the platform before the platform disappears out from under it.
///
/// PlayForItem/PlayExitForItems are the same two animations scoped to just
/// the pieces one mid-level change touches (UndoManager's Ctrl+Z), rather
/// than the whole scene — a restored device grows back in exactly like it
/// would on level entry, a removed one shrinks away like it would on exit.
///
/// All of it follows the Level animation speed setting (see AnimationSpeed),
/// the undo-scoped versions included, since they're the same animations.
/// </summary>
public static class LevelBuildInEffect
{
    // Per-item start offset, not a full reveal duration — keeps even a
    // ~10-item level (Level 9) to a couple of seconds total instead of
    // stacking every item's own GrowDuration back to back.
    const float StaggerDelay = 0.12f;

    // Matches GameBootstrap's own GroundRiseDuration/WallDropDuration
    // exactly (that class keeps its own private copy of this constant) so
    // pre-placed topology grows in at the same visible speed as the room
    // itself, not a mismatched pace.
    const float GrowDuration = 0.4f;

    // Matches GameBootstrap's own ExitWallDuration/ExitGroundDuration —
    // exit is much quicker than the entrance, same as the room's own
    // shrink-away (leaving a level shouldn't be a hassle to sit through).
    const float ShrinkDuration = 0.18f;

    // Exit stagger between items, but with the whole spread capped — a
    // level the player filled with devices (Free Build, say) clears in the
    // same short beat as a two-device one instead of taking longer the more
    // they built.
    const float ExitStaggerDelay = 0.03f;
    const float ExitMaxSpread    = 0.3f;

    // Not literally 0 — mirrors GameBootstrap's own MinGrowScale, avoiding a
    // degenerate zero-scale collider while a device/table is mid-grow.
    const float MinGrowScale = 0.001f;

    // The hidden-and-waiting build-in between Prepare() and Play(). A Unity
    // object, so if its scene unloads before Play() it just reads as null.
    static Runner prepared;

    /// <summary>Hides this level's starting topology until Play() — see class doc.
    /// Call once, the moment that topology is fully in the scene.</summary>
    public static void Prepare()
    {
        var tables  = Object.FindObjectsByType<Table>(FindObjectsSortMode.None);
        var devices = Object.FindObjectsByType<NetworkDevice>(FindObjectsSortMode.None)
            .OrderBy(DevicePriority)
            .ToArray();
        var cables  = Object.FindObjectsByType<Cable>(FindObjectsSortMode.None);

        prepared = null;
        if (tables.Length == 0 && devices.Length == 0 && cables.Length == 0) return;

        prepared = new GameObject("LevelBuildInEffectRunner").AddComponent<Runner>();
        prepared.Hide(tables, devices, cables);
    }

    /// <summary>Builds in whatever Prepare() hid, starting now. onComplete fires once the
    /// last piece has landed — immediately if there was nothing to build.</summary>
    public static void Play(System.Action onComplete)
    {
        var runner = prepared;
        prepared = null;
        if (runner == null) { onComplete?.Invoke(); return; }
        runner.Play(onComplete);
    }

    /// <summary>Reverse of the build-in — see class doc. onComplete fires once every piece has finished shrinking/hiding (immediately, if there was nothing to reverse).</summary>
    public static void PlayExitForScene(System.Action onComplete)
    {
        var tables  = Object.FindObjectsByType<Table>(FindObjectsSortMode.None);
        var devices = Object.FindObjectsByType<NetworkDevice>(FindObjectsSortMode.None)
            .OrderByDescending(DevicePriority)
            .ToArray();
        var cables  = Object.FindObjectsByType<Cable>(FindObjectsSortMode.None);

        if (tables.Length == 0 && devices.Length == 0 && cables.Length == 0) { onComplete?.Invoke(); return; }

        var items = devices.Select(d => d.transform).Concat(tables.Select(t => t.transform)).ToList();
        var runner = new GameObject("LevelBuildInEffectExitRunner").AddComponent<Runner>();
        runner.BeginExit(items, cables, onComplete);
    }

    /// <summary>Play's build-in for one table/device placed back mid-level (UndoManager),
    /// starting now. Any cables passed in are hidden immediately and pop in once item
    /// has landed — the same "devices first, cables after" order as the level
    /// entrance — then onComplete fires.</summary>
    public static void PlayForItem(Transform item, IEnumerable<Cable> cables = null, System.Action onComplete = null)
    {
        if (item == null) { onComplete?.Invoke(); return; }

        var waitingCables = new List<Cable>();
        if (cables != null)
            foreach (var cable in cables)
                if (cable != null) { HideCable(cable); waitingCables.Add(cable); }

        var runner = new GameObject("LevelBuildInEffectItemRunner").AddComponent<Runner>();
        runner.HideItem(item);
        runner.Play(() =>
        {
            foreach (var cable in waitingCables)
                if (cable != null) cable.RevealProgress = 1f;
            onComplete?.Invoke();
        });
    }

    /// <summary>PlayExitForScene's shrink for just these devices/tables (UndoManager
    /// removing something mid-level) — same PC > Switch > Router > table order.
    /// Call right before removing them: each is copied into a render-only stand-in
    /// that plays the shrink, so the real objects can still be removed/destroyed
    /// straight away (events, autosave, cable cleanup all exactly as immediate as
    /// without the animation).</summary>
    public static void PlayExitForItems(IEnumerable<NetworkDevice> devices, IEnumerable<Table> tables = null)
    {
        var sources = new List<Transform>();
        if (devices != null)
            sources.AddRange(devices.Where(d => d != null).OrderByDescending(DevicePriority).Select(d => d.transform));
        if (tables != null)
            sources.AddRange(tables.Where(t => t != null).Select(t => t.transform));
        if (sources.Count == 0) return;

        var runner = new GameObject("LevelBuildInEffectItemExitRunner").AddComponent<Runner>();
        runner.BeginExit(sources.Select(runner.AddStandIn).ToList(), null, null);
    }

    // Undrawn, and never a blue blueprint once it does appear — see PopInCable.
    static void HideCable(Cable cable)
    {
        cable.RevealProgress   = 0f;
        cable.SolidifyProgress = 1f;
    }

    // Router > Switch > PC: routers are the backbone a level's other devices
    // plug into, so they read as "built first" rather than growing in in
    // whatever arbitrary order FindObjectsByType happened to return. Reused
    // in reverse (OrderByDescending) for PlayExitForScene.
    static int DevicePriority(NetworkDevice d) => d.deviceType switch
    {
        DeviceType.Router => 0,
        DeviceType.Switch => 1,
        DeviceType.PC     => 2,
        _                 => 3,
    };

    // Bare coroutine host that self-destroys once every item's own animation
    // finishes — mirrors ConstructionDust/ConstructionRevealEffect's own
    // "component added, does its one-shot job, destroys itself" lifecycle.
    class Runner : MonoBehaviour
    {
        // Tables then devices, in build order, each with the scale it
        // should grow back to — captured by Hide() before snapping it away.
        readonly List<(Transform t, Vector3 restScale)> growItems = new List<(Transform, Vector3)>();
        Cable[] hiddenCables = new Cable[0];

        int pending;
        System.Action onDone;

        public void Hide(Table[] tables, NetworkDevice[] devices, Cable[] cables)
        {
            foreach (var table in tables)   if (table != null)  HideItem(table.transform);
            foreach (var device in devices) if (device != null) HideItem(device.transform);

            hiddenCables = cables;
            foreach (var cable in cables)
                if (cable != null) HideCable(cable);
        }

        public void HideItem(Transform t)
        {
            growItems.Add((t, t.localScale));
            t.localScale = Vector3.one * MinGrowScale;
        }

        // Everything is already hidden (see Hide), so only each item's wait
        // before it starts animating/popping in is staggered here, via each
        // coroutine's own delay argument.
        public void Play(System.Action onComplete)
        {
            onDone  = onComplete;
            pending = growItems.Count + hiddenCables.Length;
            if (pending == 0) { Finish(); return; }

            int i = 0;
            foreach (var (t, restScale) in growItems)
                StartCoroutine(GrowIn(t, restScale, i++ * StaggerDelay));
            foreach (var cable in hiddenCables)
                StartCoroutine(PopInCable(cable, i++ * StaggerDelay));
        }

        // items already in shrink order (devices by reverse priority, then tables).
        public void BeginExit(IReadOnlyList<Transform> items, IEnumerable<Cable> cables, System.Action onComplete)
        {
            onDone  = onComplete;
            pending = items.Count;

            // Cables vanish immediately, before anything else starts
            // shrinking — the exact reverse of them being the last thing to
            // pop in, and (like the entrance) no animation of their own.
            if (cables != null)
                foreach (var cable in cables)
                    if (cable != null) cable.RevealProgress = 0f;

            if (pending == 0) { Finish(); return; }

            float stagger = Mathf.Min(ExitStaggerDelay, ExitMaxSpread / Mathf.Max(1, pending - 1));
            for (int i = 0; i < items.Count; i++)
                StartCoroutine(ShrinkOut(items[i], i * stagger));
        }

        // Clones made for AddStandIn — freed with the runner (normally at
        // Finish, or with the scene if it unloads mid-shrink).
        readonly List<Object> ownedClones = new List<Object>();

        void OnDestroy()
        {
            foreach (var clone in ownedClones)
                if (clone != null) Destroy(clone);
        }

        // Render-only copy of source (every visible mesh at its current world
        // pose, nothing else — no NetworkDevice/Table/collider for anything to
        // find or hit), parented under this runner so Finish's own Destroy
        // takes it along. Its root sits on source's own pivot, so it shrinks
        // toward exactly the point the real item would have.
        //
        // Materials, and any readable texture on them, are cloned rather than
        // shared: some of a device's own components free theirs in OnDestroy
        // (PCMonitor's screen material and texture), which runs at the end of
        // this frame while the copy still has its whole shrink to play.
        public Transform AddStandIn(Transform source)
        {
            var root = new GameObject(source.name + " (removed)").transform;
            root.SetParent(transform, false);
            root.SetPositionAndRotation(source.position, source.rotation);
            root.localScale = source.lossyScale;

            var materialClones = new Dictionary<Material, Material>();
            var block = new MaterialPropertyBlock();

            foreach (var r in source.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                var part = new GameObject(r.name) { layer = r.gameObject.layer };
                part.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                part.transform.localScale = r.transform.lossyScale;
                part.transform.SetParent(root, true);

                part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var copy = part.AddComponent<MeshRenderer>();
                copy.shadowCastingMode = r.shadowCastingMode;
                copy.receiveShadows    = r.receiveShadows;

                var src  = r.sharedMaterials;
                var mats = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                    mats[i] = src[i] == null ? null : CloneMaterial(src[i], materialClones);
                copy.sharedMaterials = mats;

                if (r.HasPropertyBlock())
                {
                    r.GetPropertyBlock(block);
                    copy.SetPropertyBlock(block);
                }
            }
            return root;
        }

        Material CloneMaterial(Material src, Dictionary<Material, Material> clones)
        {
            if (clones.TryGetValue(src, out var existing)) return existing;

            var clone = new Material(src);
            ownedClones.Add(clone);
            foreach (int id in clone.GetTexturePropertyNameIDs())
            {
                if (clone.GetTexture(id) is Texture2D tex && tex.isReadable)
                {
                    var texClone = Instantiate(tex);
                    ownedClones.Add(texClone);
                    clone.SetTexture(id, texClone);
                }
            }
            clones[src] = clone;
            return clone;
        }

        IEnumerator GrowIn(Transform t, Vector3 restScale, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, delay));

            float elapsed = 0f;
            while (elapsed < GrowDuration && t != null)
            {
                elapsed += AnimationSpeed.Delta(AnimationKind.Level);
                float eased = EaseOutQuad(Mathf.Clamp01(elapsed / GrowDuration));
                t.localScale = Vector3.LerpUnclamped(Vector3.one * MinGrowScale, restScale, eased);
                yield return null;
            }

            // Null-checked throughout (here and in the exit's ShrinkOut) —
            // an item destroyed mid-animation still has to count toward
            // pending, or the whole sequence would wait on it forever.
            if (t != null)
            {
                t.localScale = restScale;
                ConstructionDust.EmitBurst(t.position, FootprintRadius(t));
            }
            ItemDone();
        }

        // No gradual reveal — SolidifyProgress pinned at 1 the whole time (a
        // cable is never a blue blueprint here), and RevealProgress jumps
        // straight from 0 to 1 the instant this cable's turn comes up.
        IEnumerator PopInCable(Cable cable, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, delay));

            if (cable != null) cable.RevealProgress = 1f;
            ItemDone();
        }

        IEnumerator ShrinkOut(Transform t, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, delay));

            if (t != null)
            {
                // Dust the instant the shrink starts, not when it finishes —
                // mirrors GameBootstrap's own exit convention (a piece bursts
                // the moment it stops being solid, not once it's already gone).
                ConstructionDust.EmitBurst(t.position, FootprintRadius(t));

                Vector3 restScale = t.localScale;
                float elapsed = 0f;
                while (elapsed < ShrinkDuration && t != null)
                {
                    elapsed += AnimationSpeed.Delta(AnimationKind.Level);
                    float eased = EaseOutQuad(Mathf.Clamp01(elapsed / ShrinkDuration));
                    t.localScale = Vector3.LerpUnclamped(restScale, Vector3.one * MinGrowScale, eased);
                    yield return null;
                }
                if (t != null) t.localScale = Vector3.one * MinGrowScale;
            }
            ItemDone();
        }

        void ItemDone()
        {
            if (--pending <= 0) Finish();
        }

        void Finish()
        {
            var callback = onDone;
            onDone = null;
            Destroy(gameObject);
            callback?.Invoke();
        }

        // Horizontal-only (X/Z) extent of everything rendered under t — a
        // device can be tall, but the dust puff spreads across the ground
        // around its footprint, not up through it (mirrors GameBootstrap's
        // own wall-dust radius derivation).
        static float FootprintRadius(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 1f;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return Mathf.Max(b.extents.x, b.extents.z);
        }

        static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
    }
}
