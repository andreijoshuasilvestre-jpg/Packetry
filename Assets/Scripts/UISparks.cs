using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Short-lived UI-space spark dot — flies outward from its spawn point with
/// a little drag, shrinking and fading over its lifetime, then destroys
/// itself. The UI equivalent of a single ConstructionSparks particle: same
/// "quick electric streak" feel, just driven by Update() instead of a
/// ParticleSystem, since UI runs in Screen Space - Camera where a 3D
/// ParticleSystem doesn't reliably sort against canvas geometry.
/// </summary>
public class UISparkParticle : MonoBehaviour
{
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

        rt.anchoredPosition += velocity * Time.unscaledDeltaTime;
        velocity *= 0.9f; // quick drag — reads as a short streak that stops, not a floating ember

        image.color   = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * (1f - t));
        rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.4f, t);

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
    const int   Count         = 10;
    const float Lifetime      = 0.22f;
    const float MinSpeed      = 300f;
    const float MaxSpeed      = 480f;
    const float Size          = 8f;
    const float GlowMultiplier = 1.4f; // UI has no HDR bloom to react to, so a modest boost rather than ConstructionSparks' 2.5x

    public static void EmitBurst(RectTransform originRect, RectTransform sparkParent, Camera eventCamera, Color color)
    {
        if (originRect == null || sparkParent == null) return;

        Vector3 worldCenter = originRect.TransformPoint(originRect.rect.center);
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, worldCenter);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(sparkParent, screenPoint, eventCamera, out var localOrigin))
            return;

        Color glowColor = color * GlowMultiplier;
        glowColor.a = 1f;

        for (int i = 0; i < Count; i++)
        {
            // Even radial spread with a little jitter — reads as "electric"
            // irregularity rather than a perfectly uniform ring.
            float angle = (360f / Count) * i + Random.Range(-12f, 12f);
            float speed = Random.Range(MinSpeed, MaxSpeed);
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            UISparkParticle.Spawn(sparkParent, localOrigin, dir * speed, glowColor, Size, Lifetime);
        }
    }
}
