using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full-screen loading overlay shown during a scene transition — masks the
/// hitch of loading and initializing a scene's GameBootstrap (procedural
/// device meshes, managers, HUD, ...) behind a screen that fits the rest of
/// the menu's dark-navy / electric-blue look (see GameTheme, RadialGlowSprite)
/// instead of a jarring freeze-then-pop.
///
/// Lives on a DontDestroyOnLoad GameObject created on first use, so it
/// survives the very scene swap it's covering for and is reused for every
/// future transition (level 2+, returning to the menu, ...). Call
/// LoadingScreenManager.LoadScene(...) instead of SceneManager.LoadScene
/// directly wherever a scene change should go through it.
/// </summary>
public class LoadingScreenManager : MonoBehaviour
{
    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    // Never flashes on/off even if the target scene loads almost instantly —
    // the fade + hold always reads as a deliberate loading beat, not a glitch.
    const float MinShowTime = 0.6f;
    const float FadeTime    = 0.25f;

    static LoadingScreenManager instance;

    CanvasGroup canvasGroup;
    TextMeshProUGUI label;
    Image progressFill;

    /// <summary>Loads sceneName behind this overlay — call instead of SceneManager.LoadScene directly.</summary>
    public static void LoadScene(string sceneName, string displayLabel = "LOADING")
    {
        var mgr = GetOrCreate();
        mgr.StartCoroutine(mgr.LoadRoutine(sceneName, displayLabel));
    }

    static LoadingScreenManager GetOrCreate()
    {
        if (instance != null) return instance;

        var go = new GameObject("LoadingScreenManager");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<LoadingScreenManager>();
        instance.Build();
        return instance;
    }

    // ── Build ────────────────────────────────────────────────────────────────

    void Build()
    {
        var canvasGo = new GameObject("LoadingScreen_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder  = 1000; // above every other canvas, in any scene

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();

        canvasGroup = canvasGo.AddComponent<CanvasGroup>();
        canvasGroup.alpha          = 0f;
        canvasGroup.blocksRaycasts = false;

        // Fully opaque backdrop, slightly darker than the menu's own camera
        // clear color — hides the target scene's Awake/Start hitch completely.
        var bgImg = canvasGo.AddComponent<Image>();
        bgImg.color = new Color(0.045f, 0.055f, 0.085f, 1f);
        var bgRt = canvasGo.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        // Soft glow behind the label — same sprite/color language as the menu
        // buttons' click glow (see MenuButtonFX), just stationary and always on.
        var glowGo = new GameObject("Glow", typeof(RectTransform));
        glowGo.transform.SetParent(canvasGo.transform, false);
        var glowImg = glowGo.AddComponent<Image>();
        glowImg.sprite = RadialGlowSprite.Get();
        glowImg.color  = new Color(GameTheme.ElectricBlue.r, GameTheme.ElectricBlue.g, GameTheme.ElectricBlue.b, 0.30f);
        glowImg.raycastTarget = false;
        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin        = new Vector2(0.5f, 0.5f);
        grt.anchorMax        = new Vector2(0.5f, 0.5f);
        grt.pivot            = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = new Vector2(0f, 40f);
        grt.sizeDelta        = new Vector2(560f, 560f);

        var font = Resources.Load<TMP_FontAsset>(FontResourcePath);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(canvasGo.transform, false);
        label             = labelGo.AddComponent<TextMeshProUGUI>();
        label.font        = font;
        label.fontSize    = 40;
        label.color       = Color.white;
        label.alignment   = TextAlignmentOptions.Center;
        label.fontStyle   = FontStyles.Bold;
        label.raycastTarget = false;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin        = new Vector2(0.5f, 0.5f);
        lrt.anchorMax        = new Vector2(0.5f, 0.5f);
        lrt.pivot            = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = new Vector2(0f, 40f);
        lrt.sizeDelta        = new Vector2(900f, 80f);

        // Progress bar: dark track + electric-blue fill, same color as the
        // spark/glow FX elsewhere so the loading screen reads as one piece
        // with the buttons that led into it.
        const float barWidth = 420f, barHeight = 8f;

        var trackGo = new GameObject("ProgressTrack", typeof(RectTransform));
        trackGo.transform.SetParent(canvasGo.transform, false);
        var trackImg = trackGo.AddComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.12f);
        var trt = trackGo.GetComponent<RectTransform>();
        trt.anchorMin        = new Vector2(0.5f, 0.5f);
        trt.anchorMax        = new Vector2(0.5f, 0.5f);
        trt.pivot            = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0f, -30f);
        trt.sizeDelta        = new Vector2(barWidth, barHeight);

        var fillGo = new GameObject("ProgressFill", typeof(RectTransform));
        fillGo.transform.SetParent(trackGo.transform, false);
        progressFill              = fillGo.AddComponent<Image>();
        progressFill.color        = GameTheme.ElectricBlue;
        progressFill.type         = Image.Type.Filled;
        progressFill.fillMethod   = Image.FillMethod.Horizontal;
        progressFill.fillOrigin   = (int)Image.OriginHorizontal.Left;
        progressFill.fillAmount   = 0f;
        progressFill.raycastTarget = false;
        var frt = fillGo.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;
    }

    // ── Load flow ────────────────────────────────────────────────────────────

    IEnumerator LoadRoutine(string sceneName, string displayLabel)
    {
        yield return FadeCanvas(0f, 1f, FadeTime);
        canvasGroup.blocksRaycasts = true;

        float shownAt = Time.unscaledTime;
        var op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        // Holds here until BOTH the load itself has reached its "ready to
        // activate" point AND at least MinShowTime has passed — the actual
        // GameBootstrap hitch happens during activation below, while this
        // overlay is already fully opaque and blocking input.
        while (op.progress < 0.9f || Time.unscaledTime - shownAt < MinShowTime)
        {
            progressFill.fillAmount = Mathf.Clamp01(op.progress / 0.9f);
            UpdateEllipsis(displayLabel);
            yield return null;
        }
        progressFill.fillAmount = 1f;

        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;

        yield return FadeCanvas(1f, 0f, FadeTime);
        canvasGroup.blocksRaycasts = false;
    }

    void UpdateEllipsis(string baseLabel)
    {
        int dots = 1 + Mathf.FloorToInt(Time.unscaledTime * 2f) % 3;
        label.text = baseLabel + new string('.', dots);
    }

    IEnumerator FadeCanvas(float from, float to, float duration)
    {
        canvasGroup.alpha = from;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        canvasGroup.alpha = to;
    }
}
