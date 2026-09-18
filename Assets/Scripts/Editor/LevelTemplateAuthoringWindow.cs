using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Authors a level's starting topology by playing it, instead of hand-coding a
/// LevelNSceneSetup. Workflow: enter Play Mode, build the topology with the
/// normal in-game tools (drag/drop, cable draw, router CLI — including
/// deliberately-wrong IPs for a troubleshooting level), open this window
/// (Tools > Capstone > Level Templates), pick a level number, click
/// "Capture &amp; Save".
///
/// Unity discards anything built during Play Mode the instant it stops, so this
/// happens in two phases: capture the live scene right now (SceneStateCapture.
/// Capture — the same call AutoSaveManager already uses for the player's own
/// save) into SessionState, then exit Play Mode; LevelTemplateBaker below
/// notices the pending capture once Play Mode has actually exited and
/// reconstructs it as real prefab-backed GameObjects in a saved
/// Assets/Scenes/LevelTemplates/Level{N}_Template.unity scene — editable by hand
/// afterward like any other scene. Editor-only: this whole file lives under
/// Assembly-CSharp-Editor and is never part of a build.
/// </summary>
public class LevelTemplateAuthoringWindow : EditorWindow
{
    const string PendingJsonKey  = "Capstone.LevelTemplate.PendingJson";
    const string PendingLevelKey = "Capstone.LevelTemplate.PendingLevel";
    const string PendingFlagKey  = "Capstone.LevelTemplate.PendingBake";

    int levelNumber = 2;

    [MenuItem("Tools/Capstone/Level Templates/Save Play Topology As Level Template...")]
    static void Open()
    {
        // Deliberately NOT a utility window (GetWindow(true, ...)) — utility
        // windows have a known Unity IMGUI bug where they paint solid white
        // until manually resized/moved, and that's especially likely to bite
        // right here since this window is opened and closed right around a
        // Play Mode transition.
        var win = GetWindow<LevelTemplateAuthoringWindow>("Save Level Template");
        win.minSize = new Vector2(340, 130);
    }

    void OnEnable() => EditorApplication.update += Repaint; // guards against the same blank-paint issue
    void OnDisable() => EditorApplication.update -= Repaint;

    void OnGUI()
    {
        EditorGUILayout.Space(6);

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode, build the topology you want this level to start with " +
                "(devices, cables, router interface IPs — including any you want to leave " +
                "unassigned or wrong for a troubleshooting level), then come back here.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox(
            "Captures the current scene right now. Unity discards Play Mode changes on " +
            "stop, so clicking Save exits Play Mode automatically to finish writing the scene file.",
            MessageType.Info);

        levelNumber = EditorGUILayout.IntField("Level Number", levelNumber);

        EditorGUILayout.Space(6);
        using (new EditorGUI.DisabledScope(levelNumber < 1))
        {
            if (GUILayout.Button("Capture & Save", GUILayout.Height(28)))
                CaptureAndExitPlayMode();
        }
    }

    void CaptureAndExitPlayMode()
    {
        TopologySave topology = SceneStateCapture.Capture();
        string json = JsonUtility.ToJson(topology);

        SessionState.SetString(PendingJsonKey, json);
        SessionState.SetInt(PendingLevelKey, levelNumber);
        SessionState.SetBool(PendingFlagKey, true);

        Debug.Log($"[LevelTemplateAuthoring] Captured topology for Level {levelNumber} " +
                  "— exiting Play Mode to bake the scene...");

        Close();
        EditorApplication.isPlaying = false;
    }
}

/// <summary>Finishes the bake LevelTemplateAuthoringWindow started, once Play Mode
/// has actually exited — see that class's doc comment for why this can't happen
/// in the same step as the capture.</summary>
[InitializeOnLoad]
static class LevelTemplateBaker
{
    const string PendingJsonKey  = "Capstone.LevelTemplate.PendingJson";
    const string PendingLevelKey = "Capstone.LevelTemplate.PendingLevel";
    const string PendingFlagKey  = "Capstone.LevelTemplate.PendingBake";

    static readonly Color DefaultCableColor = new Color(0.55f, 0.55f, 0.55f); // matches CableManager.cableColor
    const float DefaultCableWidth = 0.06f;                                    // matches CableManager.cableWidth

    static LevelTemplateBaker()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode) return;
        if (!SessionState.GetBool(PendingFlagKey, false)) return;

        SessionState.SetBool(PendingFlagKey, false);
        string json = SessionState.GetString(PendingJsonKey, null);
        int level   = SessionState.GetInt(PendingLevelKey, -1);
        if (string.IsNullOrEmpty(json) || level < 1) return;

        var topology = JsonUtility.FromJson<TopologySave>(json);
        Bake(level, topology);
    }

    static void Bake(int level, TopologySave topology)
    {
        const string folder = "Assets/Scenes/LevelTemplates";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Scenes", "LevelTemplates");

        string path = LevelTemplateScene.ScenePath(level);

        // Re-open (and wipe) the existing template if this level was authored
        // before, rather than orphaning the old file — keeps one scene per level.
        Scene scene = File.Exists(path)
            ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        foreach (var go in scene.GetRootGameObjects())
            Object.DestroyImmediate(go);

        BuildTopologyInScene(scene, topology);
        BuildPreviewRig(scene);

        EditorSceneManager.SaveScene(scene, path);
        EditorSceneManager.CloseScene(scene, true);

        AddSceneToBuildSettings(path);
        AssetDatabase.Refresh();

        Debug.Log($"[LevelTemplateAuthoring] Saved Level {level}'s template to {path}");
    }

    // Mirrors SceneStateCapture.Restore()'s build order (tables, then devices,
    // then cables) but targets a specific edit-mode scene via
    // MoveGameObjectToScene instead of whatever scene happens to be active, and
    // skips the CableManager/DragDropManager calls Restore() makes — those
    // singletons don't exist outside Play Mode.
    static void BuildTopologyInScene(Scene scene, TopologySave topology)
    {
        foreach (var t in topology.tables)
        {
            var go = DeviceModelBuilder.BuildTable(t.width, t.length);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(t.x, t.y, t.z);
            go.GetComponent<Table>().SyncFinalCenter();
        }

        var devicesById = new Dictionary<string, NetworkDevice>();
        foreach (var d in topology.devices)
        {
            if (!System.Enum.TryParse(d.deviceType, out DeviceType type)) continue;

            GameObject go = type switch
            {
                DeviceType.PC     => DeviceModelBuilder.BuildPC(),
                DeviceType.Switch => DeviceModelBuilder.BuildSwitch(),
                DeviceType.Router => DeviceModelBuilder.BuildRouter(),
                _                 => DeviceModelBuilder.BuildPC()
            };
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(d.x, d.y, d.z);
            go.transform.rotation = Quaternion.Euler(0f, d.rotY, 0f);

            var nd = go.GetComponent<NetworkDevice>();
            nd.SaveId = d.deviceId;
            nd.isPaletteItem = false;

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            if (SubnetMath.IsDottedQuad(d.ipAddress) && SubnetMath.IsDottedQuad(d.ipMask))
            {
                var templateAddr = go.AddComponent<DeviceAddress>();
                templateAddr.SetAddress(d.ipAddress, d.ipMask);
            }

            if (d.portActive != null)
                for (int i = 0; i < d.portActive.Length && i < nd.PortCount; i++)
                    nd.SetPortActive(i, d.portActive[i]);

            // DeviceModelBuilder.BuildRouter() already attached+sized a
            // RouterInterfaceConfig; fill in whatever the player configured (or
            // deliberately left wrong/empty) while playing.
            if (type == DeviceType.Router && d.interfaces != null)
            {
                var ifaceCfg = go.GetComponent<RouterInterfaceConfig>();
                foreach (var ic in d.interfaces)
                {
                    var pc = ifaceCfg.Get(ic.port);
                    if (pc == null) continue;
                    pc.ip       = ic.ip ?? "";
                    pc.mask     = ic.mask ?? "";
                    pc.shutdown = ic.shutdown;
                }
            }

            devicesById[d.deviceId] = nd;
        }

        foreach (var c in topology.cables)
        {
            if (!devicesById.TryGetValue(c.sourceDeviceId, out var a)) continue;
            if (!devicesById.TryGetValue(c.targetDeviceId, out var b)) continue;

            var go = new GameObject($"Cable_{a.deviceName}_{b.deviceName}");
            SceneManager.MoveGameObjectToScene(go, scene);
            var cable = go.AddComponent<Cable>(); // [RequireComponent] auto-adds LineRenderer
            cable.Initialize(a, c.sourcePort, b, c.targetPort, MakeLineMaterial(), DefaultCableWidth, DefaultCableColor);
            a.ConnectPort(c.sourcePort, b, cable);
            b.ConnectPort(c.targetPort, a, cable);
        }
    }

    // Camera + light so the template scene renders something sensible if
    // opened and played on its own — mirrors GameBootstrap.SetupCamera()/
    // SetupLighting()'s basics (not the Volume/post-processing setup; that's
    // real-gameplay polish, not needed to just look at a topology). Both
    // carry TemplatePreviewOnly, which self-destroys them the moment this
    // scene is instead loaded additively into a real, GameBootstrap-driven
    // play session — see that class's doc comment for why.
    static void BuildPreviewRig(Scene scene)
    {
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var cam = camGo.AddComponent<Camera>();
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.12f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<IsometricCamera>();
        camGo.AddComponent<TemplatePreviewOnly>();

        var lightGo = new GameObject("Directional Light");
        SceneManager.MoveGameObjectToScene(lightGo, scene);
        var light = lightGo.AddComponent<Light>();
        light.type      = LightType.Directional;
        light.intensity = 1.2f;
        light.color     = new Color(1f, 0.96f, 0.88f);
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        lightGo.AddComponent<TemplatePreviewOnly>();
    }

    static Material MakeLineMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        return mat;
    }

    static void AddSceneToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
