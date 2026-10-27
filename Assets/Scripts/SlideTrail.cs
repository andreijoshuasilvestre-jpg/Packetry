using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Leaves a trail of small white streaks behind a RectTransform while it's
/// actually moving — each one spawns right at the element's own trailing
/// edge (not at some point already offset behind it) and then stretches and
/// rushes away from that origin as it fades, the same "stars streaking past
/// during a warp jump" look, rather than a static afterimage. Spawn points
/// are spread across the full height (or width, for vertical motion) of the
/// moving element, not a single line down its middle. Purely a visual
/// add-on: watches its own RectTransform's anchoredPosition each frame and
/// spawns streaks whenever it's actually moved since the last spawn tick —
/// works with anything that moves the transform (SlidePanel today, see
/// MainMenuShellBuilder's logo/buttons), no coupling to how the motion is
/// driven.
/// </summary>
public class SlideTrail : MonoBehaviour
{
    const float SpawnInterval = 0.035f;
    const int   StarsPerTick  = 1;
    const int   MaxAlive      = 7; // hard cap on simultaneous streaks — a few fast ones, not a swarm
    const float StarLifetime  = 0.13f; // short — gone quickly reads as fast, not lingering
    const float Thickness     = 4f;
    static readonly Color StarColor = new Color(1f, 1f, 1f, 0.85f);

    // Default (logo) sizing — a caller can shrink these via Configure() for
    // a smaller element, e.g. MainMenuShellBuilder's buttons, without
    // affecting the logo's own trail.
    float minStreakLength   = 2f;
    float maxStreakLength   = 20f;
    float maxTravelDistance = 30f;

    RectTransform rt;
    Vector2 lastPos;
    float timer;
    int aliveCount;

    /// <summary>Scales this element's streak length/travel distance relative to the logo's own default sizing — call once after AddComponent.</summary>
    public void Configure(float sizeScale)
    {
        minStreakLength   *= sizeScale;
        maxStreakLength   *= sizeScale;
        maxTravelDistance *= sizeScale;
    }

    void Awake()
    {
        rt = (RectTransform)transform;
        lastPos = rt.anchoredPosition;
    }

    void Update()
    {
        Vector2 pos = rt.anchoredPosition;
        Vector2 delta = pos - lastPos;
        lastPos = pos;

        if (delta.sqrMagnitude < 1f) { timer = 0f; return; } // not actually moving — no trail

        timer += Time.unscaledDeltaTime;
        if (timer < SpawnInterval) return;
        timer = 0f;

        Vector2 dir = delta.normalized;
        bool horizontal = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y);

        // The moving rect's trailing edge, in its own pivot-relative local
        // frame — the same units anchoredPosition already uses (no
        // rotation/scale here) — so streaks visibly originate from the
        // element's own edge as it pulls away, not from an arbitrary point
        // already behind it. rt.rect (not sizeDelta) since the menu buttons
        // are horizontally stretch-anchored, where sizeDelta.x is just the
        // delta (0) rather than the actual rendered width.
        float w = rt.rect.width, h = rt.rect.height;
        float px = rt.pivot.x, py = rt.pivot.y;
        float edgeX = dir.x >= 0f ? -px * w : (1f - px) * w;
        float edgeY = dir.y >= 0f ? -py * h : (1f - py) * h;

        for (int i = 0; i < StarsPerTick; i++)
        {
            if (aliveCount >= MaxAlive) break; // capped — let existing streaks finish before adding more

            Vector2 originOffset = horizontal
                ? new Vector2(edgeX, Random.Range(-py * h, (1f - py) * h))
                : new Vector2(Random.Range(-px * w, (1f - px) * w), edgeY);

            SpawnStar(pos + originOffset, -dir, horizontal);
        }
    }

    void SpawnStar(Vector2 origin, Vector2 travelDir, bool horizontal)
    {
        var go = new GameObject("WarpStar", typeof(RectTransform));
        go.transform.SetParent(rt.parent, false);
        go.transform.SetSiblingIndex(rt.GetSiblingIndex()); // renders behind the moving element

        var grt = (RectTransform)go.transform;
        grt.anchorMin = rt.anchorMin;
        grt.anchorMax = rt.anchorMax;
        grt.pivot     = new Vector2(0.5f, 0.5f); // SlideTrailGhost drives position/size directly each frame, so this doesn't need to match rt's own pivot

        var img = go.AddComponent<Image>(); // no sprite — plain solid-color box, matches the blocky pixel look
        img.raycastTarget = false;
        img.color = StarColor;

        // Slight per-star variance so a tick's burst reads as a loose
        // starfield rather than identical clones.
        float minLen = Random.Range(minStreakLength, minStreakLength + 8f);
        float maxLen = Random.Range(maxStreakLength * 0.7f, maxStreakLength);

        aliveCount++;
        var ghost = go.AddComponent<SlideTrailGhost>();
        ghost.PlayStar(img, grt, origin, travelDir, horizontal, StarLifetime, minLen, maxLen, maxTravelDistance, Thickness,
            () => aliveCount--);
    }
}

/// <summary>
/// Drives one spawned trail streak — grows from a point at its origin into a
/// short line rushing away along travelDir while fading, then self-destroys.
/// Hosts its own coroutine on the streak itself rather than on the
/// SlideTrail that spawned it — a coroutine dies silently the instant its
/// host's GameObject (or any ancestor) goes inactive, which happens
/// constantly here (the menu this trail belongs to gets hidden
/// mid-transition), and that used to leave half-faded streaks frozen in
/// place, popping back into view stuck whenever the menu was shown again.
/// OnDisable cleans up immediately instead if that ever happens, so a streak
/// can never outlive its own visibility.
/// </summary>
public class SlideTrailGhost : MonoBehaviour
{
    Image image;
    RectTransform rt;
    Vector2 origin;
    Vector2 travelDir;
    bool horizontal;
    float lifetime, minLength, maxLength, maxDistance, thickness;
    System.Action onDestroyed;
    bool started;
    bool destroyed;

    public void PlayStar(Image image, RectTransform rt, Vector2 origin, Vector2 travelDir, bool horizontal,
        float lifetime, float minLength, float maxLength, float maxDistance, float thickness, System.Action onDestroyed)
    {
        this.image       = image;
        this.rt          = rt;
        this.origin      = origin;
        this.travelDir   = travelDir;
        this.horizontal  = horizontal;
        this.lifetime    = lifetime;
        this.minLength   = minLength;
        this.maxLength   = maxLength;
        this.maxDistance = maxDistance;
        this.thickness   = thickness;
        this.onDestroyed = onDestroyed;
        started = true;
        StartCoroutine(Animate());
    }

    void OnDisable()
    {
        if (started) DestroySelf();
    }

    IEnumerator Animate()
    {
        float t = 0f;
        while (t < lifetime)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / lifetime);
            // Cubic ease-out — nearly all of the travel/growth happens in the
            // first third of its (already short) lifetime, so it reads as a
            // quick dart away rather than a smooth glide — the "speedy" part.
            float eased = 1f - (1f - p) * (1f - p) * (1f - p);

            float length = Mathf.Lerp(minLength, maxLength, eased);
            float dist   = Mathf.Lerp(0f, maxDistance, eased) + length * 0.5f;
            Vector2 center = origin + travelDir * dist;

            rt.anchoredPosition = center;
            rt.sizeDelta = horizontal ? new Vector2(length, thickness) : new Vector2(thickness, length);
            var c = image.color;
            image.color = new Color(c.r, c.g, c.b, (1f - p) * 0.85f);

            yield return null;
        }
        DestroySelf();
    }

    void DestroySelf()
    {
        if (destroyed) return;
        destroyed = true;
        onDestroyed?.Invoke();
        Destroy(gameObject);
    }
}
