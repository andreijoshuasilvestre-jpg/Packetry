using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tiles a RawImage's pattern texture and slides it diagonally forever —
/// used for the main menu's seamless device-icon background (see
/// MainMenuCanvasBuilder.BuildLayers) and LoadingScreenManager's copy of it.
///
/// The pattern is anchored to the SCREEN, not to this RawImage's own rect:
/// every copy of it — the menu's own, LoadingScreenManager's bridge, the
/// gameplay background quad (ScrollingQuadBackground) — samples the same
/// screen-space tile grid (ScreenTiles) at the same scroll position
/// (SharedOffset), so any one copy can replace another mid-transition with
/// nothing on screen moving. A rect that's scaled up, offset, or sitting on
/// a canvas with different CanvasScaler settings still lands on the same
/// grid — its uvRect is worked out from where its corners actually are on
/// screen each frame.
/// </summary>
[RequireComponent(typeof(RawImage))]
[RequireComponent(typeof(RectTransform))]
public class ScrollingBackground : MonoBehaviour
{
    // On-screen size of one tile, in UIScale.BaseReferenceResolution-wide
    // units (1920 = the full screen width, whatever the actual resolution),
    // square since the pattern texture is. Shared with GameBootstrap.
    // SetupBackgroundQuad so the gameplay background uses the exact same
    // grid. 386 reproduces the menu's hand-tuned look — MainMenu.unity's
    // Background is scaled 1.61× over what used to be a 240-unit tile.
    public const float DefaultTileSizePixels = 386f;

    // UV units (tiles) per second — the menu pattern's drift, and the gameplay
    // quad's, which has to match it for the swap between them to line up.
    public static readonly Vector2 DefaultScrollSpeed = new Vector2(0.06f, 0.04f);

    [Tooltip("On-screen size of one pattern tile, in 1920-wide reference units.")]
    [SerializeField] float tileSize = DefaultTileSizePixels;

    [Tooltip("UV units per second. 1 unit = one full tile width/height.")]
    [SerializeField] Vector2 scrollSpeed = DefaultScrollSpeed;

    RawImage image;
    RectTransform rt;
    readonly Vector3[] corners = new Vector3[4];

    // The one clock every copy scrolls by. Static, so it carries straight
    // across scene loads: a copy built this frame lands exactly where one
    // that's been drifting for minutes already is. Advanced once per frame
    // by that frame's real time, capped — a scene-activation hitch (over a
    // second in the Editor) then reads as the pattern briefly stopping and
    // carrying on from where it stopped, not jumping ahead to catch up.
    const double MaxClockStep = 0.1;
    static double clock;
    static int clockFrame = -1;

    /// <summary>
    /// Scroll position every copy of the pattern shares right now, for a
    /// given speed — the same value from any copy in the same frame, however
    /// long ago it was created.
    /// </summary>
    public static Vector2 SharedOffset(Vector2 speed)
    {
        if (clockFrame != Time.frameCount)
        {
            clockFrame = Time.frameCount;
            clock += System.Math.Min(Time.unscaledDeltaTime, MaxClockStep);
        }
        return new Vector2((float)(clock * speed.x % 1.0), (float)(clock * speed.y % 1.0));
    }

    /// <summary>How many tiles of the given size span the full screen on
    /// each axis — the one grid every copy of the pattern samples.</summary>
    public static Vector2 ScreenTiles(float tileSizeUnits)
    {
        float tilePixels = tileSizeUnits * Screen.width / UIScale.BaseReferenceResolution.x;
        return new Vector2(Screen.width, Screen.height) / tilePixels;
    }

    void Awake()
    {
        image = GetComponent<RawImage>();
        rt    = GetComponent<RectTransform>();
    }

    // LateUpdate, not Update: coroutines resume between the two, and
    // LoadingScreenManager rescales its bridge copy from one right as it
    // snaps it on — measured in Update, this frame's uvRect would still fit
    // the old size and the tiles would pop for a frame.
    void LateUpdate()
    {
        var canvas = image.canvas;
        if (canvas == null || Screen.width <= 0 || Screen.height <= 0) return;
        canvas = canvas.rootCanvas;
        var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        rt.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        // Screen pixels → tiles, then offset by the shared scroll position:
        // the UV at any screen point comes out the same no matter which
        // copy's rect it falls inside.
        float tilePixels = tileSize * Screen.width / UIScale.BaseReferenceResolution.x;
        image.uvRect = new Rect(SharedOffset(scrollSpeed) + min / tilePixels, (max - min) / tilePixels);
    }
}
