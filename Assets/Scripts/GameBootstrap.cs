using System.Collections;
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
    private const float planeScale = 1.8f;  // Unity plane is 10×10; scale 1.8 → 18×18
    // Level 9 ("Multi-Department Network") places six PCs, three Switches and
    // a Router — more table space than the default 18×18 comfortably fits —
    // so it alone gets a larger plane (22.5×22.5). Every other level keeps
    // the normal size.
    private const float bigPlaneScale = 2.25f;
    [SerializeField] private Color planeColor     = new Color(0.12f, 0.14f, 0.18f);
    [SerializeField] private Color borderColor    = new Color(0.25f, 0.55f, 0.90f);
    // Bright off-white office wall.
    [SerializeField] private Color wallColor      = new Color(0.92f, 0.91f, 0.88f);
    const float wallHeight    = 4.5f;
    const float wallThickness = 0.12f;

    [Header("Palette Zone")]
    [SerializeField] private Color palettePlateColor = new Color(0.08f, 0.10f, 0.14f);

    // The level-entry sequence, in order (see EntrySequence) — each flag flips
    // true as its stage is reached. All three are reset at the top of Awake()
    // below since SceneManager.LoadScene mid-session (restart, next level)
    // does NOT trigger a domain reload — a static bool would otherwise stay
    // true from the previous level.
    //
    // TopologyLoaded: this level's starting topology (saved, template, or
    // scripted — see SetupLevelContent) is actually in the scene. Guides that
    // scan the pre-placed devices wait on this, since a template scene merges
    // in a frame or more after load.
    public static bool TopologyLoaded { get; private set; }

    // HudRevealStarted: the platform and topology have both finished building
    // in, so the HUD (sidebar, top bar, task panel) may start appearing — see
    // NetworkUIManager/TaskUIManager, which hold their reveal until this.
    public static bool HudRevealStarted { get; private set; }

    // IntroSequenceReady: the whole entrance (platform → topology → HUD) has
    // finished playing — TutorialManager and every Level2Guide..
    // Level11Challenge wait on this before showing their first tutorial
    // annotation/objective marker, so guidance never appears mid-construction.
    public static bool IntroSequenceReady { get; private set; }

    // ExitStarted: the level is being left (Home, Restart, a level's
    // Continue — see PlayExitSequence) and is only tearing down from here.
    // DragDropManager takes no more clicks, so nothing gets reopened mid-exit,
    // and AutoSaveManager stops saving, so a restart's already-cleared
    // progress (NetworkUIManager.OnReturnClicked) can't be written back by a
    // panel closing on the way out. Reset in Awake() like the flags above.
    public static bool ExitStarted { get; private set; }

    // Lets other scripts (LoadingScreenManager.ExitToLevelSelect) reach
    // PlayExitSequence without a scene search — same simple pattern every
    // other manager singleton in this project already uses.
    public static GameBootstrap Instance { get; private set; }

    // Kept from SetupPlayArea/SetupWalls so PlayExitSequence can animate
    // the exact same pieces back out again. groundRoot is a single parent
    // the plane + 4 border strips all live under (see SetupPlayArea) so
    // they scale up/down together as one "platform" growing from its own
    // center, instead of each piece scaling from its own separate pivot.
    Transform groundRoot;
    Transform[] wallPieces;

    void Awake()
    {
        Instance = this;
        TopologyLoaded = false;
        HudRevealStarted = false;
        IntroSequenceReady = false;
        ExitStarted = false;

        // Fresh slot-0-first subnet coloring for this level (see
        // SubnetColorRegistry) — cleared before anything below can restore a
        // saved topology or seed a template scene, so a replay's subnets get
        // re-numbered from scratch in whatever order they're re-assigned
        // rather than inheriting slots from a previous playthrough.
        SubnetColorRegistry.Reset();
        SaveManager.SubnetColorsChanged += OnSubnetColorsChanged;

        GameCursor.Apply();

        // Must run first — everything else that reads LevelManager.Instance
        // .CurrentLevel (TutorialManager, SetupLevelObjectives below) does so
        // in Start()/deferred coroutines, but setting it up first keeps the
        // ordering obvious rather than relying on Awake-before-Start alone.
        EnsureComponent<LevelManager>("LevelManager");

        // Build order: the room itself, then the managers everything else
        // talks to (TaskManager included — a restored save hands it task
        // state), then this level's starting topology, then the HUD, then
        // the tutorial/objectives that read all of the above. What the
        // player SEES follows the same order, but is paced separately — see
        // EntrySequence.
        SetupCamera();
        SetupLighting();
        SetupPlayArea();
        SetupManagers();
        SetupLevelContent();
        SetupUI();
        SetupTutorial();
        SetupLevelObjectives();

        StartCoroutine(EntrySequence());
    }

    void OnDestroy()
    {
        SaveManager.SubnetColorsChanged -= OnSubnetColorsChanged;
        // Leaving the level — its subnets shouldn't linger as "in use" labels
        // on the main menu's Settings page (see SettingsPanelManager.BuildSubnetRow).
        // Guarded so a restart whose new GameBootstrap already woke up (and
        // started assigning slots) isn't wiped by the old one's teardown.
        if (Instance == this) SubnetColorRegistry.Reset();
    }

    // The player recolored a Subnet Colors slot from the in-game Settings
    // dialog — repaint every subnet-colored PC, Switch and Cable already on
    // screen right away instead of waiting for the next load.
    void OnSubnetColorsChanged() => SubnetVisualizer.RepaintAll();

    // ── Entry sequence ───────────────────────────────────────────────────────
    // The one place the level's entrance is ordered, each stage waiting on
    // the previous one actually finishing rather than every piece running
    // off its own independent timer: platform (ground rise + wall drop) →
    // starting topology builds in (LevelBuildInEffect) → HUD reveals →
    // narration heads-up, on a narrated level (NarrationManager.ConfirmForLevel)
    // → tutorial/guide annotations allowed. A level with a lot of starting
    // topology just pushes the HUD back instead of overlapping it.
    //
    // The topology itself is placed (hidden) as soon as it's loaded — see
    // OnTopologyLoaded — only its reveal waits its turn here.

    // A template scene that never reports in (missing from Build Settings
    // after a rename, say) shouldn't hang the whole level behind a blank
    // HUD — carry on without it.
    const float TopologyLoadTimeout = 5f;

    IEnumerator EntrySequence()
    {
        // Ground/walls run on scaled time from this same Awake (see
        // SetupPlayArea), so the platform's finish is measured the same way.
        float levelStart = Time.time;

        // 1. Platform — already rising (SetupPlayArea). Starting topology
        //    loads meanwhile: inline for a save or a scripted setup, a frame
        //    or more later for a template scene.
        float loadWaitStart = Time.realtimeSinceStartup;
        while (!TopologyLoaded)
        {
            if (Time.realtimeSinceStartup - loadWaitStart > TopologyLoadTimeout)
            {
                Debug.LogWarning($"GameBootstrap: level {LevelManager.Instance.CurrentLevel}'s starting topology never finished loading — continuing without its build-in.");
                OnTopologyLoaded();
                break;
            }
            yield return null;
        }

        float untilWallsLand = AnimationSpeed.Seconds(AnimationKind.Level, WallsCompleteDelay) - (Time.time - levelStart);
        if (untilWallsLand > 0f) yield return new WaitForSeconds(untilWallsLand);

        // 2. Topology builds in on the finished platform.
        bool topologyBuilt = false;
        LevelBuildInEffect.Play(() => topologyBuilt = true);
        while (!topologyBuilt) yield return null;

        // 3. HUD — sidebar header first, then buttons, then the task panel.
        yield return new WaitForSeconds(AnimationSpeed.Seconds(AnimationKind.Level, HudRevealGap));
        HudRevealStarted = true;
        yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, HudRevealDuration + AnnotationGap));

        // 4. A level whose guidance is narrated says so first, so the player
        //    can switch to text only before the first line is spoken.
        int level = LevelManager.Instance.CurrentLevel;
        if (IsNarrated(level) && NarrationManager.Instance != null)
            yield return NarrationManager.Instance.ConfirmForLevel(level);

        // 5. Guidance may point at things now that they all exist on screen.
        IntroSequenceReady = true;
    }

    // Every SetupLevelContent path ends here once its topology is in the
    // scene — inline for a save/scripted setup, from TemplateTopologyLoader's
    // sceneLoaded callback for a template (still before any of it renders).
    // Hides it all immediately so it can build in later (see EntrySequence).
    // Idempotent: a template that shows up after TopologyLoadTimeout already
    // gave up on it just appears as-is instead of being hidden with nothing
    // left to reveal it.
    void OnTopologyLoaded()
    {
        if (TopologyLoaded) return;
        TopologyLoaded = true;
        FaceStartingTopology();
        LevelBuildInEffect.Prepare();
    }

    // Every device model's front — a PC's screen, a switch's or router's
    // port face — is its local -Z, and a camera at the same yaw as a device
    // sees it head-on (see DragDropManager.FacingYawForCamera). So a level
    // that opens with a topology already in place turns its opening view to
    // whichever 45° heading most of those devices share, instead of meeting
    // them side-on from the fixed default. A level that starts empty (the
    // tutorial, the sandbox) keeps the default.
    static void FaceStartingTopology()
    {
        if (IsometricCamera.Instance == null) return;

        var votes = new int[8];
        int total = 0;
        foreach (var device in FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
        {
            if (device.isPaletteItem) continue;
            int heading = Mathf.RoundToInt(Mathf.Repeat(device.transform.eulerAngles.y, 360f) / 45f) % 8;
            votes[heading]++;
            total++;
        }
        if (total == 0) return;

        int best = 0;
        for (int i = 1; i < votes.Length; i++)
            if (votes[i] > votes[best]) best = i;
        IsometricCamera.Instance.SetOpeningYaw(best * 45f);
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

        // Not Skybox (the default) — Unity's procedural default skybox was
        // silently rendering behind everything and being mistaken for the
        // background quad failing, when the quad just needed something
        // other than the skybox to actually be visible against. SolidColor
        // is only a fallback tone; SetupBackgroundQuad's quad is what
        // actually covers the screen once it's created below.
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.12f);

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
        SetupBackgroundQuad(cam);

        StartCoroutine(FadeBackgroundToLevelColor());
    }

    // ── Background: same scrolling pattern as the main menu ─────────────────
    // A two-camera compositing setup (separate background camera stacked
    // under the main one) was tried first, matching the technique
    // MainMenuCanvasBuilder effectively needs for a UI-style background
    // behind 3D geometry — but it hit a wall no amount of isolation testing
    // explained: a UI Canvas in Screen Space - Camera mode never drew its
    // graphics on that second camera in this project's URP setup (confirmed
    // with a plain flat-colored Image, no texture involved — still nothing),
    // and even a plain 3D quad on that camera stayed invisible. Ordinary 3D
    // geometry through the MAIN camera, meanwhile, rendered correctly in
    // every single test. So instead of a second camera, the pattern is a
    // quad parented directly to the main IsometricCamera itself — it rides
    // along rigidly with every pan/zoom/orbit exactly like a skybox, using
    // rendering that's already proven reliable, with none of the stacking
    // complexity. ScrollingQuadBackground scrolls its material's texture
    // offset the same way ScrollingBackground scrolls the menu's RawImage
    // uvRect.
    const string BackgroundPatternPath = "MainMenuUI/MainMenuBg_Pattern";
    // Must clear both: (a) IsometricCamera.maxDistance (60, how far the
    // camera can orbit from its look target) plus the play area's own
    // radius, so the quad never enters the same depth range as real scene
    // geometry, and (b) stay well inside the camera's farClipPlane (1000).
    const float BackgroundQuadDistance = 150f;
    const float BackgroundQuadSafetyMargin = 1.5f; // extra size so the quad never clips at frustum edges as the aspect ratio changes
    Renderer backgroundRenderer;

    // A restart's background color, carried into the reload so the level
    // comes back in the color it left in instead of starting from the menu's
    // untinted pattern and fading back up to it (see LoadingScreenManager.
    // RestartLevel). Same "plain static field, consumed on next load" idiom
    // as LevelManager.PendingLevelNumber.
    static Color? carriedBackgroundColor;

    /// <summary>Carries this level's current background color into the next
    /// level load — see carriedBackgroundColor.</summary>
    public void CarryBackgroundIntoNextLoad()
    {
        if (backgroundRenderer != null) carriedBackgroundColor = backgroundRenderer.material.color;
    }

    void SetupBackgroundQuad(Camera cam)
    {
        Color startTint = carriedBackgroundColor ?? Color.white;
        carriedBackgroundColor = null;

        if (GameObject.Find("Background") != null) return;

        var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadGo.name = "Background";
        Object.Destroy(quadGo.GetComponent<Collider>()); // no gameplay purpose, and it sits right in front of the camera
        quadGo.transform.SetParent(cam.transform, false);
        quadGo.transform.localPosition = new Vector3(0f, 0f, BackgroundQuadDistance);
        // No rotation needed — Unity's default Quad mesh normal already
        // points back toward -Z, i.e. already faces a camera positioned at
        // its parent's origin looking down +Z. (Confirmed the hard way: a
        // 180° flip here was silently backface-culling the whole quad, and
        // since nothing renders behind, the camera's own SolidColor clear
        // just showed through unchanged — which looked exactly like "the
        // quad isn't rendering" and sent troubleshooting the wrong way for
        // a long time before finally testing Cull Off to confirm it.)

        Vector2 quadSize = VisibleSizeAt(cam, BackgroundQuadDistance) * BackgroundQuadSafetyMargin;
        quadGo.transform.localScale = new Vector3(quadSize.x, quadSize.y, 1f);

        var renderer = quadGo.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows    = false;
        backgroundRenderer = renderer;

        var pattern = Resources.Load<Texture2D>(BackgroundPatternPath);
        var shader  = Shader.Find("Universal Render Pipeline/Unlit");
        var mat     = new Material(shader);
        renderer.material = mat; // before ScrollingQuadBackground below, which writes its first offset straight onto it
        if (pattern != null)
        {
            pattern.filterMode = FilterMode.Bilinear; // this texture has no mipmaps — Point filtering at BackgroundQuadDistance's minification aliased into visual mush
            mat.color   = startTint; // pattern is already tinted (see MainMenuCanvasBuilder) — FadeBackgroundToLevelColor below tints it toward the level color instead of multiplying it darker up front; a restart starts on that color already

            // The exact screen-space tile grid the menu's pattern layer samples
            // (ScrollingBackground.ScreenTiles), so the pattern doesn't
            // change size, or jump, across the menu → level swap. The quad
            // covers BackgroundQuadSafetyMargin× the visible area, so it
            // carries that many times the screen's tiles; and since its UV
            // origin is its own bottom-left corner — off-screen by
            // (margin − 1) / 2 of the visible area on each axis — the offset
            // shifts that back onto the screen's bottom-left corner, where
            // every RawImage copy of the pattern has its grid origin.
            Vector2 screenTiles = ScrollingBackground.ScreenTiles(ScrollingBackground.DefaultTileSizePixels);

            mat.mainTexture = pattern;
            mat.mainTextureScale = screenTiles * BackgroundQuadSafetyMargin;
            quadGo.AddComponent<ScrollingQuadBackground>()
                .SetOriginOffset(-screenTiles * ((BackgroundQuadSafetyMargin - 1f) * 0.5f));
        }
        else
        {
            mat.color = new Color(0.07f, 0.08f, 0.12f);
        }

        SetupBackgroundVignette(cam);
    }

    // The menu background's vignette (MainMenuCanvasBuilder.BuildVignette),
    // carried into the level so the background's edges stay darkened instead
    // of the vignette dissolving away with LoadingScreenManager's bridge.
    // Just in front of the pattern quad and depth-tested, so it only ever
    // darkens the background, never a device in front of it. A SpriteRenderer
    // rather than a URP Unlit quad: Sprites-Default is always alpha-blended,
    // with no transparency keyword a build could strip. Lowest sorting order,
    // so it draws before every other transparent (cables, walls, holograms)
    // rather than over them. Sized to the screen × the menu's own vignette
    // scale, so it's the same ellipse the menu just showed.
    const float BackgroundVignetteDistance = BackgroundQuadDistance - 1f;

    void SetupBackgroundVignette(Camera cam)
    {
        var sprite = VignetteSprite.Get();
        var go = new GameObject("Background_Vignette");
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, BackgroundVignetteDistance);

        Vector2 size = VisibleSizeAt(cam, BackgroundVignetteDistance) * MainMenuCanvasBuilder.MenuVignetteScreenScale;
        go.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f); // stretched non-uniformly, like the menu's Image — elliptical, matching the screen

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color  = MainMenuCanvasBuilder.VignetteColor;
        sr.sortingOrder = short.MinValue;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows    = false;
    }

    // World-space width/height of the camera's view at a given distance
    // straight ahead of it.
    static Vector2 VisibleSizeAt(Camera cam, float distance)
    {
        float height = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return new Vector2(height * cam.aspect, height);
    }

    // Only ever runs from here (SetupCamera, itself only called from Awake)
    // — i.e. every genuine scene load "enters" the level and gets this same
    // settle-in beat (a restart already starts on the level's color — see
    // carriedBackgroundColor — so for it this holds still); returning to the
    // menu or opening an in-level popup never touches the background again
    // afterward. This is
    // also the game's side of "the fade between the main menu and a level
    // should only change color" — LoadingScreenManager no longer fades the
    // menu's background out before the scene swap (see
    // ActivateAndFadeRoutine), so the very first gameplay frame already shows
    // the same pattern at the same scale (see SetupBackgroundQuad) the menu
    // just showed; this coroutine is the part that visibly continues that
    // into the level's own color, rather than the cut itself doing anything.
    const float BackgroundFadeDelay    = 0.15f; // brief settle so the swap doesn't read as a same-frame color snap
    const float BackgroundFadeDuration = 0.75f;

    IEnumerator FadeBackgroundToLevelColor()
    {
        // Timed from the first frame after this Awake, not from Awake itself:
        // Awake runs inside the scene-activation hitch, and that first frame's
        // unscaledDeltaTime carries the whole hitch (over a second in the
        // Editor) — without this the fade below finished in a single step and
        // the color just snapped.
        yield return null;
        yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, BackgroundFadeDelay));
        if (backgroundRenderer == null) yield break;

        Color from = backgroundRenderer.material.color;
        Color to   = GameTheme.LevelBackgroundColor(LevelManager.Instance.CurrentLevel);

        float t = 0f;
        while (t < BackgroundFadeDuration)
        {
            t += AnimationSpeed.UnscaledDelta(AnimationKind.Level);
            if (backgroundRenderer == null) yield break;
            backgroundRenderer.material.color = Color.Lerp(from, to, Mathf.Clamp01(t / BackgroundFadeDuration));
            yield return null;
        }
        backgroundRenderer.material.color = to;
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

            // Light.shadows defaults to None on a freshly AddComponent'd
            // Light — without this, the scene never casts a single shadow
            // no matter how the URP asset's shadow settings are configured.
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.7f;
            light.shadowNormalBias = 0.4f;

            // Yaw tracks the orbit camera (see SunFollowsCamera) so shadows
            // read consistently on screen regardless of Q/E rotation; pitch
            // stays fixed at the same 50° the light used to be locked to.
            go.AddComponent<SunFollowsCamera>();
        }
    }

    // ── Play Area ────────────────────────────────────────────────────────────

    // Set by SetupPlayArea, consumed by SetupManagers once DragDropManager
    // exists — keeps its placement-bounds clamp in sync with whatever size
    // plane this level actually got instead of assuming the default.
    float playAreaHalfExtent;

    void SetupPlayArea()
    {
        int level = LevelManager.Instance.CurrentLevel;
        float scale = level == 9 ? bigPlaneScale : planeScale;
        playAreaHalfExtent = scale * 5f; // half-extent in world units

        if (GameObject.Find("GamePlane") != null) return;

        // Ground
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "GamePlane";
        plane.transform.localScale = new Vector3(scale, 1f, scale);
        plane.GetComponent<Renderer>().material = MakeLitMat(planeColor, 0.05f);

        // Border frame (4 thin strips)
        float half  = playAreaHalfExtent;
        float thick = 0.08f;
        float h     = 0.04f;
        var borderMat = MakeShadedMat(borderColor, 0.4f, 501);
        var borderN = CreateBorderStrip("Border_N", borderMat, new Vector3( 0,      h,  half), new Vector3(half * 2 + thick, h * 2, thick));
        var borderS = CreateBorderStrip("Border_S", borderMat, new Vector3( 0,      h, -half), new Vector3(half * 2 + thick, h * 2, thick));
        var borderE = CreateBorderStrip("Border_E", borderMat, new Vector3( half,   h,  0   ), new Vector3(thick, h * 2, half * 2 + thick));
        var borderW = CreateBorderStrip("Border_W", borderMat, new Vector3(-half,   h,  0   ), new Vector3(thick, h * 2, half * 2 + thick));

        // Parented under a shared root positioned at the play area's own
        // center (world origin, where GamePlane already sits) so scaling
        // that ONE transform grows the whole platform — plane and all 4
        // border strips together — outward from its center, instead of
        // each piece scaling from its own separate pivot (which would just
        // have the border strips puff into existence in place).
        var groundGo = new GameObject("Ground");
        groundGo.transform.position = Vector3.zero;
        plane.transform.SetParent(groundGo.transform, true);
        borderN.transform.SetParent(groundGo.transform, true);
        borderS.transform.SetParent(groundGo.transform, true);
        borderE.transform.SetParent(groundGo.transform, true);
        borderW.transform.SetParent(groundGo.transform, true);

        groundRoot = groundGo.transform;
        StartCoroutine(AnimateGroundGrow(groundRoot, half));

        SetupWalls(half);
    }

    // ── Entrance timing ──────────────────────────────────────────────────────
    // The platform's own beats (blank background only → platform rises → the
    // 4 walls drop in one after another) are fixed offsets from Awake, each
    // timed off the one before it. Everything after — topology, HUD,
    // annotations — is sequenced by EntrySequence on actual completion.
    // Every duration and offset here is authored at 1x and shortened at its
    // use site by the Level animation speed setting (AnimationSpeed.Seconds/
    // Delta), the exit sequence's included — so the beats keep their spacing
    // relative to each other at any speed.
    const float IntroBlankHold = BackgroundFadeDelay + BackgroundFadeDuration; // background-only beat before the platform starts rising — ends right as its own color settle finishes
    const float WallDropGap           = 0.15f; // gap after the ground finishes rising before the first wall starts dropping
    const float WallIndividualStagger = 0.12f; // gap between each of the 4 walls' own drop start — they no longer all drop together
    const float HudRevealGap          = 0.3f;  // slight delay after the topology finishes building before the HUD starts appearing
    const float AnnotationGap         = 0.3f;  // further slight delay after the HUD finishes before tutorial/guide annotations and markers are allowed to appear

    static float WallDropStartDelay => IntroBlankHold + GroundRiseDuration + WallDropGap;
    static float WallsCompleteDelay => WallDropStartDelay + 3 * WallIndividualStagger + WallDropDuration;

    /// <summary>How long after HudRevealStarted the HUD buttons start flashing in — the
    /// sidebar's "TOOLS" header slides in first, at HudRevealStarted itself (see
    /// NetworkUIManager.BuildSidebar), with the task panel following the buttons
    /// (see TaskUIManager). At 1x — every caller shortens it by the Level
    /// animation speed (AnimationSpeed.Seconds), same as the beats here.</summary>
    public const float HudButtonsDelay = 0.25f;

    // Conservative upper bound on how long NetworkUIManager's own button
    // stagger can run (see StaggerRevealFlashChildren) — approximated as a
    // tuned constant rather than plumbing an exact completion callback
    // through, matching how every other beat in this sequence is timed.
    const int   ButtonRevealMaxCount      = 8;
    const float ButtonRevealStagger       = 0.14f; // must match StaggerRevealFlashChildren's staggerDelay
    const float ButtonRevealFlashDuration = 0.34f; // afterimage + click-punch glow — see HudButtonRevealFlash, SidebarIconButtonFX.ClickPunchRoutine

    static float HudRevealDuration =>
        HudButtonsDelay + (ButtonRevealMaxCount - 1) * ButtonRevealStagger + ButtonRevealFlashDuration;

    GameObject CreateBorderStrip(string name, Material sharedMat, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position   = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = sharedMat;
        Object.Destroy(go.GetComponent<Collider>());
        return go;
    }

    // ── Entrance: ground and walls scale up from nothing, with a dust puff
    // when each finishes ─────────────────────────────────────────────────────
    // Same hand-rolled-coroutine/ease-out/snap-to-final-value shape as
    // Table.AnimateAxis and FadeBackgroundToLevelColor above — no animation
    // library anywhere else in this project, so this matches rather than
    // introducing a new pattern. Scale, not position — pieces grow from
    // nothing at their own rest position/size instead of sliding in from
    // off-frame, with ConstructionDust.EmitBurst marking the moment each one
    // finishes, like a physical piece thumping into place.
    const float MinGrowScale       = 0.001f; // not literally 0 — avoids a degenerate zero-scale collider on GamePlane
    const float GroundRiseDuration = 0.4f;
    const float WallDropDuration   = 0.4f;

    IEnumerator AnimateGroundGrow(Transform root, float halfExtent)
    {
        // Snapped to (near-)zero scale immediately, synchronously, the same
        // frame the platform is created — otherwise it would sit fully
        // visible at rest for the whole IntroBlankHold beat below instead of
        // actually staying hidden through the "blank state" phase.
        Vector3 restScale = root.localScale;
        root.localScale = Vector3.one * MinGrowScale;

        yield return new WaitForSeconds(AnimationSpeed.Seconds(AnimationKind.Level, IntroBlankHold));

        float t = 0f;
        while (t < GroundRiseDuration)
        {
            t += AnimationSpeed.Delta(AnimationKind.Level);
            float eased = EaseOutQuad(Mathf.Clamp01(t / GroundRiseDuration));
            if (root == null) yield break;
            root.localScale = Vector3.LerpUnclamped(Vector3.one * MinGrowScale, restScale, eased);
            yield return null;
        }
        if (root == null) yield break;
        root.localScale = restScale;

        ConstructionDust.EmitBurst(root.position, halfExtent);
    }

    // One wall at a time (see WallIndividualStagger) instead of all 4
    // growing together — each call gets its own delay from SetupWalls.
    IEnumerator AnimateWallGrow(Transform wall, float delay)
    {
        // Same "hide immediately, synchronously" reasoning as
        // AnimateGroundGrow — snapped above before the delay/animation loop,
        // not after, so the wall doesn't sit fully built at rest for the
        // whole delay first.
        Vector3 restScale = wall.localScale;
        wall.localScale = Vector3.one * MinGrowScale;

        yield return new WaitForSeconds(AnimationSpeed.Seconds(AnimationKind.Level, delay));
        if (wall == null) yield break;

        float t = 0f;
        while (t < WallDropDuration)
        {
            t += AnimationSpeed.Delta(AnimationKind.Level);
            if (wall == null) yield break;
            float eased = EaseOutQuad(Mathf.Clamp01(t / WallDropDuration));
            wall.localScale = Vector3.LerpUnclamped(Vector3.one * MinGrowScale, restScale, eased);
            yield return null;
        }
        if (wall == null) yield break;
        wall.localScale = restScale;

        // Radius off the wall's own footprint (whichever of X/Z is its
        // length, not its thickness) — a fixed wallHeight-based radius used
        // to spawn the whole puff inside a ~1.5-unit circle regardless of
        // whether the wall itself was 15 units long, reading as a tight,
        // clustered dot instead of spreading along the wall.
        ConstructionDust.EmitBurst(wall.position, Mathf.Max(wall.localScale.x, wall.localScale.z) * 0.5f, count: 26);
    }

    static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);

    // ── Exit sequence: the entrance, reversed ───────────────────────────────
    // Played before leaving a level back to Level Select (see
    // LoadingScreenManager.ExitToLevelSelect → PlayExitSequence) instead of
    // the HUD/scene just vanishing with the scene swap: open panels and
    // tutorial guidance clear, then the HUD retreats (mirrors it appearing last on the way
    // in), then the current topology clears away (see LevelBuildInEffect.
    // PlayExitForScene — cables vanish, then devices, then tables, the
    // reverse of the entrance's topology order), then the walls shrink back down to nothing
    // (reverse stagger order — the last wall to grow in shrinks away
    // first), then the platform shrinks away too, then
    // a brief blank-background hold before the caller's onComplete
    // (typically the actual scene swap) fires — the same beats as the
    // entrance, in reverse. Each shrink gets its own dust puff right as it
    // *starts* (see ConstructionDust.EmitBurst) — the same "solid boundary"
    // moment the entrance's burst marks, just crossed in the other
    // direction: entrance bursts once a piece finishes becoming solid, exit
    // bursts the instant a piece stops being solid and starts shrinking away.
    //
    // Same beats as the entrance but played well over twice as fast —
    // leaving a level is something a player does over and over, so it
    // should read as a quick tidy-up, not a second show. The Level Select
    // preload runs alongside this rather than before it (see
    // LoadingScreenManager.PreloadDuringExit). A restart plays this same
    // exit (LoadingScreenManager.RestartLevel).
    const float ExitGap                = 0.05f; // gap between each stage below
    const float ExitWallStagger        = 0.03f;
    const float ExitWallDuration       = 0.18f;
    const float ExitGroundDuration     = 0.18f;
    const float ExitBlankHold          = 0.1f;

    public void PlayExitSequence(System.Action onComplete) => PlayExitSequence(onComplete, holdUntilSettled: false);

    /// <summary>holdUntilSettled: the swap after this has nothing covering it
    /// (a restart — see LoadingScreenManager.RestartLevel), so the closing
    /// blank hold waits for the walls' and platform's dust puffs to finish
    /// first — the swap would otherwise cut them off mid-fade.</summary>
    public void PlayExitSequence(System.Action onComplete, bool holdUntilSettled)
    {
        StartCoroutine(ExitSequenceRoutine(onComplete, holdUntilSettled));
    }

    IEnumerator ExitSequenceRoutine(System.Action onComplete, bool holdUntilSettled)
    {
        ExitStarted = true;
        CloseOpenPanels();

        // Guidance goes first and instantly — a callout or world marker
        // still pointing at a device while it shrinks away reads as a
        // leftover, not part of the exit.
        TutorialUIManager.Instance?.Hide();
        TutorialUIManager.Instance?.HideWorldMarkers();

        // Whole HUD retreats — "TOOLS" header slides out while the buttons
        // fade — not just the button icons, which used to leave the header
        // sitting there through the rest of the exit.
        bool hudExitDone = NetworkUIManager.Instance == null;
        NetworkUIManager.Instance?.SlideOutHud(() => hudExitDone = true);

        // Task panel retreats alongside the HUD buttons' own fade — the
        // reverse of its own flash-in shortly after them on the way in (see
        // TaskUIManager.TaskPanelRevealGap).
        bool taskPanelExitDone = TaskUIManager.Instance == null;
        TaskUIManager.Instance?.PlayExitReveal(() => taskPanelExitDone = true);

        while (!hudExitDone || !taskPanelExitDone) yield return null;
        yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, ExitGap));

        // Topology clears off the platform before the platform itself
        // shrinks away — the exact reverse of the entrance's platform-first
        // order (see LevelBuildInEffect's class doc).
        bool topologyExitDone = false;
        LevelBuildInEffect.PlayExitForScene(() => topologyExitDone = true);
        while (!topologyExitDone) yield return null;

        if (wallPieces != null && wallPieces.Length > 0)
        {
            // Reverse stagger order — whichever wall grew in last shrinks
            // away first, exactly undoing the entrance's own order.
            for (int i = 0; i < wallPieces.Length; i++)
                StartCoroutine(AnimateWallShrink(wallPieces[wallPieces.Length - 1 - i], i * ExitWallStagger));
            yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, (wallPieces.Length - 1) * ExitWallStagger + ExitWallDuration + ExitGap));
        }

        if (groundRoot != null)
        {
            StartCoroutine(AnimateGroundShrink(groundRoot, playAreaHalfExtent));
            yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, ExitGroundDuration));
        }

        if (holdUntilSettled)
            while (ConstructionDust.AnyAlive) yield return null;

        yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, ExitBlankHold));

        onComplete?.Invoke();
    }

    // Whatever the player had open or mid-use when they left — a CLI
    // terminal or address popup used to float over the emptying platform for
    // the whole exit (and a restart then held it in its freeze-frame over the
    // swap). Each panel plays its own close. Modal panels (Settings, concept
    // cards, the confirm dialog itself) aren't listed: their dim blocks the
    // top bar, so none can be open when an exit starts. A new non-modal panel
    // belongs here.
    static void CloseOpenPanels()
    {
        InteractionMode.CancelOthers(null); // live cable preview, remove highlights, drag ghost...
        CLIPanelManager.Instance?.Close();
        DeviceAddressPopup.Instance?.Hide();
        InterfacePickerPopup.Instance?.Hide();
        PacketTypePickerPopup.Instance?.Hide();
        AnnotationWarningManager.Instance?.Hide();
    }

    // Mirror of AnimateWallGrow — shrinks from its current (rest) scale back
    // down to nothing instead of growing up to rest, with a dust puff at
    // the moment it starts (see the Exit sequence doc comment above).
    IEnumerator AnimateWallShrink(Transform wall, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, delay));
        if (wall == null) yield break;

        // Radius off the wall's own footprint (whichever of X/Z is its
        // length, not its thickness) — a fixed wallHeight-based radius used
        // to spawn the whole puff inside a ~1.5-unit circle regardless of
        // whether the wall itself was 15 units long, reading as a tight,
        // clustered dot instead of spreading along the wall.
        ConstructionDust.EmitBurst(wall.position, Mathf.Max(wall.localScale.x, wall.localScale.z) * 0.5f, count: 26);

        Vector3 restScale = wall.localScale;
        float t = 0f;
        while (t < ExitWallDuration)
        {
            t += AnimationSpeed.UnscaledDelta(AnimationKind.Level);
            if (wall == null) yield break;
            float eased = EaseOutQuad(Mathf.Clamp01(t / ExitWallDuration));
            wall.localScale = Vector3.LerpUnclamped(restScale, Vector3.one * MinGrowScale, eased);
            yield return null;
        }
    }

    // Mirror of AnimateGroundGrow — shrinks the whole platform root back
    // down to nothing instead of growing up to rest, with a dust puff at
    // the moment it starts.
    IEnumerator AnimateGroundShrink(Transform root, float halfExtent)
    {
        ConstructionDust.EmitBurst(root.position, halfExtent);

        Vector3 restScale = root.localScale;
        float t = 0f;
        while (t < ExitGroundDuration)
        {
            t += AnimationSpeed.UnscaledDelta(AnimationKind.Level);
            if (root == null) yield break;
            float eased = EaseOutQuad(Mathf.Clamp01(t / ExitGroundDuration));
            root.localScale = Vector3.LerpUnclamped(restScale, Vector3.one * MinGrowScale, eased);
            yield return null;
        }
    }

    // Four cosmetic walls around the play area's edge, faded per-side by
    // PlayAreaWalls based on camera angle so they read as an enclosing room
    // (office setting) without ever blocking the isometric view inward.
    void SetupWalls(float half)
    {
        var wallsGo = new GameObject("Walls");
        var wallMat = MakeWallMat(wallColor);

        // North/South span the full outer width (covering the corners);
        // East/West are shrunk by wallThickness so they butt against North/
        // South's inner faces instead of overlapping their corner volume —
        // the previous +wallThickness on all 4 sides made every corner a
        // shared cube where both walls' top caps were coplanar, z-fighting.
        float wy = wallHeight * 0.5f;
        var north = CreateWall("Wall_N", wallsGo.transform, wallMat, new Vector3(0,     wy,  half), new Vector3(half * 2 + wallThickness, wallHeight, wallThickness));
        var south = CreateWall("Wall_S", wallsGo.transform, wallMat, new Vector3(0,     wy, -half), new Vector3(half * 2 + wallThickness, wallHeight, wallThickness));
        var east  = CreateWall("Wall_E", wallsGo.transform, wallMat, new Vector3(half,  wy,  0   ), new Vector3(wallThickness, wallHeight, half * 2 - wallThickness));
        var west  = CreateWall("Wall_W", wallsGo.transform, wallMat, new Vector3(-half, wy,  0   ), new Vector3(wallThickness, wallHeight, half * 2 - wallThickness));

        wallsGo.AddComponent<PlayAreaWalls>().Init(Vector3.zero,
            (north, Vector3.forward),
            (south, Vector3.back),
            (east,  Vector3.right),
            (west,  Vector3.left));

        wallPieces = new[] { north.transform, south.transform, east.transform, west.transform };

        // Each wall gets its own start time (WallDropStartDelay + its index *
        // WallIndividualStagger) instead of all 4 growing in lockstep — see
        // the Entrance sequencing block above SetupPlayArea.
        for (int i = 0; i < wallPieces.Length; i++)
            StartCoroutine(AnimateWallGrow(wallPieces[i], WallDropStartDelay + i * WallIndividualStagger));
    }

    Renderer CreateWall(string name, Transform parent, Material sharedMat, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position   = pos;
        go.transform.localScale = size;
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = sharedMat;
        Object.Destroy(go.GetComponent<Collider>());
        return renderer;
    }

    // ── Managers ─────────────────────────────────────────────────────────────

    void SetupManagers()
    {
        EnsureComponent<AudioManager>("AudioManager");
        EnsureComponent<MusicManager>("MusicManager");
        EnsureComponent<NarrationManager>("NarrationManager");
        MusicManager.Instance.Play(MusicId.Gameplay);
        EnsureComponent<DragDropManager>("DragDropManager").GetComponent<DragDropManager>()
            .SetPlayAreaHalfExtent(playAreaHalfExtent);
        EnsureComponent<CableManager>("CableManager");
        EnsureComponent<CableRemoverManager>("CableRemoverManager");
        EnsureComponent<PortHologramHoverManager>("PortHologramHoverManager");
        EnsureComponent<DeviceRemoverManager>("DeviceRemoverManager");
        EnsureComponent<TableManager>("TableManager");
        EnsureComponent<TableRemoverManager>("TableRemoverManager");
        EnsureComponent<PingToolManager>("PingToolManager");
        EnsureComponent<PacketManager>("PacketManager");
        EnsureComponent<PacketSendManager>("PacketSendManager");
        EnsureComponent<AutoSaveManager>("AutoSaveManager");
        EnsureComponent<UndoManager>("UndoManager");
        // Before SetupLevelContent, not with the UI — TopologyLoader hands a
        // resumed save's task state to it (see TaskManager.RestoreCompleted).
        EnsureComponent<TaskManager>("TaskManager");
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
        // On-screen turn/reset buttons and the 2D network diagram they open,
        // plus the rotate buttons shown while a device is selected.
        EnsureComponent<ViewControlsHud>("ViewControlsHud");
        EnsureComponent<DeviceRotateHud>("DeviceRotateHud");
        EnsureComponent<NetworkDiagramPanel>("NetworkDiagramPanel");
        // TaskManager itself always exists (see SetupManagers) — Level2Guide/
        // Level3Guide/Level4Guide still register tasks with it internally to
        // drive their own stage progression and completion detection (see
        // their HandleTasksChanged). Only the visible checklist panel is
        // skipped for levels 1/2/3/4 — each of those already drives its own
        // step-by-step UI via TutorialUIManager (TutorialManager for level 1,
        // the guide classes for 2-4), so the generic checklist would just be
        // redundant with it.
        int level = LevelManager.Instance.CurrentLevel;
        if (level != 1 && level != 2 && level != 3 && level != 4) EnsureComponent<TaskUIManager>("TaskUIManager");
        EnsureComponent<NotificationManager>("NotificationManager");
        EnsureComponent<ActivitySubtitle>("ActivitySubtitle");
        EnsureComponent<AnnotationWarningManager>("AnnotationWarningManager");
        EnsureComponent<DeviceAddressPopup>("DeviceAddressPopup");
        EnsureComponent<LevelCompletePanel>("LevelCompletePanel");
        EnsureComponent<ConceptCardManager>("ConceptCardManager");
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
    //
    // Every path ends in OnTopologyLoaded once its content is actually in
    // the scene, so EntrySequence and the level's guide both know when it's
    // safe to look at it.

    void SetupLevelContent()
    {
        int level = LevelManager.Instance.CurrentLevel;
        if (level == 1) { OnTopologyLoaded(); return; }

        // A finished guided run has nothing left to resume — restoring it
        // would re-complete every task the moment the level registers them.
        // Replay from the level's default content instead, same as the HUD's
        // Restart (completion flag and stars are kept either way). A
        // challenge's finished build is the player's own design, so it's
        // restored like any other save (see IKeepsFinishedBuild).
        if (SaveManager.IsRunFinished(level) && !KeepsFinishedBuild(level)) SaveManager.ClearLevelProgress(level);

        if (SaveManager.HasSavedTopology(level))
        {
            EnsureComponent<TopologyLoader>("TopologyLoader");
            OnTopologyLoaded();
            return;
        }

        // A level-template scene (see LevelTemplateScene / the Editor "Save Play
        // Topology As Level Template" tool) takes priority over a hand-written
        // *SceneSetup fallback — it's what lets a new level's starting topology be
        // authored without touching this switch at all.
        if (LevelTemplateScene.Exists(level))
        {
            EnsureComponent<TemplateTopologyLoader>("TemplateTopologyLoader")
                .GetComponent<TemplateTopologyLoader>().Load(level, OnTopologyLoaded);
            return;
        }

        if (level == 2) EnsureComponent<Level2SceneSetup>("Level2SceneSetup");
        else if (level == 3) EnsureComponent<Level3SceneSetup>("Level3SceneSetup");
        else if (level == 7) EnsureComponent<Level7SceneSetup>("Level7SceneSetup");
        OnTopologyLoaded();
    }

    // ── Level Objectives ─────────────────────────────────────────────────────
    // TaskManager itself is level-agnostic (see its own doc comment) — each
    // level supplies its own objective provider here. Level 1's is the
    // tutorial itself, which SetupTutorial already built (EnsureComponent
    // just finds it); TutorialManager.Start skips its walkthrough for any
    // other level, so no branching is needed there.

    void SetupLevelObjectives()
    {
        var provider = ObjectiveProviderFor(LevelManager.Instance.CurrentLevel);
        if (provider != null) EnsureComponent(provider);
    }

    // The one level → objective provider mapping — SetupLevelContent reads it
    // too (before the provider exists) to ask whether a finished run is kept,
    // and EntrySequence to ask whether the level is narrated.
    static System.Type ObjectiveProviderFor(int level) => level switch
    {
        1  => typeof(TutorialManager),
        2  => typeof(Level2Guide),
        3  => typeof(Level3Guide),
        4  => typeof(Level4Guide),
        5  => typeof(Level5Challenge),
        6  => typeof(Level6Challenge),
        7  => typeof(Level7Challenge),
        8  => typeof(Level8Challenge),
        9  => typeof(Level9Challenge),
        10 => typeof(Level10Challenge),
        11 => typeof(Level11Challenge),
        _  => null,
    };

    static bool KeepsFinishedBuild(int level) =>
        typeof(IKeepsFinishedBuild).IsAssignableFrom(ObjectiveProviderFor(level));

    static bool IsNarrated(int level) =>
        typeof(INarratedLevel).IsAssignableFrom(ObjectiveProviderFor(level));

    // ── Utility ──────────────────────────────────────────────────────────────

    static GameObject EnsureComponent<T>(string goName) where T : MonoBehaviour
    {
        var existing = Object.FindAnyObjectByType<T>();
        if (existing != null) return existing.gameObject;
        var go = new GameObject(goName);
        go.AddComponent<T>();
        return go;
    }

    // Same as above for a type only known at runtime (ObjectiveProviderFor);
    // the GameObject is named after the component, as every call above does.
    static GameObject EnsureComponent(System.Type type)
    {
        var existing = Object.FindAnyObjectByType(type) as Component;
        if (existing != null) return existing.gameObject;
        var go = new GameObject(type.Name);
        go.AddComponent(type);
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

    // Same as MakeLitMat, but with DeviceModelBuilder's top-to-bottom fake
    // shading gradient baked in (bright top / darker bottom, as if lit from
    // above) so a flat primitive face reads as gently lit instead of a flat
    // wash of color — the same trick every device body uses. Noise variation
    // is left at 0 here: devices bake in a bit of pixel-grain on top of the
    // gradient for their PS1 look, but scene geometry (walls, border) just
    // wants the shading, not the grainy texture.
    static Material MakeShadedMat(Color color, float smoothness, int seed)
    {
        var mat = MakeLitMat(color, smoothness);
        Texture2D tex = DeviceModelBuilder.CreatePixelNoiseTexture(DeviceModelBuilder.PixelTexSize, color, 0f, seed);
        DeviceModelBuilder.ApplyPixelTexture(mat, tex);
        return mat;
    }

    // Same as MakeLitMat but Transparent-surfaced (alpha blend, no
    // Z-write) — PlayAreaWalls fades each wall's alpha via a per-renderer
    // MaterialPropertyBlock, which needs the shared material itself
    // configured for transparency up front (surface state can't be
    // overridden per-instance the way color can). No baked shading texture —
    // Smoothness 0.55 matches the imported CubicleDivider asset's
    // Mat_Wall_White (also a plain, untextured URP Lit material), so the
    // walls pick up the same soft specular highlight/reflection from the
    // scene's lighting instead of reading fully matte.
    static Material MakeWallMat(Color color)
    {
        var mat = MakeLitMat(color, 0.55f);
        mat.name = "WallMat";
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetFloat("_Blend", 0f);   // Alpha blend
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        return mat;
    }
}
