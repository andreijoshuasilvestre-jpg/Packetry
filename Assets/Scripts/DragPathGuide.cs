using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// "Drag it from here to there" guidance: a trail of chevrons along a gentle
/// arc between a DragGuideRoute's two ends, with a bright pulse travelling
/// down it from source to destination on a loop, plus a UIPulseMarker on any
/// end that's a UI element (a world end is already marked by the step's own
/// WorldPulseMarker, so it isn't doubled up here) — its mouse holding the
/// button down at the source and letting go at the destination, so the
/// gesture reads as press, drag, release.
///
/// Ends are GuideAnchors, re-resolved every frame — the trail follows a CLI
/// panel's idle bob, a device being dragged, the camera panning/zooming, and
/// simply disappears while either end can't be resolved (a flyout item before
/// the flyout opens, a palette chip that isn't in the current mode) without
/// the caller having to toggle anything.
///
/// While the route reports the drag is actually in progress (InProgress), the
/// trail and the source marker fade out — the player's already holding the
/// thing, so only the destination marker keeps pointing. They fade back in if
/// the drag ends without completing the step. Once a route with a follow-up
/// reports the drag landed (Done), the trail steps aside for a single
/// clicking-mouse marker on what to press next (Then — the CLI's Run button).
///
/// When the thing to drag is scrolled out of view inside a scroll list (the
/// CLI's command palette, say), there's nothing to draw a trail from — a
/// "SCROLL" hint appears at that list's edge instead, pointing the way, and
/// the trail takes over the moment the item is scrolled into view.
///
/// Drawn on a ScreenSpaceOverlay canvas (TutorialUIManager's Guide_Canvas),
/// raycast-transparent throughout. Registers its visible chevrons with
/// ScreenOccupancy so the tutorial callout doesn't sit on top of the trail.
/// </summary>
public class DragPathGuide : MonoBehaviour, IScreenOccupant
{
    static readonly Color TrailColor = new Color(1f, 0.92f, 0.05f, 1f); // WorldPulseMarker's default yellow

    const int   MaxChevrons     = 18;
    const float ChevronSize     = 36f;  // canvas units — 12 pixel-art cells (see MarkerGlyphs), ~3 units each like the HUD's plates
    const float ChevronSpacing  = 44f;
    const float UiEndGap        = 10f;  // gap between a UI end's edge and the first/last chevron
    const float WorldEndGapMul  = 1.4f; // × on-screen size of half a world unit at a world end
    const int   CurveSamples    = 32;

    const float WavePeriod   = 1.15f; // seconds for the pulse to run the full trail
    const float WaveWidth    = 0.35f; // fraction of the trail the pulse's tail spans
    const float RevealTime   = 0.45f; // chevrons appear source → destination on Show
    const float FadeSpeed    = 5f;

    RectTransform canvasRect;
    readonly List<Image> chevrons = new();
    readonly List<Rect> chevronScreenRects = new();
    readonly Vector2[] samples = new Vector2[CurveSamples + 1];
    readonly float[] sampleLengths = new float[CurveSamples + 1];

    UIPulseMarker fromMarker;
    UIPulseMarker toMarker;
    UIPulseMarker thenMarker; // the route's follow-up click, once the drag has landed — see DragGuideRoute.Done

    // "SCROLL ▼" hint — see ShowScrollHint.
    RectTransform hintRt;
    RectTransform hintArrowRt;
    Image hintArrow;
    Image hintBack;
    TextMeshProUGUI hintLabel;
    const float HintArrowSize = 22f;

    DragGuideRoute route;
    float spawnTime;
    float trailVisibility;
    Camera cam;

    public OccupantLayer Layer => OccupantLayer.Guide;
    public bool IsShowing => route != null;

    /// <summary>Builds a guide (and its two end markers) under `canvas`, a
    /// ScreenSpaceOverlay canvas's root RectTransform. Starts hidden.</summary>
    public static DragPathGuide Create(RectTransform canvas)
    {
        var go = new GameObject("DragPathGuide", typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        var g = go.AddComponent<DragPathGuide>();
        g.Build(canvas);
        return g;
    }

    void Build(RectTransform canvas)
    {
        canvasRect = canvas;
        var root = (RectTransform)transform;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        for (int i = 0; i < MaxChevrons; i++)
        {
            var go = new GameObject($"Chevron{i}", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = MarkerGlyphs.Chevron;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.one * ChevronSize;
            go.SetActive(false);
            chevrons.Add(img);
        }

        BuildScrollHint();

        // Markers after the chevrons, so they draw on top of the trail's ends.
        fromMarker = UIPulseMarker.Create(canvas, "DragGuideFromMarker");
        toMarker = UIPulseMarker.Create(canvas, "DragGuideToMarker");
        thenMarker = UIPulseMarker.Create(canvas, "DragGuideThenMarker");

        gameObject.SetActive(false);
    }

    // A small dark plate reading "SCROLL" with a bobbing pixel arrow beside it,
    // parked on a scroll list's viewport edge. Plate behind so it
    // stays legible over whatever list item it happens to sit on.
    void BuildScrollHint()
    {
        var go = new GameObject("ScrollHint", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        hintRt = go.GetComponent<RectTransform>();
        hintRt.anchorMin = hintRt.anchorMax = Vector2.zero; // canvas units from the bottom-left, like the chevrons
        hintRt.pivot = new Vector2(0.5f, 0.5f);
        hintRt.sizeDelta = new Vector2(136f, 34f);

        hintBack = go.AddComponent<Image>();
        hintBack.sprite = PlateSpriteBuilder.GetSprite(new Color32(10, 12, 18, 235), new Color32(0, 0, 0, 255),
            new Color32(0, 0, 0, 0), borderThickness: 2, chamfer: 3, pixelScale: 3);
        hintBack.type = Image.Type.Sliced;
        hintBack.raycastTarget = false;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        hintLabel = labelGo.AddComponent<TextMeshProUGUI>();
        hintLabel.font = Resources.Load<TMP_FontAsset>("Fonts/PixeloidMono SDF");
        hintLabel.text = "SCROLL";
        hintLabel.fontSize = 16f;
        hintLabel.fontStyle = FontStyles.Bold;
        hintLabel.alignment = TextAlignmentOptions.MidlineLeft;
        hintLabel.raycastTarget = false;
        var lrt = hintLabel.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(14f, 0f);
        lrt.offsetMax = new Vector2(-40f, 0f);

        var arrowGo = new GameObject("Arrow", typeof(RectTransform));
        arrowGo.transform.SetParent(go.transform, false);
        hintArrow = arrowGo.AddComponent<Image>();
        hintArrow.sprite = MarkerGlyphs.UiArrow;
        hintArrow.raycastTarget = false;
        hintArrowRt = hintArrow.rectTransform;
        hintArrowRt.anchorMin = hintArrowRt.anchorMax = new Vector2(1f, 0.5f);
        hintArrowRt.pivot = new Vector2(0.5f, 0.5f);
        hintArrowRt.sizeDelta = Vector2.one * (HintArrowSize / MarkerGlyphs.Span(MarkerGlyphs.UiArrowCells));

        go.SetActive(false);
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Starts guiding along `newRoute`. Re-showing while already shown
    /// restarts the source → destination reveal.</summary>
    public void Show(DragGuideRoute newRoute)
    {
        route = newRoute;
        spawnTime = Time.unscaledTime;
        trailVisibility = 0f;
        arcIndex = -1;
        // Markers restart too — LateUpdate re-shows them on the new route's
        // ends (a marker already showing would otherwise keep its old target).
        HideMarkers();
        thenMarker.Hide();
        gameObject.SetActive(route != null);
    }

    public void Hide()
    {
        route = null;
        HideMarkers();
        thenMarker.Hide();
        hintRt.gameObject.SetActive(false);
        gameObject.SetActive(false);
    }

    void HideMarkers()
    {
        fromMarker.Hide();
        toMarker.Hide();
    }

    void OnEnable() => ScreenOccupancy.Register(this);
    void OnDisable() => ScreenOccupancy.Unregister(this);

    public void CollectScreenRects(List<Rect> into) => into.AddRange(chevronScreenRects);

    // ── Per-frame layout ─────────────────────────────────────────────────────
    // The trail's bow is chosen, not fixed: every frame it tries a handful of
    // arcs (a gentle lob either way, straight, and deeper bows) and takes the
    // one whose chevrons cross the least of whatever's registered with
    // ScreenOccupancy — the tutorial callout's home spot, HUD buttons, open
    // panels — so a route from a sidebar button out to the table swings
    // around the callout sitting between them instead of cutting through its
    // text. Deeper bows cost a little extra, so an unobstructed route stays a
    // gentle lob, and hysteresis keeps a bobbing end from flipping it. A
    // Straight route only ever takes the straight one.
    //
    // All in canvas units (bottom-left origin), same as the chevrons themselves.

    static readonly float[] ArcChoices = { 0.16f, -0.16f, 0f, 0.32f, -0.32f, 0.5f, -0.5f };
    const OccupantLayer AvoidLayers = OccupantLayer.Hud | OccupantLayer.Panel | OccupantLayer.Reservation;

    readonly List<ScreenOccupancy.Entry> occupied = new();
    int arcIndex = -1;

    struct TrailPath
    {
        public bool Valid;
        public Vector2 Start, End, Control;
        public Vector2 StartDir, EndDir; // the way the trail leaves each end, outward from it
    }

    void LateUpdate()
    {
        chevronScreenRects.Clear();
        if (route == null) return;
        if (cam == null) cam = Camera.main;

        // The drag already landed (see DragGuideRoute.Done) — the trail's job
        // is done, so point at what to click next instead. The marker resolves
        // its own anchor every frame, and hides itself while there's nothing to
        // point at.
        if (route.Done != null && route.Done())
        {
            HideChevrons(0);
            HideMarkers();
            hintRt.gameObject.SetActive(false);
            if (!thenMarker.IsShowing) thenMarker.Show(route.Then, gesture: MouseGesture.Click);
            return;
        }
        if (thenMarker.IsShowing) thenMarker.Hide();

        bool inProgress = route.InProgress != null && route.InProgress();
        trailVisibility = Mathf.MoveTowards(trailVisibility, inProgress ? 0f : 1f, FadeSpeed * Time.unscaledDeltaTime);

        bool fromOk = route.From.TryGetScreenRect(cam, out Rect fromScreen, out float fromWorldPx);
        bool toOk = route.To.TryGetScreenRect(cam, out Rect toScreen, out float toWorldPx);
        if (!fromOk || !toOk)
        {
            // Either end missing → the whole route is meaningless right now
            // (don't point at a destination with nothing to drag, or vice versa).
            HideChevrons(0);
            HideMarkers();
            hintRt.gameObject.SetActive(false);
            return;
        }

        float scale = canvasRect.lossyScale.x > 0f ? canvasRect.lossyScale.x : 1f;

        // An end scrolled out of view inside a scroll list — teach scrolling
        // to it instead of drawing a trail to/from something that isn't there.
        if (ShowScrollHint(route.From, scale) || ShowScrollHint(route.To, scale))
        {
            HideChevrons(0);
            HideMarkers();
            return;
        }
        hintRt.gameObject.SetActive(false);

        Rect from = ToCanvas(fromScreen, scale), to = ToCanvas(toScreen, scale);
        float fromGap = fromWorldPx / scale, toGap = toWorldPx / scale;

        occupied.Clear();
        ScreenOccupancy.Collect(occupied, AvoidLayers);
        for (int i = 0; i < occupied.Count; i++)
        {
            var e = occupied[i];
            e.Rect = ToCanvas(e.Rect, scale);
            occupied[i] = e;
        }

        float chevronArea = ChevronSize * ChevronSize;
        int best = -1;
        float bestScore = float.MaxValue, currentScore = float.MaxValue;
        for (int i = 0; i < ArcChoices.Length; i++)
        {
            if (route.Straight && ArcChoices[i] != 0f) continue;
            TrailPath p = BuildPath(from, to, fromGap, toGap, ArcChoices[i]);
            if (!p.Valid) continue;
            float score = PathOverlap(p) + Mathf.Abs(ArcChoices[i]) * chevronArea * 2f;
            if (score < bestScore) { bestScore = score; best = i; }
            if (i == arcIndex) currentScore = score;
        }

        if (best < 0)
        {
            // Ends too close for a trail (or their gaps overlap) — the end
            // markers alone say it.
            Vector2 d = (to.center - from.center).normalized;
            UpdateEndMarker(fromMarker, route.From, d, inProgress, MouseGesture.Hold);
            UpdateEndMarker(toMarker, route.To, -d, false, MouseGesture.Release);
            HideChevrons(0);
            return;
        }

        if (currentScore == float.MaxValue || bestScore < currentScore - chevronArea * 0.5f) arcIndex = best;
        TrailPath path = BuildPath(from, to, fromGap, toGap, ArcChoices[arcIndex]);

        UpdateEndMarker(fromMarker, route.From, path.StartDir, inProgress, MouseGesture.Hold);
        UpdateEndMarker(toMarker, route.To, path.EndDir, false, MouseGesture.Release);
        LayoutChevrons(path, scale);
    }

    static Rect ToCanvas(Rect screen, float scale) => new Rect(screen.position / scale, screen.size / scale);

    // Parks the "SCROLL" hint on the edge of the scroll list `anchor`
    // is hidden in — the bottom edge (arrow down) when it's below the visible
    // part of the list, the top edge (arrow up) when above. False (hint left
    // alone) when the anchor isn't clipped by a scroll list at all.
    bool ShowScrollHint(GuideAnchor anchor, float scale)
    {
        if (anchor.IsWorld || !anchor.TryGetScrollClip(out ScrollRect scroll, out int direction)) return false;

        // Centered ON the list's edge rather than inside it — reads as "more
        // past this edge", and doesn't sit on top of the very item being
        // pointed at when it's only partly scrolled out of view.
        Rect view = ToCanvas(ScreenOccupancy.ScreenRectOf(scroll.viewport), scale);
        hintRt.anchoredPosition = new Vector2(view.center.x, direction < 0 ? view.yMin : view.yMax);
        if (!hintRt.gameObject.activeSelf) hintRt.gameObject.SetActive(true);

        float t = Mathf.Sin(Time.unscaledTime * 4f) * 0.5f + 0.5f;
        Color tint = route.Color ?? TrailColor;
        hintLabel.color = tint;
        Color arrowTint = tint;
        arrowTint.a = Mathf.Lerp(0.7f, 1f, t);
        hintArrow.color = arrowTint;
        hintArrowRt.anchoredPosition = new Vector2(-24f, direction * Mathf.Lerp(-1f, 4f, t));
        hintArrowRt.localRotation = Quaternion.Euler(0f, 0f, direction < 0 ? 0f : 180f); // glyph points down

        chevronScreenRects.Add(new Rect((hintRt.anchoredPosition - hintRt.sizeDelta * 0.5f) * scale, hintRt.sizeDelta * scale));
        return true;
    }

    static void UpdateEndMarker(UIPulseMarker marker, GuideAnchor anchor, Vector2 trailDir, bool suppressed, MouseGesture gesture)
    {
        if (anchor.IsWorld) { marker.Hide(); return; }
        marker.SetSuppressed(suppressed);
        if (!marker.IsShowing) marker.Show(anchor, trailDir, gesture);
        else marker.SetAvoidDirection(trailDir);
    }

    // A quadratic Bézier between the two ends bowed sideways by `arc` × the
    // distance between them (positive = up on screen, or left for a mostly
    // vertical route). Each end is left in the direction of the bow's control
    // point — out through a UI end's edge (plus a small gap), or short of a
    // world end by roughly its marker's ring — so a deep bow leaves a button
    // through its top/bottom rather than cutting across it sideways.
    TrailPath BuildPath(Rect from, Rect to, float fromWorldGap, float toWorldGap, float arc)
    {
        Vector2 ca = from.center, cb = to.center;
        Vector2 delta = cb - ca;
        float len = delta.magnitude;
        if (len < 1f) return default;

        Vector2 dir = delta / len;
        Vector2 perp = new Vector2(-dir.y, dir.x);
        if (Mathf.Abs(dir.x) < 0.35f) { if (perp.x > 0f) perp = -perp; }
        else if (perp.y < 0f) perp = -perp;
        Vector2 control = (ca + cb) * 0.5f + perp * (arc * len * 2f); // a quadratic's peak sits halfway to its control point

        Vector2 startDir = (control - ca).normalized;
        Vector2 endDir = (control - cb).normalized;
        Vector2 a = EndPoint(route.From.IsWorld, from, fromWorldGap, startDir);
        Vector2 b = EndPoint(route.To.IsWorld, to, toWorldGap, endDir);

        // Too close, or the two ends' gaps already overlap (the trail's start
        // would sit past its end).
        if ((b - a).magnitude < ChevronSpacing * 1.5f || Vector2.Dot(b - a, dir) <= 0f) return default;

        return new TrailPath { Valid = true, Start = a, End = b, Control = control, StartDir = startDir, EndDir = endDir };
    }

    static Vector2 EndPoint(bool world, Rect rect, float worldGap, Vector2 dir)
    {
        if (world)
            return rect.center + dir * (worldGap * WorldEndGapMul + UiEndGap);

        Vector2 half = rect.size * 0.5f;
        float tx = Mathf.Abs(dir.x) > 1e-4f ? half.x / Mathf.Abs(dir.x) : float.MaxValue;
        float ty = Mathf.Abs(dir.y) > 1e-4f ? half.y / Mathf.Abs(dir.y) : float.MaxValue;
        return rect.center + dir * (Mathf.Min(tx, ty) + UiEndGap);
    }

    // Fills samples/sampleLengths for `p`; returns its arc length and how many
    // chevrons it gets.
    int SamplePath(TrailPath p, out float total)
    {
        total = 0f;
        samples[0] = p.Start;
        sampleLengths[0] = 0f;
        for (int i = 1; i <= CurveSamples; i++)
        {
            samples[i] = Bezier(p.Start, p.Control, p.End, i / (float)CurveSamples);
            total += Vector2.Distance(samples[i - 1], samples[i]);
            sampleLengths[i] = total;
        }
        return Mathf.Clamp(Mathf.FloorToInt(total / ChevronSpacing), 2, MaxChevrons);
    }

    float PathOverlap(TrailPath p)
    {
        int count = SamplePath(p, out float total);
        float overlap = 0f;
        var half = Vector2.one * ChevronSize * 0.5f;
        for (int k = 0; k < count; k++)
        {
            SampleAt((k + 0.5f) / count * total, out Vector2 pos, out _);
            overlap += ScreenOccupancy.OverlapArea(new Rect(pos - half, half * 2f), occupied);
        }
        return overlap;
    }

    void LayoutChevrons(TrailPath path, float scale)
    {
        int count = SamplePath(path, out float total);
        float since = Time.unscaledTime - spawnTime;
        float head = (Time.unscaledTime / WavePeriod) % 1f * (1f + WaveWidth);

        for (int k = 0; k < count; k++)
        {
            float u = (k + 0.5f) / count;
            SampleAt(u * total, out Vector2 pos, out Vector2 tangent);

            // Reveal source → destination on Show, then a pulse running the
            // same way on a loop: sharp leading edge, fading tail.
            float reveal = Mathf.Clamp01((since - u * RevealTime) / 0.2f);
            float d = head - u;
            float bump = d >= 0f && d <= WaveWidth ? 1f - d / WaveWidth : 0f;

            var img = chevrons[k];
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
            var rt = img.rectTransform;
            rt.anchoredPosition = pos;
            // Pixel-art glyph: snapped to 45° steps (a staircase of headings
            // along the curve, like the rest of the game's pixel art, rather
            // than every chevron's cells rotated to an arbitrary angle), and
            // the pulse is carried by brightness alone — resizing a
            // point-filtered glyph every frame makes its cells shimmer.
            float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg + 90f; // sprite points down
            angle = Mathf.Round(angle / 45f) * 45f;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            float size = ChevronSize * (reveal < 1f ? Mathf.Lerp(0.6f, 1f, reveal) : 1f);
            rt.sizeDelta = Vector2.one * size;

            Color c = route.Color ?? TrailColor;
            c.a = Mathf.Lerp(0.4f, 1f, bump) * reveal * trailVisibility;
            img.color = c;

            if (c.a > 0.15f)
                chevronScreenRects.Add(new Rect((pos - Vector2.one * size * 0.5f) * scale, Vector2.one * size * scale));
        }
        HideChevrons(count);
    }

    static Vector2 Bezier(Vector2 a, Vector2 c, Vector2 b, float t)
    {
        float it = 1f - t;
        return it * it * a + 2f * it * t * c + t * t * b;
    }

    // Arc-length lookup into the sampled curve, so chevrons sit evenly spaced
    // along the curve rather than bunching where the Bézier parameter is slow.
    void SampleAt(float length, out Vector2 pos, out Vector2 tangent)
    {
        for (int i = 1; i <= CurveSamples; i++)
        {
            if (sampleLengths[i] < length && i < CurveSamples) continue;
            float segLen = sampleLengths[i] - sampleLengths[i - 1];
            float t = segLen > 1e-4f ? (length - sampleLengths[i - 1]) / segLen : 0f;
            pos = Vector2.Lerp(samples[i - 1], samples[i], t);
            tangent = (samples[i] - samples[i - 1]).normalized;
            return;
        }
        pos = samples[CurveSamples];
        tangent = (samples[CurveSamples] - samples[CurveSamples - 1]).normalized;
    }

    void HideChevrons(int fromIndex)
    {
        for (int i = fromIndex; i < chevrons.Count; i++)
            if (chevrons[i].gameObject.activeSelf) chevrons[i].gameObject.SetActive(false);
    }
}
