using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The 2D, screen-space counterpart of WorldPulseMarker — the same "look
/// here" read (a pixel-art mouse beside the target acting out what to do
/// there — see MouseGesture — plus a staggered pair of expanding, fading
/// pulses around it, one sent out per press, dropping in with the same
/// blueprint-blue → marker-yellow tint sweep) but for a UI element instead of
/// a spot in the 3D world: a Command Builder chip, the Command Queue, a
/// sidebar button. The mouse is drawn the same way as WorldPulseMarker's
/// (see MarkerGlyphs.UiMouse), so the two kinds of marker read as one family.
///
/// Where the mouse sits isn't fixed: every frame it scores the four sides of
/// the target against ScreenOccupancy (panels, HUD buttons, reservations) and
/// the screen edges, plus an optional "keep clear of this direction" (see
/// Show's avoidDirection — DragPathGuide passes the direction its trail
/// leaves in, so the mouse never sits on top of the trail), and uses the
/// least-covered side — with hysteresis, so it doesn't flip-flop while the
/// target bobs. It always stays upright, pressing toward the target from
/// whichever side it's on. A chip inside the CLI's command builder therefore
/// gets its mouse from OUTSIDE the builder instead of on top of the next chip.
///
/// Lives on a ScreenSpaceOverlay canvas (TutorialUIManager's Guide_Canvas);
/// nothing in it ever blocks a raycast.
/// </summary>
public class UIPulseMarker : MonoBehaviour, IScreenOccupant
{
    enum Side { Above, Right, Left, Below }

    static readonly Side[] Sides = { Side.Above, Side.Right, Side.Left, Side.Below };
    // Its own low-res pixel glyph (~3 canvas units per cell, the HUD plates'
    // density) rather than the world mouse's finer one — see MarkerGlyphs.
    static MouseGlyph Mouse => MarkerGlyphs.UiMouse;

    // Same defaults as WorldPulseMarker, so a UI target and a world target
    // on the same step read as the same kind of "go here" marker.
    static readonly Color DefaultColor  = new Color(1f, 0.92f, 0.05f, 1f);
    static readonly Color BlueprintTint = new Color(0.15f, 0.45f, 1f, 1f);
    const float ButtonIdleShade = 0.5f;  // WorldPulseMarker.buttonIdleShade

    const float MouseHeight    = 40f;   // canvas units, the glyph itself (outline excluded)
    const float MouseGap       = 6f;    // mouse edge → target edge
    const float BobAmount      = 3f;
    const float PressDip       = 6f;    // toward the target while the button is pressed
    const float PulseSpeed     = 2.2f;  // WorldPulseMarker.pulseSpeed
    const float MinAlpha = 0.8f, MaxAlpha = 1f;

    // The glyph's on-screen footprint — what the side choice and screen
    // clamping work with (the Image itself is square, with clear margins).
    static Vector2 MouseFootprint => new Vector2(MouseHeight * Mouse.Aspect, MouseHeight);

    const float RingLoopDuration = 1.8f; // WorldPulseMarker.ringLoopDuration
    const float RingStartPad     = 3f;
    const float RingEndPad       = 20f;
    const float RingMaxAlpha     = 0.85f;
    const int   PulseCount       = 2;

    const float DropDistance = 48f;
    const float DropDuration = 0.45f;
    const float FadeSpeed    = 6f;       // suppression fade, 1/seconds

    RectTransform canvasRect;
    RectTransform mouseRt;
    Image mouseImg;
    Image buttonImg;   // the gesture's lit button, stacked exactly on the body
    Image glowImg;
    RectTransform[] ringRts;
    Image[] ringImgs;

    GuideAnchor target;
    RectTransform lastTargetRect; // what `target` resolved to last frame — see LateUpdate
    Vector2? avoidDirection;
    MouseGesture gesture;
    Side side;
    bool hasSide;
    Vector2 lastTargetCenter;
    bool hasLastTargetCenter;
    bool suppressed;
    float visibility;   // 0..1, eased toward suppressed ? 0 : 1
    float spawnTime;
    Rect mouseScreenRect;
    bool mouseShown;

    readonly List<ScreenOccupancy.Entry> occupied = new();

    public OccupantLayer Layer => OccupantLayer.Guide;
    public bool IsShowing => gameObject.activeSelf;

    /// <summary>Builds a marker under `canvas` (a ScreenSpaceOverlay canvas's
    /// root RectTransform). Starts hidden.</summary>
    public static UIPulseMarker Create(RectTransform canvas, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        var m = go.AddComponent<UIPulseMarker>();
        m.Build(canvas);
        return m;
    }

    void Build(RectTransform canvas)
    {
        canvasRect = canvas;
        var root = (RectTransform)transform;
        Stretch(root);

        ringRts = new RectTransform[PulseCount];
        ringImgs = new Image[PulseCount];
        var ringSprite = PlateSpriteBuilder.GetSprite(new Color32(0, 0, 0, 0), new Color32(255, 255, 255, 255),
            new Color32(0, 0, 0, 0), borderThickness: 2, chamfer: 5, pixelScale: 3);
        for (int i = 0; i < PulseCount; i++)
        {
            var img = MakeImage($"PulseRing{i}", ringSprite);
            img.type = Image.Type.Sliced;
            // Black rim on both sides of the yellow stroke, same idea as the
            // outline drawn around the mouse (see MarkerGlyphs) — keeps the
            // pulse readable over a light chip or bright panel. Offset by one
            // of the plate's own logical pixels (pixelScale 3), so the rim
            // lands on the same pixel grid. Fades with the ring (useGraphicAlpha).
            var rim = img.gameObject.AddComponent<Outline>();
            rim.effectColor = Color.black;
            rim.effectDistance = new Vector2(3f, 3f);
            ringImgs[i] = img;
            ringRts[i] = img.rectTransform;
        }

        glowImg = MakeImage("MouseGlow", RadialGlowSprite.Get());
        mouseImg = MakeImage("Mouse", Mouse.Body);
        mouseRt = mouseImg.rectTransform;
        mouseRt.sizeDelta = Vector2.one * (MouseHeight / Mouse.Span);
        // Child of the body (drawn after it, same rect), so it follows the
        // body's position for free.
        buttonImg = MakeImage("MouseButton", Mouse.LeftButton);
        var buttonRt = buttonImg.rectTransform;
        buttonRt.SetParent(mouseRt, false);
        buttonRt.anchorMin = Vector2.zero;
        buttonRt.anchorMax = Vector2.one;
        buttonRt.offsetMin = Vector2.zero;
        buttonRt.offsetMax = Vector2.zero;

        gameObject.SetActive(false);
    }

    Image MakeImage(string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero; // positioned in canvas units from the bottom-left
        rt.pivot = new Vector2(0.5f, 0.5f);
        return img;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Points the marker at a UI anchor, its mouse acting out `gesture`.
    /// avoidDirection (screen space) is a direction the mouse should NOT sit on —
    /// e.g. the way a drag trail leaves this target. Re-showing the same target
    /// keeps the running animation instead of restarting the drop-in.</summary>
    public void Show(GuideAnchor anchor, Vector2? avoidDirection = null, MouseGesture gesture = MouseGesture.Click)
    {
        this.avoidDirection = avoidDirection;
        this.gesture = gesture;
        buttonImg.sprite = Mouse.Button(gesture);
        bool restart = !gameObject.activeSelf;
        target = anchor;
        if (restart)
        {
            spawnTime = Time.unscaledTime;
            hasSide = false;
            hasLastTargetCenter = false;
            lastTargetRect = null;
            visibility = suppressed ? 0f : 1f;
            gameObject.SetActive(true);
        }
    }

    public void SetAvoidDirection(Vector2? dir) => avoidDirection = dir;

    /// <summary>Fades the marker out (true) or back in (false) without
    /// forgetting its target — used while the player is mid-drag.</summary>
    public void SetSuppressed(bool on) => suppressed = on;

    public void Hide()
    {
        gameObject.SetActive(false);
        mouseShown = false;
    }

    void OnEnable() => ScreenOccupancy.Register(this);
    void OnDisable() => ScreenOccupancy.Unregister(this);

    public void CollectScreenRects(List<Rect> into)
    {
        if (mouseShown && visibility > 0.1f) into.Add(mouseScreenRect);
    }

    void LateUpdate()
    {
        visibility = Mathf.MoveTowards(visibility, suppressed ? 0f : 1f, FadeSpeed * Time.unscaledDeltaTime);

        RectTransform rt = target.ResolveRect();
        // An anchor that moves on to a different element mid-show (a CLI
        // parameter box filled in, so it's Run's turn) drops in fresh there —
        // a new beat, not a slide across the panel.
        if (rt != null && rt != lastTargetRect)
        {
            if (lastTargetRect != null)
            {
                spawnTime = Time.unscaledTime;
                hasSide = false;
                hasLastTargetCenter = false;
            }
            lastTargetRect = rt;
        }
        if (rt == null || visibility <= 0f)
        {
            SetPartsActive(false);
            return;
        }
        SetPartsActive(true);

        float scale = canvasRect.lossyScale.x > 0f ? canvasRect.lossyScale.x : 1f;
        Rect screen = ScreenOccupancy.ScreenRectOf(rt);
        Rect local = new Rect(screen.position / scale, screen.size / scale); // canvas units, bottom-left origin

        float since = Time.unscaledTime - spawnTime;
        float t = Mathf.Sin(Time.unscaledTime * PulseSpeed) * 0.5f + 0.5f;

        ChooseSide(screen, scale);
        UpdateMouse(local, since, t, scale);
        UpdateRings(local, since);
    }

    void SetPartsActive(bool on)
    {
        if (mouseImg.gameObject.activeSelf == on) return;
        mouseImg.gameObject.SetActive(on);
        glowImg.gameObject.SetActive(on);
        foreach (var r in ringImgs) r.gameObject.SetActive(on);
        if (!on) mouseShown = false;
    }

    // ── Side choice ─────────────────────────────────────────────────────────

    void ChooseSide(Rect targetScreen, float scale)
    {
        occupied.Clear();
        ScreenOccupancy.Collect(occupied, OccupantLayer.Hud | OccupantLayer.Panel | OccupantLayer.Reservation);

        Vector2 mousePx = MouseFootprint * scale;
        float mouseArea = mousePx.x * mousePx.y;

        Side best = Side.Above;
        float bestScore = float.MaxValue;
        float currentScore = float.MaxValue;
        for (int i = 0; i < Sides.Length; i++)
        {
            Side s = Sides[i];
            // Pulled back on-screen the same way UpdateMouse does it, so a
            // mouse a few pixels past the edge (a panel clamped flush against
            // it) still counts as usable — only overlapping its own target
            // as a result costs extra.
            Rect r = ClampToScreen(MouseRect(targetScreen, s, mousePx, MouseGap * scale));
            float score = ScreenOccupancy.OverlapArea(r, occupied)
                        + ScreenOccupancy.OverlapArea(r, targetScreen) * 3f
                        + OffscreenArea(r) * 4f
                        + i * mouseArea * 0.05f; // mild preference order: above, right, left, below
            if (avoidDirection.HasValue && Vector2.Dot(Normal(s), avoidDirection.Value) > 0.35f)
                score += mouseArea * 1.5f;

            if (score < bestScore) { bestScore = score; best = s; }
            if (hasSide && s == side) currentScore = score;
        }

        // Hysteresis — only move off the current side for a clear improvement,
        // so the idle bob of a floating panel can't make the mouse flip-flop.
        // Not while the target is still travelling, though (e.g. the CLI panel
        // sliding into place as the camera focuses): a side picked mid-slide
        // would otherwise stick after it lands somewhere that side is bad.
        bool moving = hasLastTargetCenter && (targetScreen.center - lastTargetCenter).magnitude > 2f * scale;
        lastTargetCenter = targetScreen.center;
        hasLastTargetCenter = true;
        if (!hasSide || moving || bestScore < currentScore - mouseArea * 0.25f)
        {
            side = best;
            hasSide = true;
        }
    }

    // The mouse's footprint parked `gap` off the target's `s` edge. Its extent
    // along the side's normal is its width for Left/Right and its height for
    // Above/Below — it stays upright on every side, unlike the old arrow,
    // which rotated to point back at the target.
    static Rect MouseRect(Rect target, Side s, Vector2 size, float gap)
    {
        Vector2 n = Normal(s);
        Vector2 edge = target.center + Vector2.Scale(n, target.size * 0.5f);
        Vector2 c = edge + n * (gap + HalfExtentAlong(n, size));
        return new Rect(c - size * 0.5f, size);
    }

    static float HalfExtentAlong(Vector2 axisNormal, Vector2 size) =>
        Mathf.Abs(axisNormal.x) * size.x * 0.5f + Mathf.Abs(axisNormal.y) * size.y * 0.5f;

    static float OffscreenArea(Rect r)
    {
        var screenRect = new Rect(0, 0, Screen.width, Screen.height);
        return r.width * r.height - ScreenOccupancy.OverlapArea(r, screenRect);
    }

    static Rect ClampToScreen(Rect r) => new Rect(
        Mathf.Clamp(r.x, 0f, Mathf.Max(0f, Screen.width - r.width)),
        Mathf.Clamp(r.y, 0f, Mathf.Max(0f, Screen.height - r.height)),
        r.width, r.height);

    static Vector2 Normal(Side s) => s switch
    {
        Side.Above => Vector2.up,
        Side.Below => Vector2.down,
        Side.Left  => Vector2.left,
        _          => Vector2.right,
    };

    // ── Animation ───────────────────────────────────────────────────────────

    void UpdateMouse(Rect local, float since, float t, float scale)
    {
        float dropT = Mathf.Clamp01(since / DropDuration);
        float landed = 1f - (1f - dropT) * (1f - dropT); // same ease-out as WorldPulseMarker's drop-in

        // One press per ring, u = 0 as a fresh ring leaves — same timing as
        // WorldPulseMarker (see MarkerGlyphs.GesturePress); held off until landed.
        float cycle = since / (RingLoopDuration / PulseCount);
        float press = dropT < 1f ? 0f : MarkerGlyphs.GesturePress(gesture, cycle - Mathf.Floor(cycle));

        Vector2 n = Normal(side);
        Vector2 size = MouseFootprint;
        // No scale heartbeat (unlike the old world arrow's) — a point-filtered
        // pixel glyph resized every frame shimmers as its cells snap between
        // sizes; the bob, the press dip toward the target and the alpha pulse
        // carry the motion instead.
        float offset = MouseGap + HalfExtentAlong(n, size) + Mathf.Lerp(0f, BobAmount, t) - PressDip * press
                     + (1f - landed) * DropDistance;
        Vector2 edge = local.center + Vector2.Scale(n, local.size * 0.5f);
        Vector2 pos = edge + n * offset;
        // Keep it on-screen (eating into the gap/bob instead) — see ChooseSide.
        Vector2 canvasSize = canvasRect.rect.size;
        Vector2 half = size * 0.5f;
        pos = new Vector2(
            Mathf.Clamp(pos.x, half.x, Mathf.Max(half.x, canvasSize.x - half.x)),
            Mathf.Clamp(pos.y, half.y, Mathf.Max(half.y, canvasSize.y - half.y)));

        mouseRt.anchoredPosition = pos;
        // The glyph is MouseHeight tall; its texture is square, with a clear
        // margin for the black outline ring (see MarkerGlyphs.Span), so the
        // Image is that much bigger — the glyph is centered in it.
        mouseRt.sizeDelta = Vector2.one * (MouseHeight / Mouse.Span);

        // Same palette as WorldPulseMarker: the body in the marker color, the
        // gesture's button a darker shade of it between presses and white on
        // each press. A screen-space canvas gets no bloom, so the glow behind
        // the mouse flares with the press instead.
        Color tint = Color.Lerp(BlueprintTint, DefaultColor, landed);
        float alpha = Mathf.Lerp(MinAlpha, MaxAlpha, t) * Mathf.Lerp(0.4f, 1f, landed) * visibility;
        Color body = tint;
        body.a = alpha;
        mouseImg.color = body;
        Color button = Color.Lerp(tint * ButtonIdleShade, Color.white, press);
        button.a = alpha;
        buttonImg.color = button;

        var glowRt = glowImg.rectTransform;
        glowRt.anchoredPosition = pos;
        glowRt.sizeDelta = Vector2.one * MouseHeight * 1.9f;
        Color glow = tint;
        glow.a = Mathf.Lerp(Mathf.Lerp(0.12f, 0.3f, t), 0.6f, press) * landed * visibility;
        glowImg.color = glow;

        mouseScreenRect = new Rect((pos - half) * scale, size * scale);
        mouseShown = true;
    }

    void UpdateRings(Rect local, float since)
    {
        for (int i = 0; i < PulseCount; i++)
        {
            float phase = since / RingLoopDuration + (float)i / PulseCount;
            float loopT = phase - Mathf.Floor(phase);

            float pad = Mathf.Lerp(RingStartPad, RingEndPad, loopT);
            ringRts[i].anchoredPosition = local.center;
            ringRts[i].sizeDelta = local.size + Vector2.one * pad * 2f;

            Color c = DefaultColor;
            c.a = Mathf.Lerp(RingMaxAlpha, 0f, loopT) * visibility;
            ringImgs[i].color = c;
        }
    }
}
