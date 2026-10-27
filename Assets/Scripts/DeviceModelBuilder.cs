using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds simple procedural device GameObjects from Unity primitives.
/// Call Build*() to get a fully configured prefab-like root object.
/// </summary>
public static class DeviceModelBuilder
{
    // Cached shared materials per device type
    static Material s_pcBodyMat, s_pcScreenMat, s_pcChassisMat;
    static Material s_switchBodyMat, s_switchLedMat;
    static Material s_routerBodyMat, s_routerChassisMat;
    static Material s_tableTopMat, s_tableLegMat;
    static Material s_portMat;
    static Material s_portArrowMat;
    // One flat arrow mesh shared by every device — CreatePortSideArrow() just
    // gives each instance its own transform (position + yaw).
    static Mesh s_portArrowMesh;

    // World-space height of the table's top surface; also where table-requiring
    // devices physically come to rest when dropped/placed on top of one.
    public const float TableHeight = 0.62f;

    // Exposed so TableManager's placement hologram can mirror the exact same
    // slab+legs geometry instead of duplicating these numbers.
    public const float TableTopThickness = 0.06f;
    public const float TableLegThickness = 0.08f;
    public const float TableLegInset     = 0.10f; // distance from footprint edge to leg centre

    // PS1-era look: a tiny, point-filtered noise texture stretched once across
    // each face (no tiling) reads as a handful of big, visible pixel blocks
    // instead of a smooth sculpted material — this is the same trick original
    // PS1 games used (very low native texture resolution mapped straight
    // across a face). Kept small enough that at normal camera distance the
    // blocks are clearly visible rather than dissolving into fine grain.
    public const int   PixelTexSize      = 16;
    public const float PixelTexVariation = 0.09f; // +/- per-pixel lightness offset

    // Lazily (re)creates the cached materials rather than a static constructor
    // — the Level Template Editor tool (LevelTemplateAuthoringWindow) calls
    // BuildTable()/Build*Procedural() right after Play Mode exits, and with
    // "Reload Domain" disabled in Enter Play Mode Settings these static
    // fields survive that exit still pointing at Material instances that were
    // themselves created *during* Play Mode — which Unity destroys the
    // instant Play Mode stops (same reason the whole capture/bake dance
    // exists — see LevelTemplateAuthoringWindow's doc comment). Assigning a
    // destroyed-but-not-null C# reference to a Renderer.sharedMaterial
    // serializes as a missing material (pink) and NREs the first thing that
    // reads it (e.g. TableManager.ApplyHighlightTint), so every Build* entry
    // point calls this first — `== null` also catches Unity's "fake null" on
    // a destroyed object, so a stale reference gets rebuilt instead of reused.
    static void EnsureMaterials()
    {
        if (s_tableTopMat != null) return;

        // PC/Switch/Router get the pixelated PS1-style body texture (see
        // MakeTexturedLitMaterial) — texture is opt-in per material, not global.
        s_pcBodyMat     = MakeTexturedLitMaterial(new Color(0.82f, 0.82f, 0.86f), 101);
        s_pcChassisMat  = MakeTexturedLitMaterial(new Color(0.10f, 0.10f, 0.10f), 102);
        s_pcScreenMat   = MakeLitMaterial(new Color(0.10f, 0.28f, 0.78f));
        s_switchBodyMat = MakeTexturedLitMaterial(new Color(0.55f, 0.55f, 0.58f), 201);
        s_switchLedMat  = MakeLitMaterial(new Color(0.10f, 0.85f, 0.15f));
        s_routerBodyMat = MakeTexturedLitMaterial(new Color(0.92f, 0.92f, 0.94f), 301);
        s_routerChassisMat = MakeTexturedLitMaterial(new Color(0.10f, 0.10f, 0.10f), 302);
        s_tableTopMat   = MakeTexturedLitMaterial(new Color(0.04f, 0.04f, 0.05f), 401);
        s_tableLegMat   = MakeTexturedLitMaterial(new Color(0.02f, 0.02f, 0.03f), 402);
        // A distinct cyan/teal instead of blending into the same s_darkMat as
        // the rest of the chassis trim — ports are the one part of a device a
        // player actually needs to pick out visually (see NetworkDevice.
        // HighlightFreePorts, driven by CableManager while the interface
        // picker is open), so they read as their own "thing" at a glance even
        // before any highlight kicks in.
        s_portMat       = MakeTexturedLitMaterial(new Color(0.25f, 0.78f, 0.9f), 403);
        s_portArrowMat  = MakeUnlitDoubleSidedMaterial(new Color(0.15f, 0.95f, 0.25f));
    }

    static Material MakeLitMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0f);
        return mat;
    }

    // Same as MakeLitMaterial but with a small pixelated noise texture baked
    // in (see PixelTexSize doc comment above) — used by the runtime fallback
    // path only; DevicePrefabBuilder's editor tool bakes the equivalent
    // texture into a real persistent asset for the prefab-backed path so it
    // survives domain reloads/rebuilds instead of being regenerated in memory
    // every time.
    static Material MakeTexturedLitMaterial(Color color, int seed)
    {
        var mat = MakeLitMaterial(color);
        Texture2D tex = CreatePixelNoiseTexture(PixelTexSize, color, PixelTexVariation, seed);
        ApplyPixelTexture(mat, tex);
        return mat;
    }

    // Same fake-shading trick Cable.GetCableTexture bakes across a cable's
    // width (brighter middle, darker edges, reading as a lit cylinder out of
    // a flat ribbon) — applied here as a top-to-bottom gradient across each
    // face instead, so a flat primitive cuboid reads as gently lit from above
    // rather than uniformly flat. Combined with the existing deterministic
    // (seeded, System.Random — doesn't touch UnityEngine.Random's global
    // state, which other systems rely on) per-pixel lightness noise. Point-
    // filtered + no mip chain so it stays crisp, blocky pixels at any
    // distance instead of blurring into smooth grain — that crunchy, low-res
    // look is the whole point (PS1-style texturing).
    const float ShadeBottom = 0.82f;
    const float ShadeTop    = 1.15f;

    public static Texture2D CreatePixelNoiseTexture(int size, Color baseColor, float variation, int seed)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name       = "PixelNoise",
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Repeat,
        };

        var rng = new System.Random(seed);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float v     = (y + 0.5f) / size; // 0 at the bottom of the face, 1 at the top
            float shade = Mathf.Lerp(ShadeBottom, ShadeTop, v);
            for (int x = 0; x < size; x++)
            {
                float n = (float)(rng.NextDouble() * 2.0 - 1.0) * variation;
                pixels[y * size + x] = new Color(
                    Mathf.Clamp01(baseColor.r * shade + n),
                    Mathf.Clamp01(baseColor.g * shade + n),
                    Mathf.Clamp01(baseColor.b * shade + n),
                    1f);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    // No tiling (scale stays 1,1) — the low-res texture is meant to stretch
    // once across each face, not repeat, so it reads as chunky visible pixels
    // sized to the part rather than fine repeating grain.
    public static void ApplyPixelTexture(Material mat, Texture2D tex)
    {
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
    }

    // Unlit + double-sided (no backface culling) so the flat port-direction
    // arrow reads as a solid color from directly above (top-down placement
    // view) regardless of which way the mesh's face winding happens to point.
    static Material MakeUnlitDoubleSidedMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        return mat;
    }

    // Flat shaft+triangle-head arrow lying in the local XZ plane, pointing
    // toward local +Z. CreatePortSideArrow() aims +Z at the device's own
    // PortSideLocalDirection via transform rotation, so every device reuses
    // this one mesh. Sized to read clearly against a whole device rather than
    // a single port slot.
    static Mesh BuildPortArrowMesh()
    {
        const float shaftLen = 0.16f, shaftWidth = 0.09f, headLen = 0.12f, headWidth = 0.22f;
        var verts = new[]
        {
            new Vector3(-shaftWidth * 0.5f, 0f, 0f),
            new Vector3( shaftWidth * 0.5f, 0f, 0f),
            new Vector3( shaftWidth * 0.5f, 0f, shaftLen),
            new Vector3(-shaftWidth * 0.5f, 0f, shaftLen),
            new Vector3(-headWidth  * 0.5f, 0f, shaftLen),
            new Vector3( headWidth  * 0.5f, 0f, shaftLen),
            new Vector3(0f, 0f, shaftLen + headLen),
        };
        var tris = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6 };

        var mesh = new Mesh { name = "PortSideArrow" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Extra outward gap (beyond the port cluster's own position) the arrow is
    // pushed out by, so it clearly floats outside the device body instead of
    // overlapping it.
    const float PortArrowStandoff = 0.15f;

    // Spawns the single port-side direction arrow for a device's drag
    // hologram (see DragDropManager.ApplyHologram/RestoreFromHologram) —
    // not part of the permanent built model, since it's only useful while
    // orienting a device before it's placed. Parented under the device's own
    // visualRoot so it moves/rotates with the ghost for free.
    public static GameObject CreatePortSideArrow(NetworkDevice device, Transform parent)
    {
        if (s_portArrowMesh == null) s_portArrowMesh = BuildPortArrowMesh(); // see EnsureMaterials — `??=` misses a "fake null" destroyed Mesh

        var go = new GameObject("PortSideArrow");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = device.PortSideLocalOffset + device.PortSideLocalDirection.normalized * PortArrowStandoff;
        go.transform.localRotation = Quaternion.LookRotation(device.PortSideLocalDirection, Vector3.up);

        go.AddComponent<MeshFilter>().sharedMesh = s_portArrowMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = s_portArrowMat;
        // Deliberately no Collider: WouldOverlapDevice() (DragDropManager) only
        // ever tests the root's own BoxCollider against Physics.OverlapBox, and
        // this arrow has no collider at all to be picked up by that or any other
        // physics/raycast query — so it never counts as space the device takes up.
        return go;
    }

    // Returns the created part's GameObject (most callers ignore it — only the
    // port-slot parts capture it, so NetworkDevice can highlight the right
    // one later; see NetworkDevice.SetPortVisual / HighlightFreePorts).
    // name is optional and only needed by parts something else has to find
    // again by name later (see the PC screen below, matched against
    // DevicePrefabBuilder's own "PC_Screen" so PingWipeEffect can find it
    // regardless of which of the two build paths actually ran) — every other
    // part is left with CreatePrimitive's generic default name, same as before.
    static GameObject Part(PrimitiveType shape, Vector3 pos, Vector3 scale, Material mat, Transform parent, string name = null)
    {
        var go = GameObject.CreatePrimitive(shape);
        if (name != null) go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        // Only the root keeps a collider; remove from children. DestroyImmediate
        // outside Play Mode — the level-template Editor tool calls Build*() too,
        // and plain Destroy() is a no-op (with a console error) in edit mode.
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }
        return go;
    }

    // Every visual Part() for a device parents here instead of directly under
    // root, so PacketDeviceEffect can scale just the model (packet-flow bounce)
    // without dragging the root's collider/Rigidbody/port offsets along with it.
    static Transform AddVisualRoot(GameObject root, NetworkDevice nd)
    {
        var go = new GameObject("Visual");
        go.transform.SetParent(root.transform, false);
        nd.visualRoot = go.transform;
        return go.transform;
    }

    static Rigidbody AddPhysics(GameObject root)
    {
        var rb = root.AddComponent<Rigidbody>();
        rb.mass            = 1f;
        rb.linearDamping   = 3f;
        rb.angularDamping  = 10f;
        rb.constraints     = RigidbodyConstraints.FreezeRotation;
        rb.isKinematic     = true; // kinematic until dropped
        return rb;
    }

    // Devices are normally sourced from the .prefab assets that
    // DevicePrefabBuilder (Editor-only, Tools > Capstone > Build Device
    // Prefabs (ProBuilder)) bakes into Assets/Resources/DevicePrefabs/ — real
    // ProBuilder-authored meshes/materials, not primitives built in code. The
    // Build*() methods below fall back to the original primitive-based
    // construction only if that prefab hasn't been generated yet, so the
    // project still runs before anyone's opened the Editor tool.
    static GameObject LoadDevicePrefab(string name)
    {
        var prefab = Resources.Load<GameObject>($"DevicePrefabs/{name}");
        return prefab != null ? Object.Instantiate(prefab) : null;
    }

    // ── PC ──────────────────────────────────────────────────────────────────
    // PCMonitor is added here rather than baked into the prefab so both build
    // paths get it, and so it lands before BuildTemplate deactivates the PC —
    // a DragDropManager clone then already carries the screen quad when its
    // hologram material swap runs.
    public static GameObject BuildPC()
    {
        GameObject pc = LoadDevicePrefab("PC") ?? BuildPCProcedural();
        PCMonitor.Attach(pc);
        return pc;
    }

    static GameObject BuildPCProcedural()
    {
        EnsureMaterials();
        var root = new GameObject("PC");
        var nd = root.AddComponent<NetworkDevice>();
        nd.deviceType = DeviceType.PC;
        nd.deviceName = "PC";
        nd.requiresTable = true;

        Transform visual = AddVisualRoot(root, nd);

        // Tower — named so DeviceAddress can scope its subnet tint to just this
        // part (the "main PC"), leaving the monitor/keyboard their own color;
        // matches PBCube("PC_Tower", ...) on the prefab-baked build path.
        Part(PrimitiveType.Cube, new Vector3(-0.38f, 0.30f, 0f),
             new Vector3(0.28f, 0.60f, 0.24f), s_pcChassisMat, visual, "PC_Tower");

        // Monitor stand foot (flat base on the desk)
        Part(PrimitiveType.Cube, new Vector3(0.12f, 0.015f, 0f),
             new Vector3(0.28f, 0.03f, 0.16f), s_pcChassisMat, visual);

        // Monitor stand neck (connects foot up to the bezel's bottom edge)
        Part(PrimitiveType.Cube, new Vector3(0.12f, 0.11f, 0f),
             new Vector3(0.05f, 0.16f, 0.05f), s_pcChassisMat, visual);

        // Monitor bezel
        Part(PrimitiveType.Cube, new Vector3(0.12f, 0.47f, 0f),
             new Vector3(0.72f, 0.56f, 0.06f), s_pcChassisMat, visual);

        // Screen (faces -z, toward the keyboard/user side)
        Part(PrimitiveType.Cube, new Vector3(0.12f, 0.47f, -0.04f),
             new Vector3(0.60f, 0.44f, 0.02f), s_pcScreenMat, visual, "PC_Screen");

        // Keyboard base (in front of the monitor, on the screen-facing side)
        Part(PrimitiveType.Cube, new Vector3(0.12f, 0.02f, -0.22f),
             new Vector3(0.55f, 0.04f, 0.20f), s_pcBodyMat, visual);

        // Port slot (back of tower)
        Vector3[] pcPorts = { new Vector3(-0.38f, 0.20f, 0.12f) };
        GameObject pcPortGo = Part(PrimitiveType.Cube, pcPorts[0], new Vector3(0.06f, 0.05f, 0.02f), s_portMat, visual);
        nd.InitPorts(pcPorts);
        nd.SetPortVisual(0, pcPortGo);
        nd.SetPortSideIndicator(pcPorts[0], Vector3.forward); // +Z — outward, away from the tower

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.35f, 0f);
        col.size   = new Vector3(1.0f, 0.75f, 0.5f);

        AddPhysics(root);
        return root;
    }

    // ── Switch ───────────────────────────────────────────────────────────────
    public static GameObject BuildSwitch() => LoadDevicePrefab("Switch") ?? BuildSwitchProcedural();

    static GameObject BuildSwitchProcedural()
    {
        EnsureMaterials();
        var root = new GameObject("Switch");
        var nd = root.AddComponent<NetworkDevice>();
        nd.deviceType = DeviceType.Switch;
        nd.deviceName = "Switch";
        nd.requiresTable = true;

        Transform visual = AddVisualRoot(root, nd);

        // Body — named to match PBCube("Switch_Body", ...) on the prefab-baked
        // build path (see SubnetVisualizer's switch-tint scoping).
        Part(PrimitiveType.Cube, new Vector3(0f, 0.09f, 0f),
             new Vector3(1.35f, 0.18f, 0.63f), s_switchBodyMat, visual, "Switch_Body");

        // LED strip
        Part(PrimitiveType.Cube, new Vector3(0f, 0.20f, 0f),
             new Vector3(1.26f, 0.018f, 0.09f), s_switchLedMat, visual);

        // Port slots (front face)
        Vector3[] switchPorts = new Vector3[6];
        GameObject[] switchPortGos = new GameObject[6];
        Vector3 portCenter = Vector3.zero;
        for (int i = 0; i < 6; i++)
        {
            float x = -0.495f + i * 0.198f;
            switchPorts[i] = new Vector3(x, 0.09f, -0.324f);
            portCenter += switchPorts[i];
            switchPortGos[i] = Part(PrimitiveType.Cube, switchPorts[i],
                 new Vector3(0.072f, 0.054f, 0.018f), s_portMat, visual);
        }
        nd.InitPorts(switchPorts);
        for (int i = 0; i < switchPortGos.Length; i++) nd.SetPortVisual(i, switchPortGos[i]);
        nd.SetPortSideIndicator(portCenter / switchPorts.Length, Vector3.back); // -Z — outward, out the front face

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.09f, 0f);
        col.size   = new Vector3(1.35f, 0.225f, 0.63f);

        AddPhysics(root);
        return root;
    }

    // ── Router ───────────────────────────────────────────────────────────────
    public static GameObject BuildRouter()
    {
        var go = LoadDevicePrefab("Router") ?? BuildRouterProcedural();

        // Every router carries its own RouterInterfaceConfig regardless of which
        // path built it — RouterCLI reads/writes through this, and
        // SceneStateCapture/the level-template Editor tool need it present to
        // save/restore per-interface IPs (see RouterInterfaceConfig's doc comment).
        var cfg = go.GetComponent<RouterInterfaceConfig>() ?? go.AddComponent<RouterInterfaceConfig>();
        cfg.EnsureSize(go.GetComponent<NetworkDevice>().PortCount);

        return go;
    }

    static GameObject BuildRouterProcedural()
    {
        EnsureMaterials();
        var root = new GameObject("Router");
        var nd = root.AddComponent<NetworkDevice>();
        nd.deviceType = DeviceType.Router;
        nd.deviceName = "Router";
        nd.requiresTable = true;

        Transform visual = AddVisualRoot(root, nd);

        // Body
        Part(PrimitiveType.Cube, new Vector3(0f, 0.14f, 0f),
             new Vector3(0.65f, 0.25f, 0.46f), s_routerBodyMat, visual);

        // Top panel
        Part(PrimitiveType.Cube, new Vector3(0f, 0.265f, 0f),
             new Vector3(0.60f, 0.015f, 0.43f), s_routerChassisMat, visual);

        // Antennas
        Part(PrimitiveType.Cylinder, new Vector3(-0.18f, 0.53f, 0f),
             new Vector3(0.03f, 0.28f, 0.03f), s_routerChassisMat, visual);
        Part(PrimitiveType.Cylinder, new Vector3(0.18f, 0.53f, 0f),
             new Vector3(0.03f, 0.28f, 0.03f), s_routerChassisMat, visual);

        // Port slots (front)
        Vector3[] routerPorts = new Vector3[3];
        GameObject[] routerPortGos = new GameObject[3];
        Vector3 routerPortCenter = Vector3.zero;
        for (int i = 0; i < 3; i++)
        {
            float x = -0.15f + i * 0.15f;
            routerPorts[i] = new Vector3(x, 0.13f, -0.24f);
            routerPortCenter += routerPorts[i];
            routerPortGos[i] = Part(PrimitiveType.Cube, routerPorts[i],
                 new Vector3(0.06f, 0.045f, 0.015f), s_portMat, visual);
        }
        nd.InitPorts(routerPorts);
        for (int i = 0; i < routerPortGos.Length; i++) nd.SetPortVisual(i, routerPortGos[i]);
        nd.SetPortSideIndicator(routerPortCenter / routerPorts.Length, Vector3.back); // -Z — outward, out the front face

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.28f, 0f);
        col.size   = new Vector3(0.64f, 0.62f, 0.47f);

        AddPhysics(root);
        return root;
    }

    // ── Table ────────────────────────────────────────────────────────────────
    // Built directly at its final world position by TableManager — no drag/physics
    // step, since the two placement clicks already fix both position and footprint.
    // The footprint can still change afterward (TableManager's resize handles) —
    // LayoutTable is the one place it's applied, at build time and on resize.
    public static GameObject BuildTable(float width, float length)
    {
        EnsureMaterials();
        var root = new GameObject("Table");
        var table = root.AddComponent<Table>();
        table.topSurfaceY = TableHeight + TableTopThickness * 0.5f; // exposed so Cable can route above it

        // Placed and sized by LayoutTable below. Top first, then the legs —
        // LayoutTable finds them in that order.
        Part(PrimitiveType.Cube, Vector3.zero, Vector3.one, s_tableTopMat, root.transform, "TableTop");
        for (int i = 0; i < 4; i++)
            Part(PrimitiveType.Cube, Vector3.zero, Vector3.one, s_tableLegMat, root.transform, "TableLeg");

        // Solid from the ground up to the tabletop surface, not just the thin
        // top slab — legs are visual-only (their per-part colliders are
        // destroyed in Part()), so a slab-only collider would let anything
        // pass through the open space between them.
        root.AddComponent<BoxCollider>();

        LayoutTable(table, width, length);
        return root;
    }

    /// <summary>Re-fits a placed table's top, legs and collider to a new
    /// footprint around its own pivot (TableManager's resize handles, and
    /// undoing a resize). The caller moves the table to the new center.</summary>
    public static void ResizeTable(Table table, float width, float length)
    {
        if (table != null) LayoutTable(table, width, length);
    }

    static void LayoutTable(Table table, float width, float length)
    {
        table.width  = width;
        table.length = length;

        // The top is the first mesh child and the legs the next four — the
        // order BuildTable adds them in. Found by order, not name: a table
        // baked into a level template before the parts were named has them
        // all as plain "Cube"s.
        Transform top = null;
        var legs = new List<Transform>(4);
        foreach (Transform child in table.transform)
        {
            if (child.GetComponent<MeshRenderer>() == null) continue;
            if (top == null) top = child;
            else if (legs.Count < 4) legs.Add(child);
        }

        const float topThickness = TableTopThickness;
        const float legThickness = TableLegThickness;
        const float legInset     = TableLegInset;

        if (top != null)
        {
            top.localPosition = new Vector3(0f, TableHeight, 0f);
            top.localScale    = new Vector3(width, topThickness, length);
        }

        float legHeight = TableHeight - topThickness * 0.5f;
        float hx = Mathf.Max(width * 0.5f - legInset, legThickness);
        float hz = Mathf.Max(length * 0.5f - legInset, legThickness);
        Vector3[] legOffsets =
        {
            new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz),
            new Vector3(-hx, 0f,  hz), new Vector3(hx, 0f,  hz),
        };
        for (int i = 0; i < legs.Count; i++)
        {
            legs[i].localPosition = new Vector3(legOffsets[i].x, legHeight * 0.5f, legOffsets[i].z);
            legs[i].localScale    = new Vector3(legThickness, legHeight, legThickness);
        }

        var col = table.GetComponent<BoxCollider>();
        if (col != null)
        {
            col.center = new Vector3(0f, table.topSurfaceY * 0.5f, 0f);
            col.size   = new Vector3(width, table.topSurfaceY, length);
        }
    }

    // Returns an inactive template (palette uses copies of these)
    public static GameObject BuildTemplate(DeviceType type)
    {
        GameObject t = type switch
        {
            DeviceType.PC     => BuildPC(),
            DeviceType.Switch => BuildSwitch(),
            DeviceType.Router => BuildRouter(),
            _                 => BuildPC()
        };
        t.SetActive(false);
        return t;
    }
}
