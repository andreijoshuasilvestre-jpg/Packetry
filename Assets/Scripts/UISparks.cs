using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Short-lived UI-space spark dot — flies outward from its spawn point,
/// curving under a little "gravity" and stretching into a motion streak
/// aligned with its own travel direction (instead of staying a flat round
/// dot), shrinking and fading over its lifetime, then destroys itself.
/// Rendered additive (see RadialGlowSprite.GetAdditiveMaterial) so a cluster
/// of sparks actually adds up into bright light instead of alpha-compositing
/// into a flat tinted blob. The UI equivalent of a single ConstructionSparks
/// particle: same "quick electric streak" feel, just driven by Update()
/// instead of a ParticleSystem, since UI runs in Screen Space - Camera where
/// a 3D ParticleSystem doesn't reliably sort against canvas geometry.
/// </summary>
public class UISparkParticle : MonoBehaviour
{
    const float Gravity = 260f; // curves the streak into a falling arc rather than a dead-straight radial line

    RectTransform rt;
    Image image;
    Vector2 velocity;
    Color baseColor;
    float lifetime;
    float age;

    public static void Spawn(RectTransform parent, Vector2 anchoredOrigin, Vector2 velocity, Color color, float size, float lifetime)
    {
        var go = new GameObject("Spark", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredOrigin;
        rt.sizeDelta        = new Vector2(size, size);

        var image = go.AddComponent<Image>();
        image.sprite         = RadialGlowSprite.Get();
        image.material       = RadialGlowSprite.GetAdditiveMaterial();
        image.raycastTarget  = false;
        image.color          = color;

        var p = go.AddComponent<UISparkParticle>();
        p.rt        = rt;
        p.image     = image;
        p.velocity  = velocity;
        p.baseColor = color;
        p.lifetime  = lifetime;
    }

    void Update()
    {
        age += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(age / lifetime);

        velocity += Vector2.down * Gravity * Time.unscaledDeltaTime;
        rt.anchoredPosition += velocity * Time.unscaledDeltaTime;
        velocity *= 0.92f; // drag — reads as a short streak that stops, not a floating ember

        image.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * (1f - t));

        // Non-uniform scale stretches the round glow sprite into an elongated
        // streak along its own velocity — faster sparks read as hot elongated
        // filaments, slow/dying ones relax back toward round.
        float shrink  = Mathf.Lerp(1f, 0.4f, t);
        float stretch = 1f + Mathf.Clamp01(velocity.magnitude / 300f) * 1.6f;
        if (velocity.sqrMagnitude > 1f)
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90f);
        rt.localScale = new Vector3(shrink, shrink * stretch, 1f);

        if (age >= lifetime) Destroy(gameObject);
    }
}

/// <summary>
/// Spawns a short radial burst of UISparkParticles for button click feedback
/// — the UI counterpart of ConstructionSparks.EmitBurst used when a device
/// is placed in the game scene. Uses screen-space conversion (not a direct
/// anchoredPosition copy) to find the burst origin, so it centers correctly
/// regardless of the origin rect's own pivot/anchor setup.
/// </summary>
public static class UISparks
{
    const int   Count         = 14;
    const float Lifetime      = 0.32f;
    const float MinSpeed      = 420f;
    const float MaxSpeed      = 720f;
    const float Size          = 8f;
    const float GlowMultiplier = 1.4f; // UI has no HDR bloom to react to, so a modest boost rather than ConstructionSparks' 2.5x

    public static void EmitBurst(RectTransform originRect, RectTransform sparkParent, Camera eventCamera, Color color)
    {
        if (!TryGetLocalFrame(originRect, sparkParent, eventCamera, out var localOrigin, out var halfExtent)) return;

        Color glowColor = BoostedColor(color);

        for (int i = 0; i < Count; i++)
        {
            // Even radial spread with a little jitter — reads as "electric"
            // irregularity rather than a perfectly uniform ring.
            float angle = (360f / Count) * i + Random.Range(-12f, 12f);
            float speed = Random.Range(MinSpeed, MaxSpeed);
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));

            // Spread across the button's face instead of one exact center
            // pixel, so the burst reads as coming FROM the button rather than
            // from a single dot floating in front of it.
            Vector2 originJitter = new Vector2(dir.x * halfExtent.x, dir.y * halfExtent.y) * Random.Range(0.2f, 0.85f);

            UISparkParticle.Spawn(sparkParent, localOrigin + originJitter, dir * speed, JitteredColor(glowColor), Size, Lifetime);
        }
    }

    /// <summary>
    /// Panel-sized burst — see PanelArrivalGlow. EmitBurst's 14 sparks
    /// bunched toward the center vanish against a panel several times a
    /// button's size, so these instead spawn spread evenly around
    /// originRect's whole outline (stratified along its perimeter, so no
    /// stretch of edge goes bare) and fly outward off whichever edge they
    /// started on. Caller picks count/lifetime to suit the panel.
    /// </summary>
    public static void EmitOutlineBurst(RectTransform originRect, RectTransform sparkParent, Camera eventCamera, Color color,
        int count, float lifetime, float speedScale = 1f)
    {
        if (count <= 0) return;
        if (!TryGetLocalFrame(originRect, sparkParent, eventCamera, out var localOrigin, out var halfExtent)) return;

        float hx = Mathf.Abs(halfExtent.x), hy = Mathf.Abs(halfExtent.y);
        float perimeter = 4f * (hx + hy);
        Color glowColor = BoostedColor(color);

        for (int i = 0; i < count; i++)
        {
            float s = (i + Random.Range(0.15f, 0.85f)) / count * perimeter;
            Vector2 point = PerimeterPoint(s, hx, hy, out Vector2 normal);

            // Fanned off the edge's own outward normal rather than dead
            // perpendicular, so neighbouring sparks cross and scatter.
            float angle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg + Random.Range(-40f, 40f);
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            float speed = Random.Range(MinSpeed, MaxSpeed) * speedScale;

            UISparkParticle.Spawn(sparkParent, localOrigin + point, dir * speed, JitteredColor(glowColor), Size, lifetime);
        }
    }

    // Point at distance s clockwise around a centered hx-by-hy half-extent
    // rect, starting at its top-left corner, plus that edge's outward normal.
    static Vector2 PerimeterPoint(float s, float hx, float hy, out Vector2 normal)
    {
        if (s < 2f * hx) { normal = Vector2.up; return new Vector2(-hx + s, hy); }
        s -= 2f * hx;
        if (s < 2f * hy) { normal = Vector2.right; return new Vector2(hx, hy - s); }
        s -= 2f * hy;
        if (s < 2f * hx) { normal = Vector2.down; return new Vector2(hx - s, -hy); }
        s -= 2f * hx;
        normal = Vector2.left;
        return new Vector2(-hx, -hy + s);
    }

    // originRect's center and half-size, converted into sparkParent-local
    // units via screen space — works regardless of the origin rect's own
    // pivot/anchors/scale.
    static bool TryGetLocalFrame(RectTransform originRect, RectTransform sparkParent, Camera eventCamera,
        out Vector2 localOrigin, out Vector2 halfExtent)
    {
        localOrigin = halfExtent = Vector2.zero;
        if (originRect == null || sparkParent == null) return false;

        Vector3 worldCenter = originRect.TransformPoint(originRect.rect.center);
        Vector3 worldEdge   = originRect.TransformPoint(originRect.rect.center + new Vector2(originRect.rect.width, originRect.rect.height) * 0.5f);
        Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(eventCamera, worldCenter);
        Vector2 screenEdge   = RectTransformUtility.WorldToScreenPoint(eventCamera, worldEdge);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(sparkParent, screenCenter, eventCamera, out localOrigin))
            return false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(sparkParent, screenEdge, eventCamera, out var localEdge);
        halfExtent = localEdge - localOrigin;
        return true;
    }

    static Color BoostedColor(Color color)
    {
        Color c = color * GlowMultiplier;
        c.a = 1f;
        return c;
    }

    // Per-spark brightness jitter — a uniform ring of identical sparks reads
    // as a UI effect; a slightly uneven shower reads as real ones.
    static Color JitteredColor(Color glowColor)
    {
        Color c = glowColor * Random.Range(0.8f, 1.3f);
        c.a = 1f;
        return c;
    }
}
