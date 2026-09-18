using UnityEngine;

/// <summary>Keeps a world-space TextMesh label facing the main camera.</summary>
public class BillboardLabel : MonoBehaviour
{
    private Transform camTransform;

    void Start()
    {
        if (Camera.main != null) camTransform = Camera.main.transform;
    }

    void LateUpdate()
    {
        if (camTransform == null) return;

        // Ignore the vertical component so the label only yaws to face the camera
        // instead of matching its ~60° pitch — a full spherical billboard tilts the
        // label plane into the device mesh at a near-coplanar angle and z-fights.
        Vector3 dir = camTransform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(dir);
    }
}
