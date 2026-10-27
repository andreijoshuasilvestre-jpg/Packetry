using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registers this RectTransform's on-screen bounds with ScreenOccupancy for
/// exactly as long as its GameObject is active — so a panel that's hidden via
/// SetActive(false) (a closed popup, a SidebarButtonLock-hidden button)
/// automatically stops counting as covering anything. Only valid on
/// ScreenSpaceOverlay canvases, whose world corners are screen pixels.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class OccupiesScreen : MonoBehaviour, IScreenOccupant
{
    [SerializeField] private OccupantLayer layer = OccupantLayer.Panel;

    RectTransform rt;

    public OccupantLayer Layer => layer;

    /// <summary>Adds (or re-layers) the component on `go`.</summary>
    public static OccupiesScreen Add(GameObject go, OccupantLayer layer)
    {
        var o = go.GetComponent<OccupiesScreen>();
        if (o == null) o = go.AddComponent<OccupiesScreen>();
        o.layer = layer;
        return o;
    }

    void Awake() => rt = (RectTransform)transform;
    void OnEnable() => ScreenOccupancy.Register(this);
    void OnDisable() => ScreenOccupancy.Unregister(this);

    public void CollectScreenRects(List<Rect> into)
    {
        if (rt != null && isActiveAndEnabled) into.Add(ScreenOccupancy.ScreenRectOf(rt));
    }
}
