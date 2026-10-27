using System.Collections;
using System.Collections.Generic;
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
    // consumed on next load" idiom as LevelManager.PendingLevelNumber.
    // Landing a finished level directly back on Level Select (not the main
    // menu root) is the whole point of the Level Complete prompt.
    public static bool OpenLevelSelectOnLoad;

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "SampleScene";

    [Header("Look")]
    [SerializeField] private Color backgroundColor = new Color(0.07f, 0.08f, 0.12f); // matches GameBootstrap's play-scene camera color
    [SerializeField] private Color buttonTextColor = Color.white;

    // Also the fallback when the Inspector field is left blank — an instance
    // already in an open scene keeps whatever value it was first serialized
    // with, so changing the initializer alone wouldn't reach it.
    const string DefaultFeedbackFormUrl = "https://forms.gle/gViWCJ1c8uyxiwfk9";

    [Header("Links")]
    [Tooltip("Opened in the player's browser when Feedback is clicked — a Google Form share link (https://forms.gle/... or https://docs.google.com/forms/.../viewform). Leave blank to use the built-in default.")]
    [SerializeField] private string feedbackFormUrl = DefaultFeedbackFormUrl;

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
    LevelSelectManager levelSelect;
    CanvasGroup backgroundGroup;

    void Awake()
    {
        menuFont = Resources.Load<TMP_FontAsset>(FontResourcePath);
        SetupCamera();
        SetupEventSystem();
        SetupAudio();
        ResolveCanvas();
        ResolveShell();
        ResolveLevelSelect();

        // Topmost layer, added last so it sits above both shells built just
        // above — experimental, see CRTOverlay's class doc for rollback.
        CRTOverlay.Build(canvas.transform);

        // Deferred to here rather than build/bake time — see BuildLogo's
        // comment on why (RadialGlowSprite.Get() can't be baked into a prefab).
        if (shell.LogoGlow != null) shell.LogoGlow.sprite = RadialGlowSprite.Get();

        WireButton(shell.Play, OnPlayClicked);
        WireButton(shell.Settings, OpenSettings);
        WireButton(shell.Feedback, OpenFeedbackForm);
        WireButton(shell.Quit, () => ConfirmDialogManager.Instance?.ShowQuitPrompt());

        levelSelect.Build(menuFont, buttonTextColor, gameSceneName, backgroundGroup, ShowMainMenu);

        if (OpenLevelSelectOnLoad)
        {
            OpenLevelSelectOnLoad = false;
            shell.gameObject.SetActive(false);
            // Background-only beat before Level Select's panel zooms in —
            // same "background first, then the UI" beat GameBootstrap's
            // IntroBlankHold gives the level-entry sequence, so landing back
            // here (e.g. the gameplay Home button) reads consistently with
            // entering a level rather than the panel popping in instantly.
            StartCoroutine(OpenLevelSelectAfterBlankHold());
        }
        else
        {
            // Menu's very first appearance only — returning from Level
            // Select instead plays PlayMenuEnter (see below), the reverse of
            // PlayMenuExit's slide-out. Opens on a black screen that fades
            // into the background, then reveals the logo and each button one
            // at a time via the same glow-flash language as a button click,
            // instead of the whole menu sliding/popping in as one block.
            StartCoroutine(PlayOpeningSequence());
        }
    }

    // ── Opening sequence (first appearance only) ─────────────────────────────
    // Black screen -> fade into the background -> slight delay -> logo ->
    // very fast delay -> buttons (each still staggered by RevealStagger).

    const float OpeningBlackFadeDuration  = 0.5f;
    const float OpeningPostFadeDelay      = 0.25f; // "slight delay" before the logo
    const float OpeningLogoToButtonDelay  = 0.06f; // "very fast delay" before buttons start

    IEnumerator PlayOpeningSequence()
    {
        var blackGroup = BuildOpeningBlackOverlay();
        yield return FadeGroup(blackGroup, 1f, 0f, OpeningBlackFadeDuration);
        Destroy(blackGroup.gameObject);

        yield return new WaitForSecondsRealtime(OpeningPostFadeDelay);
        PlayFirstAppearanceReveal(OpeningLogoToButtonDelay);
    }

    // Full-screen solid black Image, last sibling under the Canvas so it
    // paints over the background/shell/level-select that are already built
    // by this point — built here rather than baked into MainMenuCanvasBuilder
    // so it works the same whether the canvas came from a pre-placed
    // instance, a baked prefab, or the runtime fallback, and so it never
    // lingers: destroyed the instant its fade-out finishes.
    CanvasGroup BuildOpeningBlackOverlay()
    {
        var go = new GameObject("OpeningBlackFade", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsLastSibling();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.color = Color.black;
        img.raycastTarget = true; // blocks clicks on the not-yet-revealed menu while it's covering the screen

        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        return group;
    }

    static IEnumerator FadeGroup(CanvasGroup group, float from, float to, float duration)
    {
        group.alpha = from;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        group.alpha = to;
    }

    // Only ever played on the way back from a level — kept short, since
    // quitting a level is something a player does over and over (see
    // GameBootstrap's own exit timings).
    const float LevelSelectBlankHold = 0.15f;

    IEnumerator OpenLevelSelectAfterBlankHold()
    {
        yield return new WaitForSecondsRealtime(LevelSelectBlankHold);
        levelSelect.Open();
    }

    // ── Menu <-> Level Select transition ─────────────────────────────────────
    // Each element (logo, then Play/Settings/Feedback/Quit) has its own
    // SlidePanel (see MainMenuShellBuilder) — logo slides off/on to the left,
    // buttons to the right — driven one at a time with a slight delay between
    // each instead of the old single whole-MenuRoot SlidePanel.

    const float MenuTransitionStagger = 0.09f;

    List<SlidePanel> MenuTransitionElements()
    {
        var list = new List<SlidePanel>();
        if (shell.LogoSlide != null) list.Add(shell.LogoSlide);
        foreach (var refs in new[] { shell.Play, shell.Settings, shell.Feedback, shell.Quit })
            if (refs?.Slide != null) list.Add(refs.Slide);
        return list;
    }

    // Logo, then each button in order, sliding out of frame one at a time;
    // onComplete (typically hiding this shell and opening Level Select)
    // fires once the last element finishes.
    void PlayMenuExit(System.Action onComplete)
    {
        StartCoroutine(StaggerSlide(MenuTransitionElements(), slideOut: true, onComplete));
    }

    // The exact reverse of PlayMenuExit — same elements, reverse order
    // (buttons first, logo last), each sliding back in instead of out. Used
    // when returning from Level Select (ShowMainMenu); the very first app
    // launch uses PlayFirstAppearanceReveal's glow-flash entrance instead.
    void PlayMenuEnter()
    {
        // PlayFirstAppearanceReveal (the only thing that ever sets these) only
        // runs on a scene load that lands directly on the main menu — a load
        // that instead opens straight to Level Select (OpenLevelSelectOnLoad,
        // e.g. the Home button) skips it entirely, leaving every element
        // parked at its pre-reveal alpha 0 / scale 0.82. Force them to their
        // normal visible state here unconditionally (a no-op if the reveal
        // already ran) so returning to the menu never leaves it invisible —
        // only position needs the staggered slide-in below.
        if (shell.LogoRevealGroup != null) shell.LogoRevealGroup.alpha = 1f;
        if (shell.LogoVisual != null) shell.LogoVisual.localScale = Vector3.one;
        foreach (var refs in new[] { shell.Play, shell.Settings, shell.Feedback, shell.Quit })
        {
            if (refs?.Fx == null) continue;
            if (refs.Fx.revealGroup != null) refs.Fx.revealGroup.alpha = 1f;
            if (refs.VisualRect != null) refs.VisualRect.localScale = Vector3.one;
        }

        var elements = MenuTransitionElements();
        elements.Reverse();
        // Every element snapped off-screen immediately, together — before
        // any of them starts its staggered SlideIn below — see SlidePanel.
        // SnapForSlideIn's doc comment for why: without this, an element
        // that's never been slid out before this session (e.g. every
        // element but the first, whose own staggered SlideIn snap normally
        // handles it) sits fully visible at rest until its own turn comes
        // up, then visibly pops off-screen and slides back — read as "no
        // sliding, then it plays" instead of a clean staggered entrance.
        foreach (var el in elements) el.SnapForSlideIn();
        StartCoroutine(StaggerSlide(elements, slideOut: false, null));
    }

    IEnumerator StaggerSlide(List<SlidePanel> elements, bool slideOut, System.Action onComplete)
    {
        if (elements.Count == 0) { onComplete?.Invoke(); yield break; }

        for (int i = 0; i < elements.Count; i++)
        {
            bool isLast = i == elements.Count - 1;
            System.Action done = isLast ? onComplete : null;
            if (slideOut) elements[i].SlideOut(done);
            else elements[i].SlideIn(done);

            if (!isLast) yield return new WaitForSecondsRealtime(MenuTransitionStagger);
        }
    }

    // ── First-appearance reveal ──────────────────────────────────────────────

    const float RevealStagger = 0.13f; // delay between each element's own MenuRevealFlash (see that class)

    // logoToFirstButtonGap: gap between the logo's reveal and the first
    // button's — defaults to RevealStagger (used by ShowMainMenu's plain
    // re-reveal path, if ever called this way), but PlayOpeningSequence
    // passes OpeningLogoToButtonDelay instead for the "very fast delay"
    // the first-launch opening asks for.
    void PlayFirstAppearanceReveal(float logoToFirstButtonGap = RevealStagger)
    {
        float delay = 0f;

        if (shell.LogoRevealGroup != null)
        {
            StartCoroutine(DelayedLogoReveal(delay));
            delay += logoToFirstButtonGap;
        }

        foreach (var refs in new[] { shell.Play, shell.Settings, shell.Feedback, shell.Quit })
        {
            if (refs?.Fx == null) continue;
            StartCoroutine(DelayedButtonReveal(refs.Fx, delay));
            delay += RevealStagger;
        }
    }

    IEnumerator DelayedButtonReveal(MenuButtonFX fx, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        fx.PlayRevealIn();
    }

    IEnumerator DelayedLogoReveal(float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        StartCoroutine(MenuRevealFlash.Play(shell.LogoRevealGroup, shell.LogoVisual, shell.LogoGlow, GameTheme.ElectricBlue,
            shell.LogoVisual, canvas.transform as RectTransform, Camera.main));
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
        EnsureComponent<ConfirmDialogManager>("ConfirmDialogManager"); // Quit button's confirm prompt
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
        {
            canvas = MainMenuCanvasBuilder.Build(transform, backgroundColor, out backgroundGroup);
        }
        else
        {
            // Pre-placed canvas (hand-dragged into the scene) — find its
            // Background child the same way MainMenuCanvasBuilder names it,
            // upgrading it in place to the pattern/vignette layers if
            // it still has the old flat single-RawImage layout (see
            // MainMenuCanvasBuilder.UpgradeBackground), and adding a
            // CanvasGroup if an older/hand-built one doesn't have one yet,
            // so the fade transition always has something to fade.
            var bg = canvas.transform.Find("Background");
            if (bg != null)
            {
                MainMenuCanvasBuilder.UpgradeBackground(bg, backgroundColor);
                // Not `GetComponent() ?? AddComponent()`: in the Editor a
                // missing component comes back as a fake-null object that
                // `??` doesn't see through, so no group was ever added and
                // every menu → level swap fell back to LoadingScreenManager's
                // flat blip instead of handing off to its background bridge.
                backgroundGroup = bg.GetComponent<CanvasGroup>();
                if (backgroundGroup == null) backgroundGroup = bg.gameObject.AddComponent<CanvasGroup>();
            }
            else
            {
                backgroundGroup = null;
            }
        }

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
        PlayMenuEnter();
    }

    // ── Events ───────────────────────────────────────────────────────────────

    void OnPlayClicked() => OpenLevelSelect();

    // Hands off to the player's default browser — the menu stays where it is
    // behind it, so there's no slide-out transition to play here. Only
    // http(s) links are opened, so a typo'd or blank field can't launch
    // some other protocol handler.
    void OpenFeedbackForm()
    {
        string url = string.IsNullOrWhiteSpace(feedbackFormUrl) ? DefaultFeedbackFormUrl : feedbackFormUrl.Trim();
        if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri)
            || (uri.Scheme != System.Uri.UriSchemeHttps && uri.Scheme != System.Uri.UriSchemeHttp))
        {
            Debug.LogWarning($"[MainMenu] Feedback clicked, but Feedback Form Url \"{url}\" isn't a valid http(s) link.");
            return;
        }
        Application.OpenURL(uri.AbsoluteUri);
    }

    // Click FX already played (see WireButton) by the time this runs. Slides
    // the menu's elements out one by one (see PlayMenuExit), then hands off
    // to level select, which zooms itself in (see LevelSelectManager.Open).
    void OpenLevelSelect()
    {
        PlayMenuExit(() =>
        {
            shell.gameObject.SetActive(false);
            levelSelect.Open();
        });
    }

    // Same hand-off as OpenLevelSelect: the menu's elements slide out, then
    // the full-screen Settings page zooms in over the shared background.
    // Closing it zooms back out and slides the menu back in (ShowMainMenu),
    // exactly like Level Select's Back button.
    void OpenSettings()
    {
        var settings = SettingsPanelManager.Instance;
        if (settings == null) return;

        PlayMenuExit(() =>
        {
            shell.gameObject.SetActive(false);
            settings.OpenFullScreen(ShowMainMenu);
        });
    }
}
