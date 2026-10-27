using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A single understated, movie-style subtitle line at the bottom-center of
/// the screen ("PINGING...", "SENDING HTTP REQUEST...") shown while the player
/// is waiting on network activity to play out — as opposed to
/// NotificationManager's toast, which reports a one-off result after the fact.
///
/// Keyed by owner so several independent sources (the ping tool, the packet
/// system, anything added later) can each post and clear their own line
/// without stomping on each other: Show(owner, text) posts or updates that
/// owner's line, Hide(owner) clears it, and whichever still-active line was
/// posted most recently is the one on screen. Updating an owner's existing
/// line keeps its place rather than jumping it back to the front, so a source
/// that re-derives its line every frame (PacketManager) doesn't permanently
/// bury a line someone else posted after it.
///
/// Owners that are UnityEngine.Objects are also lifetime-bound: once one is
/// destroyed its line is dropped automatically, so a source can tie its line
/// to the object that represents the activity itself (PingToolManager keys
/// each ping by its PingPacketEffect journey) and there's no Hide call to
/// forget or to be skipped by an activity cut short mid-flight.
///
/// Text is posted without trailing dots — this appends its own animated
/// "." / ".." / "..." (unscaled time) so every line reads as "still waiting".
/// The not-yet-shown dots are rendered fully transparent rather than omitted,
/// so the centered line keeps a constant width instead of jittering sideways
/// as the dots cycle.
///
/// Purely informational: nothing here is a raycast target and its canvas has
/// no GraphicRaycaster, so it can never eat a click meant for the 3D scene.
/// </summary>
public class ActivitySubtitle : MonoBehaviour
{
    public static ActivitySubtitle Instance { get; private set; }

    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    const float FadeSeconds    = 0.18f;
    const float DotStepSeconds = 0.4f;
    const int   MaxDots        = 3;

    // Clearance above whatever bottom-center band the HUD reserves (see
    // NetworkUIManager.BottomCenterReservedHeight) — the Run Network pill
    // stays up for the whole of a run, which is exactly when this shows.
    const float GapAboveHud = 16f;

    const float PlateHeight = 38f;
    const float PaddingX    = 22f;
    const float MinWidth    = 220f;
    // Capped so even a long line stays well clear of NotificationManager's
    // bottom-right toast (400 wide, 20 in from the right edge): centered on a
    // canvas that's always at least 1920 units wide (UIScale's Expand match),
    // 900 units spans at most 510..1410 against the toast's 1500+.
    const float MaxWidth    = 900f;
    const float FontSize    = 18f;

    // Same chamfered PlateSpriteBuilder plate every other small panel in the
    // game uses (CLIPanelManager/DeviceAddressPopup's SmallPlate proportions),
    // but kept deliberately quiet: GameTheme.PanelBg's navy at partial alpha
    // with a muted slate border, so it reads as a subtitle backing rather
    // than another panel competing with the HUD.
    static readonly Color PlateFill   = new Color(0.06f, 0.08f, 0.13f, 0.72f);
    static readonly Color PlateBorder = new Color(0.22f, 0.28f, 0.38f, 0.85f);
    static readonly Color PlateTrim   = new Color(0.02f, 0.03f, 0.05f, 0.85f);
    static readonly Color TextColor   = new Color(0.90f, 0.94f, 1.00f, 1f);

    // Posting order, oldest first — the last entry is the one on screen.
    readonly List<(object owner, string text)> lines = new();

    GameObject      plateGo;
    RectTransform   plateRect;
    CanvasGroup     plateGroup;
    TextMeshProUGUI label;

    string renderedText;
    int    renderedDots = -1;
    float  dotClock;
    bool   wasShowing;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        BuildCanvas();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Posts owner's line, or updates its text in place if owner
    /// already has one (keeping its current place in the posting order). A
    /// null/empty text is the same as Hide(owner), so a source that derives
    /// its line from live state can post it unconditionally every frame.
    /// Safe to call before this component has built its UI — lines are just
    /// recorded until then.</summary>
    public void Show(object owner, string text)
    {
        if (owner == null) return;
        if (string.IsNullOrEmpty(text)) { Hide(owner); return; }

        int i = IndexOf(owner);
        if (i < 0) lines.Add((owner, text));
        else if (lines[i].text != text) lines[i] = (owner, text);
    }

    /// <summary>Clears owner's line, if it has one. The subtitle fades out
    /// once no line is left (or falls back to the next most recent one).</summary>
    public void Hide(object owner)
    {
        int i = IndexOf(owner);
        if (i >= 0) lines.RemoveAt(i);
    }

    int IndexOf(object owner)
    {
        for (int i = 0; i < lines.Count; i++)
            if (ReferenceEquals(lines[i].owner, owner)) return i;
        return -1;
    }

    // ── Build ────────────────────────────────────────────────────────────────

    void BuildCanvas()
    {
        var canvasGo = new GameObject("ActivitySubtitle_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        // Above NotificationManager's toast (7), below HUD_Canvas (10) — so
        // the HUD's full-screen CRTOverlay scanlines wash over this the same
        // way they do the toast, instead of it looking pasted on top.
        canvas.sortingOrder = 8;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        // Deliberately no GraphicRaycaster — nothing here is ever clickable.

        plateGo = new GameObject("Subtitle", typeof(RectTransform));
        plateGo.transform.SetParent(canvasGo.transform, false);

        plateGroup = plateGo.AddComponent<CanvasGroup>();
        plateGroup.alpha          = 0f;
        plateGroup.blocksRaycasts = false;
        plateGroup.interactable   = false;

        var plateImg = plateGo.AddComponent<Image>();
        plateImg.sprite = PlateSpriteBuilder.GetSprite(PlateFill, PlateBorder, PlateTrim,
            borderThickness: 1, chamfer: 2, pixelScale: 2);
        plateImg.type          = Image.Type.Sliced;
        plateImg.raycastTarget = false;

        plateRect = plateGo.GetComponent<RectTransform>();
        plateRect.anchorMin        = new Vector2(0.5f, 0f);
        plateRect.anchorMax        = new Vector2(0.5f, 0f);
        plateRect.pivot            = new Vector2(0.5f, 0f);
        plateRect.anchoredPosition = new Vector2(0f, NetworkUIManager.BottomCenterReservedHeight + GapAboveHud);
        plateRect.sizeDelta        = new Vector2(MinWidth, PlateHeight);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(plateGo.transform, false);
        label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font             = Resources.Load<TMP_FontAsset>(FontResourcePath);
        label.fontSize         = FontSize;
        label.color            = TextColor;
        label.alignment        = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode     = TextOverflowModes.Ellipsis;
        label.raycastTarget    = false;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(PaddingX, 0f);
        lrt.offsetMax = new Vector2(-PaddingX, 0f);

        plateGo.SetActive(false);
    }

    // ── Per-frame ────────────────────────────────────────────────────────────

    void Update()
    {
        // Lifetime-bound owners (see class comment): a destroyed
        // UnityEngine.Object still compares == null through Unity's overload
        // even though the C# reference held here is non-null.
        for (int i = lines.Count - 1; i >= 0; i--)
            if (lines[i].owner is UnityEngine.Object uo && uo == null) lines.RemoveAt(i);

        if (plateGo == null) return; // Start hasn't built the UI yet

        bool showing = lines.Count > 0;
        if (showing)
        {
            if (!plateGo.activeSelf) plateGo.SetActive(true);

            // Restart the dot cycle from a single "." each time the subtitle
            // appears, but not when the line merely changes text (e.g. an HTTP
            // request turning into its response) — the rhythm stays steady.
            if (!wasShowing) dotClock = 0f;
            dotClock += Time.unscaledDeltaTime;
            int dots = 1 + Mathf.FloorToInt(dotClock / DotStepSeconds) % MaxDots;

            Render(lines[lines.Count - 1].text, dots);
        }
        wasShowing = showing;

        // While fading out, the last line stays rendered as-is rather than
        // blanking — only its alpha changes.
        plateGroup.alpha = Mathf.MoveTowards(plateGroup.alpha, showing ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
        if (!showing && plateGroup.alpha <= 0f && plateGo.activeSelf) plateGo.SetActive(false);
    }

    void Render(string text, int dots)
    {
        bool textChanged = text != renderedText;
        if (!textChanged && dots == renderedDots) return;

        if (textChanged)
        {
            // Sized for the fully-dotted line, so the plate never has to
            // resize as the dots cycle.
            float textWidth = label.GetPreferredValues(text + new string('.', MaxDots)).x;
            plateRect.sizeDelta = new Vector2(Mathf.Clamp(textWidth + PaddingX * 2f, MinWidth, MaxWidth), PlateHeight);
        }

        string hidden = dots < MaxDots ? "<alpha=#00>" + new string('.', MaxDots - dots) : "";
        label.text = text + new string('.', dots) + hidden;

        renderedText = text;
        renderedDots = dots;
    }
}
