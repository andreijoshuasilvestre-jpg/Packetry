using System.Collections;
using UnityEngine;

/// <summary>
/// Drives a transferring packet's body the same way PingPacketEffect's orb
/// animates itself — an elastic pop-in on spawn, then a comet-style stretch
/// along the direction of travel (thinning the cross-section to roughly
/// conserve volume) — but entirely self-contained, reading its own
/// transform's position each frame rather than being told where it's going.
/// PacketManager.Update() still owns all real packet behaviour (speed, hop
/// advance, capacity backpressure, delivery/drop) and only ever writes to the
/// root transform's position, exactly as before this effect existed; this
/// component just reacts to that position changing, so none of that timing
/// changes. Lives on the packet's root GameObject and only ever touches the
/// separate Body child transform (see PacketVisual.Create), so the Icon
/// child's own billboarded glyph stays undistorted instead of stretching
/// along with the body.
///
/// PlaySquashOut mirrors PingPacketEffect.SquashOut — the same "hitting the
/// model" flatten right before PacketManager destroys this packet on
/// delivery, so a regular transfer's arrival reads with the same
/// stretch-bounce language a ping's orb already has instead of just vanishing
/// mid-stretch.
/// </summary>
public class PacketTravelEffect : MonoBehaviour
{
    const float PopInDuration     = 0.12f;
    const float SquashOutDuration = 0.08f; // matches PingPacketEffect.SquashOutDuration

    Transform body;
    float baseRadius;
    float referenceSpeed;
    Vector3 prevPos;
    float popT;
    bool suspended; // true once PlaySquashOut takes over the body's scale

    public void Init(Transform bodyTransform, float radius, float typicalSpeed)
    {
        body = bodyTransform;
        baseRadius = radius;
        referenceSpeed = Mathf.Max(typicalSpeed, 0.01f);
        body.localScale = Vector3.zero;
        prevPos = transform.position;
        popT = 0f;
    }

    /// <summary>Flattens the body the same way a ping's orb squashes on
    /// arrival, then leaves it squashed — caller destroys the packet right
    /// after. Sets `suspended` first so LateUpdate's own scale logic doesn't
    /// fight this frame-by-frame.</summary>
    public IEnumerator PlaySquashOut()
    {
        suspended = true;
        if (body == null) yield break;

        Vector3 start = body.localScale;
        Vector3 squashed = new Vector3(start.x * 2.2f, start.y * 2.2f, start.z * 0.25f);
        float t = 0f;
        while (t < SquashOutDuration)
        {
            t += Time.deltaTime;
            if (body == null) yield break;
            body.localScale = Vector3.Lerp(start, squashed, t / SquashOutDuration);
            yield return null;
        }
    }

    // LateUpdate so this always reads the position PacketManager.Update()
    // already moved the packet to this frame (all Updates run before any
    // LateUpdate, regardless of script execution order between the two).
    void LateUpdate()
    {
        if (suspended || body == null) return;

        popT += Time.deltaTime;
        float pop = popT < PopInDuration ? EaseOutBack(Mathf.Clamp01(popT / PopInDuration)) : 1f;
        pop = Mathf.Max(pop, 0f);

        Vector3 pos = transform.position;
        Vector3 delta = pos - prevPos;
        prevPos = pos;

        if (delta.sqrMagnitude > 1e-10f)
        {
            body.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            float stretch = Mathf.Clamp(speed / referenceSpeed, 0.7f, 1.6f);
            float cross = baseRadius / Mathf.Sqrt(stretch);
            body.localScale = new Vector3(cross, cross, baseRadius * stretch * 2f) * pop;
        }
        else
        {
            body.localScale = Vector3.one * baseRadius * pop;
        }
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float f = x - 1f;
        return 1f + c3 * f * f * f + c1 * f * f;
    }
}
