using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum NotifyKind { Info, Success, Warning, Danger }

/// <summary>
/// Owns a single transient top-center banner for one-off event feedback
/// (ping results, objective complete, etc.) — the kind of message
/// NetworkUIManager's status bar can't show because it's overwritten every
/// frame by mode-checking logic in NetworkUIManager.Update().
/// </summary>
public class NotificationManager : MonoBehaviour
{
    public static NotificationManager Instance { get; private set; }

    const float FadeTime = 0.25f;
    const float HoldTime = 2.5f;
    const float RestY    = -20f;
    const float HiddenY  = 10f;

    RectTransform bannerRect;
    CanvasGroup   bannerGroup;
    Image         bannerBg;
    TextMeshProUGUI bannerLabel;
    Coroutine     activeRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        BuildCanvas();
    }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("Notification_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 7; // between TaskUIManager (5) and NetworkUIManager's HUD (10)

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();

        var banner = new GameObject("Banner");
        banner.transform.SetParent(canvasGo.transform, false);

        bannerGroup       = banner.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 0f;
        bannerGroup.blocksRaycasts = false;
        bannerGroup.interactable   = false;

        bannerBg = banner.AddComponent<Image>();
        bannerBg.color = GameTheme.Info;

        bannerRect = banner.GetComponent<RectTransform>();
        bannerRect.anchorMin        = new Vector2(0.5f, 1f);
        bannerRect.anchorMax        = new Vector2(0.5f, 1f);
        bannerRect.pivot            = new Vector2(0.5f, 1f);
        bannerRect.anchoredPosition = new Vector2(0, HiddenY);
        bannerRect.sizeDelta        = new Vector2(480, 40);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(banner.transform, false);
        bannerLabel           = labelGo.AddComponent<TextMeshProUGUI>();
        bannerLabel.fontSize  = 15;
        bannerLabel.alignment = TextAlignmentOptions.Center;
        bannerLabel.color     = Color.white;
        bannerLabel.fontStyle = FontStyles.Bold;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(14, 0);
        lrt.offsetMax = new Vector2(-14, 0);
    }

    public void Notify(string message, NotifyKind kind = NotifyKind.Info)
    {
        if (bannerLabel == null) return; // BuildCanvas hasn't run yet (called before Start)

        bannerLabel.text = message;
        bannerBg.color   = ColorFor(kind);

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(ShowRoutine());
    }

    static Color ColorFor(NotifyKind kind) => kind switch
    {
        NotifyKind.Success => GameTheme.Success,
        NotifyKind.Warning => GameTheme.Warning,
        NotifyKind.Danger  => GameTheme.Danger,
        _                  => GameTheme.Info,
    };

    IEnumerator ShowRoutine()
    {
        yield return Slide(HiddenY, RestY, FadeTime, 0f, 1f);
        yield return new WaitForSeconds(HoldTime);
        yield return Slide(RestY, HiddenY, FadeTime, 1f, 0f);
        activeRoutine = null;
    }

    IEnumerator Slide(float fromY, float toY, float duration, float fromAlpha, float toAlpha)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            bannerRect.anchoredPosition = new Vector2(0, Mathf.Lerp(fromY, toY, k));
            bannerGroup.alpha           = Mathf.Lerp(fromAlpha, toAlpha, k);
            yield return null;
        }
        bannerRect.anchoredPosition = new Vector2(0, toY);
        bannerGroup.alpha           = toAlpha;
    }
}
