using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;

/// <summary>
/// One-time (re-runnable) Editor tool that authors the PC/Switch/Router models
/// as ProBuilder geometry and bakes them into real .prefab assets — this is
/// Unity's own in-editor modeling tool doing the job an external DCC app
/// (e.g. Blender) would otherwise do, so the resulting meshes are as much
/// "hand-authored assets" as anything imported from one.
///
/// Mirrors the exact part positions/sizes that DeviceModelBuilder's
/// BuildPC/BuildSwitch/BuildRouter used to construct procedurally from
/// GameObject.CreatePrimitive() at runtime, so the finished look doesn't
/// change — only where the geometry comes from. Each part's ProBuilderMesh
/// is finalized (ToMesh/Refresh), baked into a persistent Mesh
/// asset, then the ProBuilderMesh component is stripped so play-mode
/// instances are plain MeshFilter/MeshRenderer with no ProBuilder runtime
/// dependency.
///
/// Run via Tools > Capstone > Build Device Prefabs (ProBuilder). Output:
///   Assets/Resources/DevicePrefabs/{PC,Switch,Router}.prefab  (loaded by
///     DeviceModelBuilder at runtime via Resources.Load)
///   Assets/Meshes/Devices/*.asset                             (baked parts)
///   Assets/Materials/Devices/*.mat                            (shared materials;
///     left alone on re-runs so hand-tuned colors survive regeneration)
///
/// The gameplay Table is intentionally NOT converted by this tool — its
/// footprint (width/length) is chosen per-instance by the player when
/// placing it, so its geometry can't be pre-baked into a single fixed-size
/// prefab without re-scaling it at runtime anyway, which would defeat the
/// point. It stays procedural in DeviceModelBuilder.BuildTable().
///
/// Separately, BuildTableProp() (menu: Tools > Capstone > Build Table Prop
/// (Decoration)) bakes a fixed-size, non-functional Table look-alike purely
/// for hand-placed level/background dressing — no NetworkDevice/Table
/// component, not loaded via Resources at runtime. Output:
///   Assets/Prefabs/TableProp.prefab
///   Assets/Meshes/Props/*.asset
/// </summary>
public static class DevicePrefabBuilder
{
    const string PrefabFolder = "Assets/Resources/DevicePrefabs";
    const string MeshFolder   = "Assets/Meshes/Devices";
    const string MatFolder    = "Assets/Materials/Devices";
    const string TexFolder    = "Assets/Textures/Devices";

    // Separate output for TableProp — a purely decorative background/level-design
    // object, not a gameplay device, so it doesn't belong under DevicePrefabs
    // (which DeviceModelBuilder Resources.Loads at runtime for actual devices).
    const string PropPrefabFolder = "Assets/Prefabs";
    const string PropMeshFolder   = "Assets/Meshes/Props";

    class Mats
    {
        public Material pcBody, pcChassis, pcScreen, switchBody, switchLed, routerBody, routerChassis, port;
        public Material tableTop, tableLeg;
    }

    static int s_meshCounter;

    [MenuItem("Tools/Capstone/Build Device Prefabs (ProBuilder)")]
    public static void BuildAll()
    {
        // Wipe and rebuild the generated folders so re-running this tool after
        // a geometry tweak doesn't leave stale/orphaned parts or prefabs behind.
        // Materials live outside this wipe (see MakeMat) so hand-tuned colors
        // aren't stomped on every regenerate.
        if (AssetDatabase.IsValidFolder(MeshFolder))   AssetDatabase.DeleteAsset(MeshFolder);
        if (AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.DeleteAsset(PrefabFolder);
        EnsureFolder(PrefabFolder);
        EnsureFolder(MeshFolder);
        EnsureFolder(MatFolder);
        EnsureFolder(TexFolder);
        s_meshCounter = 0;

        var m = BuildMaterials();

        SaveDevicePrefab("PC",     BuildPC(m));
        SaveDevicePrefab("Switch", BuildSwitch(m));
        SaveDevicePrefab("Router", BuildRouter(m));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[DevicePrefabBuilder] Generated device prefabs into {PrefabFolder} " +
                  $"(meshes in {MeshFolder}, materials in {MatFolder}).");
    }

    // ── materials ────────────────────────────────────────────────────────────
    static Mats BuildMaterials() => new Mats
    {
        // PC/Switch/Router bodies (and PC/Router's own dark trim) get the
        // pixelated PS1-style texture baked in as a real persistent asset —
        // see MakeTexturedMat.
        pcBody        = MakeTexturedMat("PCBody",        new Color(0.82f, 0.82f, 0.86f), 101),
        pcChassis     = MakeTexturedMat("PCChassis",     new Color(0.10f, 0.10f, 0.10f), 102),
        pcScreen      = MakeMat("PCScreen",   new Color(0.10f, 0.28f, 0.78f)),
        switchBody    = MakeTexturedMat("SwitchBody",    new Color(0.55f, 0.55f, 0.58f), 201),
        switchLed     = MakeMat("SwitchLed",  new Color(0.10f, 0.85f, 0.15f)),
        routerBody    = MakeTexturedMat("RouterBody",    new Color(0.92f, 0.92f, 0.94f), 301),
        routerChassis = MakeTexturedMat("RouterChassis", new Color(0.10f, 0.10f, 0.10f), 302),
        port       = MakeTexturedMat("Port",       new Color(0.25f, 0.78f, 0.90f), 403),
        // Matches DeviceModelBuilder's s_tableTopMat/s_tableLegMat colors exactly
        // so TableProp reads as "the same table" as the in-game gameplay one.
        tableTop   = MakeTexturedMat("TableTop", new Color(0.04f, 0.04f, 0.05f), 401),
        tableLeg   = MakeTexturedMat("TableLeg", new Color(0.02f, 0.02f, 0.03f), 402),
    };

    static Material MakeMat(string name, Color color)
    {
        string path = $"{MatFolder}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // Same as MakeMat, but bakes in a small pixelated PS1-style noise texture
    // (DeviceModelBuilder.CreatePixelNoiseTexture — same helper the runtime
    // fallback path uses, so both paths render identically) as a real
    // persistent Texture2D asset, the same way FinalizePart bakes meshes.
    // Left alone on re-runs (existing-asset check, like MakeMat) so a
    // hand-tuned color/texture survives regenerating the prefabs.
    static Material MakeTexturedMat(string name, Color color, int seed)
    {
        string matPath = $"{MatFolder}/{name}.mat";
        var existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        string texPath = $"{TexFolder}/{name}Noise.asset";
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null)
        {
            tex = DeviceModelBuilder.CreatePixelNoiseTexture(
                DeviceModelBuilder.PixelTexSize, color, DeviceModelBuilder.PixelTexVariation, seed);
            AssetDatabase.CreateAsset(tex, texPath);
        }

        if (existingMat != null)
        {
            // Unlike MakeMat's plain existing-asset reuse, this can't just return
            // the material as-is: PCBody/SwitchBody/RouterBody already existed as
            // flat-color assets from before texturing was added, so "already
            // exists" would otherwise mean "stays flat forever" even across
            // reruns. Only patch in the texture if it's actually missing, so a
            // hand-tuned color on an already-textured material still survives.
            bool hasTex = (existingMat.HasProperty("_BaseMap") && existingMat.GetTexture("_BaseMap") != null)
                       || (existingMat.HasProperty("_MainTex") && existingMat.GetTexture("_MainTex") != null);
            if (!hasTex)
            {
                DeviceModelBuilder.ApplyPixelTexture(existingMat, tex);
                EditorUtility.SetDirty(existingMat);
            }
            return existingMat;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0f);
        DeviceModelBuilder.ApplyPixelTexture(mat, tex);
        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    // ── ProBuilder part helpers ─────────────────────────────────────────────
    static GameObject PBCube(string bakedName, Vector3 pos, Vector3 size, Material mat, Transform parent, string meshFolder = MeshFolder)
    {
        ProBuilderMesh pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        return FinalizePart(bakedName, pb, pos, mat, parent, meshFolder);
    }

    // size follows the same convention DeviceModelBuilder.Part() used to pass
    // as localScale on a built-in cylinder primitive (radius 0.5, height 2 at
    // scale 1), so callers can keep the exact numbers from the old code.
    static GameObject PBCylinder(string bakedName, Vector3 pos, Vector3 size, Material mat, Transform parent, string meshFolder = MeshFolder)
    {
        float radius = size.x * 0.5f;
        float height = size.y * 2f;
        ProBuilderMesh pb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 12, radius, height, 0, 1);
        return FinalizePart(bakedName, pb, pos, mat, parent, meshFolder);
    }

    static GameObject FinalizePart(string bakedName, ProBuilderMesh pb, Vector3 pos, Material mat, Transform parent, string meshFolder = MeshFolder)
    {
        pb.ToMesh();
        pb.Refresh();

        GameObject go = pb.gameObject;
        // Name the actual part GameObject, not just its baked Mesh asset below —
        // code that finds a specific part by name at runtime (PingWipeEffect's
        // "PC_Screen" scoping, DeviceAddress's "PC_Tower" tint scoping) needs
        // this to match on the prefab-baked build path the same way it already
        // did on the primitive-based runtime fallback (DeviceModelBuilder.Part()
        // sets the name directly), or the match silently finds nothing and each
        // falls back to its whole-model behavior instead.
        go.name = bakedName;
        var mf = go.GetComponent<MeshFilter>();
        var mr = go.GetComponent<MeshRenderer>();

        NormalizeUVsForPixelTexture(mf.sharedMesh);

        Mesh baked = Object.Instantiate(mf.sharedMesh);
        baked.name = $"{bakedName}_{s_meshCounter++}";
        AssetDatabase.CreateAsset(baked, $"{meshFolder}/{baked.name}.asset");

        Object.DestroyImmediate(pb); // strip the ProBuilder editing component
        mf.sharedMesh      = baked;
        mr.sharedMaterial  = mat;

        var col = go.GetComponent<Collider>(); // parts stay collider-free; only the device root has one
        if (col != null) Object.DestroyImmediate(col);

        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = Vector3.one;
        return go;
    }

    // ProBuilder's ShapeGenerator ties each face's auto-UV span directly to
    // the part's own physical size in world units (a 0.28-unit-wide face gets
    // a UV span of 0.28, a 1.35-unit one gets 1.35) — so on a small part the
    // pixel-noise texture (see MakeTexturedMat) only ever showed a tiny sliver
    // of itself (barely-visible, near-flat), while a large part over-tiled
    // into a busy repeating pattern, instead of the single clean low-res
    // stretch per face the runtime-fallback path gets for free (Unity's
    // built-in primitive UVs are always a fixed 0-1 regardless of transform
    // scale). Rescaling the whole UV set uniformly around its own center to
    // exactly 0-1 matches that fallback convention, so every part — tiny port
    // or full device body — shows the same one full stretch of the texture,
    // independent of its physical size.
    static void NormalizeUVsForPixelTexture(Mesh mesh)
    {
        Vector2[] uv = mesh.uv;
        if (uv == null || uv.Length == 0) return;

        float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        foreach (var c in uv)
        {
            if (c.x < minU) minU = c.x; if (c.x > maxU) maxU = c.x;
            if (c.y < minV) minV = c.y; if (c.y > maxV) maxV = c.y;
        }
        float spanU = Mathf.Max(maxU - minU, 0.0001f);
        float spanV = Mathf.Max(maxV - minV, 0.0001f);
        float cx = (minU + maxU) * 0.5f, cy = (minV + maxV) * 0.5f;

        for (int i = 0; i < uv.Length; i++)
            uv[i] = new Vector2((uv[i].x - cx) / spanU + 0.5f, (uv[i].y - cy) / spanV + 0.5f);
        mesh.uv = uv;
    }

    static Transform MakeVisualRoot(GameObject root, NetworkDevice nd)
    {
        var go = new GameObject("Visual");
        go.transform.SetParent(root.transform, false);
        nd.visualRoot = go.transform;
        return go.transform;
    }

    static Rigidbody AddPhysics(GameObject root)
    {
        var rb = root.AddComponent<Rigidbody>();
        rb.mass           = 1f;
        rb.linearDamping  = 3f;
        rb.angularDamping = 10f;
        rb.constraints    = RigidbodyConstraints.FreezeRotation;
        rb.isKinematic    = true;
        return rb;
    }

    // ── devices (geometry mirrors DeviceModelBuilder.BuildPC/BuildSwitch/BuildRouter) ──
    static GameObject BuildPC(Mats m)
    {
        var root = new GameObject("PC");
        var nd = root.AddComponent<NetworkDevice>();
        nd.deviceType = DeviceType.PC;
        nd.deviceName = "PC";
        nd.requiresTable = true;

        Transform visual = MakeVisualRoot(root, nd);

        PBCube("PC_Tower",        new Vector3(-0.38f, 0.30f, 0f),  new Vector3(0.28f, 0.60f, 0.24f), m.pcChassis, visual);
        PBCube("PC_MonitorFoot",  new Vector3(0.12f, 0.015f, 0f),  new Vector3(0.28f, 0.03f, 0.16f), m.pcChassis, visual);
        PBCube("PC_MonitorNeck",  new Vector3(0.12f, 0.11f, 0f),   new Vector3(0.05f, 0.16f, 0.05f), m.pcChassis, visual);
        PBCube("PC_MonitorBezel", new Vector3(0.12f, 0.47f, 0f),   new Vector3(0.72f, 0.56f, 0.06f), m.pcChassis, visual);
        PBCube("PC_Screen",       new Vector3(0.12f, 0.47f, -0.04f), new Vector3(0.60f, 0.44f, 0.02f), m.pcScreen, visual);
        PBCube("PC_Keyboard",     new Vector3(0.12f, 0.02f, -0.22f), new Vector3(0.55f, 0.04f, 0.20f), m.pcBody, visual);

        Vector3[] ports = { new Vector3(-0.38f, 0.20f, 0.12f) };
        GameObject portGo = PBCube("PC_Port0", ports[0], new Vector3(0.06f, 0.05f, 0.02f), m.port, visual);
        nd.InitPorts(ports);
        nd.SetPortVisual(0, portGo);
        nd.SetPortSideIndicator(ports[0], Vector3.forward);

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.35f, 0f);
        col.size   = new Vector3(1.0f, 0.75f, 0.5f);

        AddPhysics(root);
        return root;
    }

    static GameObject BuildSwitch(Mats m)
    {
        var root = new GameObject("Switch");
        var nd = root.AddComponent<NetworkDevice>();
        nd.deviceType = DeviceType.Switch;
        nd.deviceName = "Switch";
        nd.requiresTable = true;

        Transform visual = MakeVisualRoot(root, nd);

        PBCube("Switch_Body", new Vector3(0f, 0.09f, 0f), new Vector3(1.35f, 0.18f, 0.63f), m.switchBody, visual);
        PBCube("Switch_LED",  new Vector3(0f, 0.20f, 0f), new Vector3(1.26f, 0.018f, 0.09f), m.switchLed, visual);

        Vector3[] ports = new Vector3[6];
        GameObject[] portGos = new GameObject[6];
        Vector3 portCenter = Vector3.zero;
        for (int i = 0; i < 6; i++)
        {
            float x = -0.495f + i * 0.198f;
            ports[i] = new Vector3(x, 0.09f, -0.324f);
            portCenter += ports[i];
            portGos[i] = PBCube($"Switch_Port{i}", ports[i], new Vector3(0.072f, 0.054f, 0.018f), m.port, visual);
        }
        nd.InitPorts(ports);
        for (int i = 0; i < portGos.Length; i++) nd.SetPortVisual(i, portGos[i]);
        nd.SetPortSideIndicator(portCenter / ports.Length, Vector3.back);

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.09f, 0f);
        col.size   = new Vector3(1.35f, 0.225f, 0.63f);

        AddPhysics(root);
        return root;
    }

    static GameObject BuildRouter(Mats m)
    {
        var root = new GameObject("Router");
        var nd = root.AddComponent<NetworkDevice>();
        nd.deviceType = DeviceType.Router;
        nd.deviceName = "Router";
        nd.requiresTable = true;

        Transform visual = MakeVisualRoot(root, nd);

        PBCube("Router_Body",     new Vector3(0f, 0.14f, 0f),   new Vector3(0.65f, 0.25f, 0.46f), m.routerBody, visual);
        PBCube("Router_TopPanel", new Vector3(0f, 0.265f, 0f),  new Vector3(0.60f, 0.015f, 0.43f), m.routerChassis, visual);
        PBCylinder("Router_AntennaL", new Vector3(-0.18f, 0.53f, 0f), new Vector3(0.03f, 0.28f, 0.03f), m.routerChassis, visual);
        PBCylinder("Router_AntennaR", new Vector3(0.18f, 0.53f, 0f),  new Vector3(0.03f, 0.28f, 0.03f), m.routerChassis, visual);

        Vector3[] ports = new Vector3[3];
        GameObject[] portGos = new GameObject[3];
        Vector3 portCenter = Vector3.zero;
        for (int i = 0; i < 3; i++)
        {
            float x = -0.15f + i * 0.15f;
            ports[i] = new Vector3(x, 0.13f, -0.24f);
            portCenter += ports[i];
            portGos[i] = PBCube($"Router_Port{i}", ports[i], new Vector3(0.06f, 0.045f, 0.015f), m.port, visual);
        }
        nd.InitPorts(ports);
        for (int i = 0; i < portGos.Length; i++) nd.SetPortVisual(i, portGos[i]);
        nd.SetPortSideIndicator(portCenter / ports.Length, Vector3.back);

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.28f, 0f);
        col.size   = new Vector3(0.64f, 0.62f, 0.47f);

        AddPhysics(root);
        return root;
    }

    // ── Table prop (decoration only — NOT a gameplay device) ───────────────────
    // Unlike the gameplay Table (DeviceModelBuilder.BuildTable(width, length)),
    // this has no NetworkDevice/Table component and no CLI/port/networking
    // behavior at all — it's just a static mesh for dressing up level
    // backgrounds by hand in the Scene view. Baked at one fixed footprint
    // (unlike the gameplay table, which is deliberately kept procedural so its
    // footprint can be sized per-instance at placement — see the class doc
    // comment above). Since this is a plain GameObject with no gameplay math
    // tied to its dimensions, a level designer can still rescale a placed
    // instance's transform freely; it just won't regenerate geometry to match.
    const float PropTableWidth  = 1.4f;
    const float PropTableLength = 0.9f;

    [MenuItem("Tools/Capstone/Build Table Prop (Decoration)")]
    public static void BuildTableProp()
    {
        // Wipe just the prop mesh folder so re-running after a size/geometry
        // tweak doesn't leave orphaned baked meshes behind (materials and the
        // prefab itself are overwritten in place instead of wiped).
        if (AssetDatabase.IsValidFolder(PropMeshFolder)) AssetDatabase.DeleteAsset(PropMeshFolder);
        EnsureFolder(PropPrefabFolder);
        EnsureFolder(PropMeshFolder);
        EnsureFolder(MatFolder);
        EnsureFolder(TexFolder);
        s_meshCounter = 0;

        var m = BuildMaterials();
        GameObject built = BuildTablePropGO(m, PropTableWidth, PropTableLength);

        string path = $"{PropPrefabFolder}/TableProp.prefab";
        PrefabUtility.SaveAsPrefabAsset(built, path, out bool success);
        Object.DestroyImmediate(built);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (success)
            Debug.Log($"[DevicePrefabBuilder] Generated {path} (meshes in {PropMeshFolder}).");
        else
            Debug.LogError($"[DevicePrefabBuilder] Failed to save {path}");
    }

    // Geometry mirrors DeviceModelBuilder.BuildTable() exactly (same height/
    // thickness/leg-inset constants) at a single fixed width/length.
    static GameObject BuildTablePropGO(Mats m, float width, float length)
    {
        var root = new GameObject("TableProp");

        const float topThickness = DeviceModelBuilder.TableTopThickness;
        const float legThickness = DeviceModelBuilder.TableLegThickness;
        const float legInset     = DeviceModelBuilder.TableLegInset;
        const float tableHeight  = DeviceModelBuilder.TableHeight;

        PBCube("TableProp_Top", new Vector3(0f, tableHeight, 0f),
               new Vector3(width, topThickness, length), m.tableTop, root.transform, PropMeshFolder);

        float legHeight = tableHeight - topThickness * 0.5f;
        float hx = Mathf.Max(width * 0.5f - legInset, legThickness);
        float hz = Mathf.Max(length * 0.5f - legInset, legThickness);
        Vector3[] legOffsets =
        {
            new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz),
            new Vector3(-hx, 0f,  hz), new Vector3(hx, 0f,  hz),
        };
        for (int i = 0; i < legOffsets.Length; i++)
            PBCube($"TableProp_Leg{i}", new Vector3(legOffsets[i].x, legHeight * 0.5f, legOffsets[i].z),
                   new Vector3(legThickness, legHeight, legThickness), m.tableLeg, root.transform, PropMeshFolder);

        // Solid from the ground to the tabletop surface, same reasoning as the
        // gameplay table's collider (legs alone would let things pass through
        // the open space between them).
        float topSurfaceY = tableHeight + topThickness * 0.5f;
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, topSurfaceY * 0.5f, 0f);
        col.size   = new Vector3(width, topSurfaceY, length);

        return root;
    }

    // ── plumbing ─────────────────────────────────────────────────────────────
    static void SaveDevicePrefab(string name, GameObject built)
    {
        string path = $"{PrefabFolder}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(built, path, out bool success);
        Object.DestroyImmediate(built);
        if (!success) Debug.LogError($"[DevicePrefabBuilder] Failed to save {path}");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}
