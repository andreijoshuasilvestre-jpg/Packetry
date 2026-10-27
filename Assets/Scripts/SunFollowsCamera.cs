using UnityEngine;

/// <summary>
/// Locks the directional "sun" light's yaw to a fixed offset from the orbit
/// camera's own yaw, so shadows always read the same way on screen (e.g.
/// always falling toward the lower-right of the view) no matter which way
/// the player has spun the camera with Q/E or free rotation. A light with a
/// fixed world-space yaw would otherwise swing shadows across the screen —
/// or even point them back at the camera — as the view orbits around it.
/// Pitch stays fixed; only yaw tracks the camera.
/// </summary>
public class SunFollowsCamera : MonoBehaviour
{
    [SerializeField] private float yawOffset = 135f;
    [SerializeField] private float pitch     = 50f;

    void Awake()  => Sync();
    void LateUpdate() => Sync();

    void Sync()
    {
        var cam = IsometricCamera.Instance;
        if (cam == null) return;

        float camYaw = cam.transform.eulerAngles.y;
        transform.rotation = Quaternion.Euler(pitch, camYaw + yawOffset, 0f);
    }
}
