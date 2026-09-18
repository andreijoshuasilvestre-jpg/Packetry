using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

/// <summary>
/// Scene bootstrapper for the main menu — logo + button stack, and the
/// level-select panel, toggled in this same scene (both live in
/// MainMenu.unity; not separate scenes — see project history for why that
/// split was tried and reverted).
///
/// Resolves each shell — MainMenuShellBuilder/MainMenuShellView and
/// LevelSelectShellBuilder/LevelSelectManager — from whichever source is
/// available, cheapest-and-most-editable first:
///   1. A pre-placed instance dragged into MainMenu.unity and wired to the
///      shellView/levelSelectPrePlaced fields below (hand-editable directly
///      in the Scene view, no Play needed — see MainMenuUIBaker's class doc).
///   2. The baked prefab (Assets/Resources/MainMenuPrefabs/*.prefab),
///      instantiated at runtime — hand-editable in Prefab Mode.
///   3. A live fallback build via the same shell builders MainMenuUIBaker
///      calls to produce those prefabs in the first place, so this never
///      breaks before the bake tool has been run.
///
/// Whichever source wins, this class does none of the actual construction —
/// it only wires behavior onto the result: click handlers, MenuButtonFX.Init(),
/// OpenLevelSelectOnLoad, and slide-transition logic.
///
/// The menu renders as a flat Screen Space - Overlay canvas — no camera-
/// dependent perspective tilt, no cursor-parallax drift — so what you see in
/// the Scene view without pressing Play is exactly what Play shows.
/// </summary>
public class MainMenuBootstrap : MonoBehaviour
{
    // Set by LevelCompletePanel's Continue callback (TutorialManager /
    // Level2Guide / Level3Guide) before loading this scene — same "static field
    // consumed on next load" idiom as LevelManager.PendingLevelNumber /
    // TutorialManager.SkipOnNextLoad. Landing a finished level directly back
    // on Level Select (not the main menu root) is the whole point of the
    // Level Complete prompt.
    public static bool OpenLevelSelectOnLoad;

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "SampleScene";

    [Header("Look")]
    [SerializeField] private Color backgroundColor = new Color(0.07f, 0.08f, 0.12f); // matches GameBootstrap's play-scene camera color
    [SerializeField] private Color buttonTextColor = Color.white;

    [Header("Pre-placed content (optional — leave empty to resolve automatically)")]
    [SerializeField] private Canvas canvas;
    [Tooltip("Drag a pre-placed MainMenuShell prefab instance (under the Canvas) here.")]
    [SerializeField] private MainMenuShellView shellView;
    [Tooltip("Drag a pre-placed LevelSelectShell prefab instance (under the Canvas) here.")]
    [SerializeField] private LevelSelectManager levelSelectPrePlaced;

    // Container box look for the runtime-fallback level-select build path
    // only — mirrors LevelSelectShellBuilder/MainMenuUIBaker's own defaults.
    // A baked or pre-placed shell already has these baked into its sprites.
    static readonly Color ContainerFill   = new Color(0.055f, 0.07f, 0.11f);
    static readonly Color ContainerBorder = new Color(0.14f, 0.32f, 0.50f);
    static readonly Color ContainerTrim   = new Color(0.02f, 0.03f, 0.05f);
    const int ContainerBorderThickness = 5;
    const int ContainerChamfer         = 34;
    const int ContainerPixelScale      = 6;

    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    TMP_FontAsset menuFont;
    MainMenuShellView shell;
    SlidePanel menuSlide;
    LevelSelectManager levelSelect;

    void Awake()
    {
        menuFont = Resources.Load<TMP_FontAsset>(FontResourcePath);
        SetupCamera();
        SetupEventSystem();
        SetupAudio();
        ResolveCanvas();
        ResolveShell();
        ResolveLevelSelect();

        menuSlide = shell.Slide;

        WireButton(shell.Play, OnPlayClicked);
        WireButton(shell.Tutorial, () => OnStubClicked("Tutorial"));
        WireButton(shell.Settings, () => SettingsPanelManager.Instance?.OpenFullScreen());
        WireButton(shell.Feedback, () => OnStubClicked("Feedback"));
        WireButton(shell.SkipTutorial, OnSkipTutorialClicked);

        levelSelect.Build(menuFont, buttonTextColor, gameSceneName, ShowMainMenu);

        if (OpenLevelSelectOnLoad)
        {
            OpenLevelSelectOnLoad = false;
            shell.gameObject.SetActive(false);
            levelSelect.Open();
        }
        else
        {
            // Menu's first appearance also plays as a slide-in, not just the
            // transitions between screens.
            menuSlide.SlideIn();
        }
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

        // Blank background per design — solid color, no skybox/imagery. The
        // Canvas's own full-screen Background image (see
        // MainMenuCanvasBuilder) is what's actually visible; this is just a
        // safety-net fallback behind it.
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
    }

    void SetupEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();
        }
    }

    // ── Audio / Settings ─────────────────────────────────────────────────────
    // GameBootstrap sets these up for the gameplay scene (SetupManagers/SetupUI);
    // the main menu needs its own copies since managers aren't carried across
    // scene loads anywhere in this project (see MusicManager's doc comment) and
    // the Settings button here needs SettingsPanelManager.Instance to exist.

    void SetupAudio()
    {
        EnsureComponent<AudioManager>("AudioManager");
        EnsureComponent<MusicManager>("MusicManager");
        MusicManager.Instance.Play(MusicId.MainMenu);
        EnsureComponent<SettingsPanelManager>("SettingsPanelManager");
    }

    static GameObject EnsureComponent<T>(string goName) where T : MonoBehaviour
    {
        var existing = Object.FindAnyObjectByType<T>();
        if (existing != null) return existing.gameObject;
        var go = new GameObject(goName);
        go.AddComponent<T>();
        return go;
    }

    // ── Canvas / shell resolution ────────────────────────────────────────────

    void ResolveCanvas()
    {
        if (canvas == null)
            canvas = MainMenuCanvasBuilder.Build(transform, backgroundColor);

        // No-op for Screen Space - Overlay, but required if the Canvas is
        // switched to Screen Space - Camera (e.g. by hand, for a diegetic
        // tilt effect) — keeps it pointed at whichever camera SetupCamera()
        // actually ends up using, whether that's a pre-placed one or one it
        // just created, instead of silently rendering through a stale/wrong
        // camera reference.
        canvas.worldCamera = Camera.main;
    }

    void ResolveShell()
    {
        if (shellView != null) { shell = shellView; return; }

        var prefab = Resources.Load<GameObject>("MainMenuPrefabs/MainMenuShell");
        GameObject instance = prefab != null
            ? Instantiate(prefab, canvas.transform, false)
            : MainMenuShellBuilder.Build(canvas.GetComponent<RectTransform>(), menuFont, buttonTextColor).gameObject;

        shell = instance.GetComponent<MainMenuShellView>();
    }

    void ResolveLevelSelect()
    {
        if (levelSelectPrePlaced != null) { levelSelect = levelSelectPrePlaced; return; }

        var prefab = Resources.Load<GameObject>("MainMenuPrefabs/LevelSelectShell");
        GameObject instance;
        if (prefab != null)
        {
            instance = Instantiate(prefab, canvas.transform, false);
        }
        else
        {
            var view = LevelSelectShellBuilder.Build(canvas.transform, menuFont, buttonTextColor,
                ContainerFill, ContainerBorder, ContainerTrim,
                ContainerBorderThickness, ContainerChamfer, ContainerPixelScale);
            instance = view.gameObject;
        }

        levelSelect = instance.GetComponent<LevelSelectManager>();
    }

    // A shell button's Fx.Init()/onClick wiring happens here, once it lives
    // under a real Canvas — see MainMenuShellBuilder's class doc for why it
    // can't happen at build/bake time.
    static void WireButton(MenuButtonRefs refs, System.Action onClick)
    {
        if (refs?.Button == null) return;
        refs.Fx?.Init();
        refs.Button.onClick.AddListener(() =>
        {
            if (refs.Fx != null) refs.Fx.PlayClickThen(onClick);
            else onClick?.Invoke();
        });
    }

    void ShowMainMenu()
    {
        shell.gameObject.SetActive(true);
        menuSlide.SlideIn();
    }

    // ── Events ───────────────────────────────────────────────────────────────

    void OnPlayClicked() => OpenLevelSelect(unlockAll: false);

    // Tutorial/Feedback are clickable but intentionally not wired up yet.
    // Settings now opens SettingsPanelManager.OpenFullScreen() instead (see Awake).
    void OnStubClicked(string label)
    {
        Debug.Log($"[MainMenu] {label} clicked — not implemented yet.");
    }

    // Temporary dev button (see MainMenuShellBuilder) — same flow as Play, but arms
    // TutorialManager to skip itself the instant the gameplay scene loads,
    // and opens level select with every level force-unlocked (see
    // LevelSelectManager.Open) so testers can jump straight to any level
    // without playing/completing the ones before it.
    void OnSkipTutorialClicked()
    {
        TutorialManager.SkipOnNextLoad = true;
        OpenLevelSelect(unlockAll: true);
    }

    // Shared by Play and the dev Skip-Tutorial button — click FX already
    // played (see WireButton) by the time this runs. Slides the menu out,
    // then hands off to level select, which slides itself in.
    void OpenLevelSelect(bool unlockAll)
    {
        menuSlide.SlideOut(() =>
        {
            shell.gameObject.SetActive(false);
            levelSelect.Open(unlockAll);
        });
    }
}
