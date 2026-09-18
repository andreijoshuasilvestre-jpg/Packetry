using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One-time scene bootstrapper.
/// Place this component on a GameObject called "Bootstrap" in SampleScene.
/// On Awake it creates the play area, managers, palette, and HUD.
/// Safe to run multiple times — checks for duplicates first.
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [Header("Play Area")]
    private const float planeScale = 1.5f;  // Unity plane is 10×10; scale 1.5 → 15×15
    [SerializeField] private Color planeColor     = new Color(0.12f, 0.14f, 0.18f);
    [SerializeField] private Color borderColor    = new Color(0.25f, 0.55f, 0.90f);

    [Header("Palette Zone")]
    [SerializeField] private Color palettePlateColor = new Color(0.08f, 0.10f, 0.14f);

    void Awake()
    {
        // Must run first — everything else that reads LevelManager.Instance
        // .CurrentLevel (TutorialManager, SetupLevelObjectives below) does so
        // in Start()/deferred coroutines, but setting it up first keeps the
        // ordering obvious rather than relying on Awake-before-Start alone.
        EnsureComponent<LevelManager>("LevelManager");

        SetupCamera();
        SetupLighting();
        SetupPlayArea();
        SetupManagers();
        SetupLevelContent();
        SetupUI();
        SetupTutorial();
        SetupLevelObjectives();
    }

    // ── Camera ───────────────────────────────────────────────────────────────

    void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }

        if (cam.GetComponent<IsometricCamera>() == null)
            cam.gameObject.AddComponent<IsometricCamera>();

        cam.backgroundColor = new Color(0.07f, 0.08f, 0.12f);
        cam.clearFlags = CameraClearFlags.SolidColor;

        // HDR + post-processing both have to be on or URP's Bloom never picks
        // up anything — every HDR-boosted glow color in the game (see
        // HologramMaterial.GlowMultiplier, ConstructionRevealEffect's own
        // Line/FillGlowMultiplier) would otherwise render identically to its
        // un-boosted value, since nothing ever captures values above 1.0.
        cam.allowHDR = true;
        var camData = cam.GetComponent<UniversalAdditionalCameraData>()
                   ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;

        SetupPostProcessingVolume();
    }

    // A single global Volume carrying DefaultVolumeProfile.asset (Bloom, tone
    // mapping, etc.). Nothing else in the scene ever creates one — the whole
    // scene is built here, not hand-placed — so without this, every effect
    // configured in that profile is inert no matter what the camera above is
    // set to.
    void SetupPostProcessingVolume()
    {
        if (Object.FindAnyObjectByType<Volume>() != null) return;

        var profile = Resources.Load<VolumeProfile>("DefaultVolumeProfile");
        if (profile == null)
        {
            Debug.LogWarning("GameBootstrap: DefaultVolumeProfile not found in Resources — " +
                              "post-processing (Bloom, etc.) will stay off.");
            return;
        }

        var go = new GameObject("Global Volume");
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.profile = profile;
    }

    // ── Lighting ─────────────────────────────────────────────────────────────

    void SetupLighting()
    {
        // Ensure a directional light exists
        if (Object.FindAnyObjectByType<Light>() == null)
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.96f, 0.88f);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    // ── Play Area ────────────────────────────────────────────────────────────

    void SetupPlayArea()
    {
        if (GameObject.Find("GamePlane") != null) return;

        // Ground
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "GamePlane";
        plane.transform.localScale = new Vector3(planeScale, 1f, planeScale);
        plane.GetComponent<Renderer>().material = MakeLitMat(planeColor, 0.05f);

        // Border frame (4 thin strips)
        float half  = planeScale * 5f; // half-extent in world units
        float thick = 0.08f;
        float h     = 0.04f;
        CreateBorderStrip("Border_N", new Vector3( 0,      h,  half), new Vector3(half * 2 + thick, h * 2, thick));
        CreateBorderStrip("Border_S", new Vector3( 0,      h, -half), new Vector3(half * 2 + thick, h * 2, thick));
        CreateBorderStrip("Border_E", new Vector3( half,   h,  0   ), new Vector3(thick, h * 2, half * 2 + thick));
        CreateBorderStrip("Border_W", new Vector3(-half,   h,  0   ), new Vector3(thick, h * 2, half * 2 + thick));

    }

    void CreateBorderStrip(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position   = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().material = MakeLitMat(borderColor, 0.4f);
        Object.Destroy(go.GetComponent<Collider>());
    }

    // ── Managers ─────────────────────────────────────────────────────────────

    void SetupManagers()
    {
        EnsureComponent<AudioManager>("AudioManager");
        EnsureComponent<MusicManager>("MusicManager");
        MusicManager.Instance.Play(MusicId.Gameplay);
        EnsureComponent<DragDropManager>("DragDropManager");
        EnsureComponent<CableManager>("CableManager");
        EnsureComponent<CableRemoverManager>("CableRemoverManager");
        EnsureComponent<DeviceRemoverManager>("DeviceRemoverManager");
        EnsureComponent<TableManager>("TableManager");
        EnsureComponent<TableRemoverManager>("TableRemoverManager");
        EnsureComponent<PingToolManager>("PingToolManager");
        EnsureComponent<PacketManager>("PacketManager");
        EnsureComponent<PacketSendManager>("PacketSendManager");
        EnsureComponent<AutoSaveManager>("AutoSaveManager");
    }

    // ── Palette ──────────────────────────────────────────────────────────────

    void SetupPalette()
    {
        if (GameObject.Find("DevicePalette") != null) return;
        var go = new GameObject("DevicePalette");
        go.AddComponent<DevicePalette>();
    }

    // ── HUD ──────────────────────────────────────────────────────────────────

    void SetupUI()
    {
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();
        }

        EnsureComponent<NetworkUIManager>("UIManager");
        EnsureComponent<CLIPanelManager>("CLIPanelManager");
        // TaskManager itself always exists — Level2Guide/Level3Guide/Level4Guide
        // still register tasks with it internally to drive their own stage
        // progression and completion detection (see their HandleTasksChanged).
        // Only the visible checklist panel is skipped for levels 1/2/3/4 — each
        // of those already drives its own step-by-step UI via TutorialUIManager
        // (TutorialManager for level 1, the guide classes for 2-4) and never
        // registers anything with TaskManager, so the generic checklist would
        // just sit there empty rather than being redundant with real content.
        EnsureComponent<TaskManager>("TaskManager");
        int level = LevelManager.Instance.CurrentLevel;
        if (level != 1 && level != 2 && level != 3 && level != 4) EnsureComponent<TaskUIManager>("TaskUIManager");
        EnsureComponent<NotificationManager>("NotificationManager");
        EnsureComponent<DeviceAddressPopup>("DeviceAddressPopup");
        EnsureComponent<LevelCompletePanel>("LevelCompletePanel");
        EnsureComponent<ConfirmDialogManager>("ConfirmDialogManager");
        EnsureComponent<SettingsPanelManager>("SettingsPanelManager");

        // TopologyDetector tryout — only in the dev sandbox (direct Editor
        // Play), never in a real numbered level. See TopologyDebugOverlay.
        if (level == LevelManager.DevSandboxLevel) EnsureComponent<TopologyDebugOverlay>("TopologyDebugOverlay");
    }

    // ── Tutorial ─────────────────────────────────────────────────────────────

    void SetupTutorial()
    {
        // TutorialUIManager first — TutorialManager reaches into it as soon as
        // its own (deferred, see TutorialManager.InitNextFrame) init runs.
        EnsureComponent<TutorialUIManager>("TutorialUIManager");
        EnsureComponent<TutorialManager>("TutorialManager");
    }

    // ── Level Content ────────────────────────────────────────────────────────
    // If this level already has a saved topology (the player started it
    // before), restore that instead of rebuilding that level's first-time
    // default content — TopologyLoader is generic and reused by any level;
    // only the "what's the default starting content" part is level-specific.
    // Level 1 (the tutorial) never restores a topology — replaying it is
    // meant to be a fresh linear walkthrough each time; only its completion
    // flag persists (see TutorialManager.CompleteTutorial).

    void SetupLevelContent()
    {
        int level = LevelManager.Instance.CurrentLevel;
        if (level == 1) return;

        if (SaveManager.HasSavedTopology(level)) { EnsureComponent<TopologyLoader>("TopologyLoader"); return; }

        // A level-template scene (see LevelTemplateScene / the Editor "Save Play
        // Topology As Level Template" tool) takes priority over a hand-written
        // *SceneSetup fallback — it's what lets a new level's starting topology be
        // authored without touching this switch at all.
        if (LevelTemplateScene.Exists(level)) { EnsureComponent<TemplateTopologyLoader>("TemplateTopologyLoader"); return; }

        if (level == 2) EnsureComponent<Level2SceneSetup>("Level2SceneSetup");
        else if (level == 3) EnsureComponent<Level3SceneSetup>("Level3SceneSetup");
        else if (level == 7) EnsureComponent<Level7SceneSetup>("Level7SceneSetup");
    }

    // ── Level Objectives ─────────────────────────────────────────────────────
    // TaskManager itself is level-agnostic (see its own doc comment) — each
    // level beyond the tutorial supplies its own objective provider here.
    // TutorialManager.Start already skips its walkthrough for any level other
    // than 1, so no branching is needed there.

    void SetupLevelObjectives()
    {
        int level = LevelManager.Instance.CurrentLevel;
        if (level == 2) EnsureComponent<Level2Guide>("Level2Guide");
        else if (level == 3) EnsureComponent<Level3Guide>("Level3Guide");
        else if (level == 4) EnsureComponent<Level4Guide>("Level4Guide");
        else if (level == 5) EnsureComponent<Level5Challenge>("Level5Challenge");
        else if (level == 6) EnsureComponent<Level6Challenge>("Level6Challenge");
        else if (level == 7) EnsureComponent<Level7Challenge>("Level7Challenge");
    }

    // ── Utility ──────────────────────────────────────────────────────────────

    static GameObject EnsureComponent<T>(string goName) where T : MonoBehaviour
    {
        var existing = Object.FindAnyObjectByType<T>();
        if (existing != null) return existing.gameObject;
        var go = new GameObject(goName);
        go.AddComponent<T>();
        return go;
    }

    static Material MakeLitMat(Color color, float smoothness = 0.3f)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0f);
        return mat;
    }
}
