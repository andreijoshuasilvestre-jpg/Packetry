using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static tabletop surface. Devices whose NetworkDevice.requiresTable is true
/// may only be dropped inside a table's XZ footprint (see DragDropManager.Drop()).
/// Placed directly by TableManager — position and footprint are fixed at creation
/// from the two grid corners the player clicked; there is no drag/physics step
/// like devices go through.
/// </summary>
public class Table : MonoBehaviour
{
    private static readonly List<Table> s_all = new List<Table>();

    // Read-only view of every live table — DragDropManager raycasts each
    // one's own collider directly (alongside the ground) to find the exact
    // surface under the cursor while dragging a device, instead of guessing
    // via ClearanceAt's XZ-only step function (see DragDropManager.
    // TryRaycastSurface for why that guess was the source of the
    // table-edge jump bug).
    public static IReadOnlyList<Table> AllTables => s_all;

    public float width;      // world-space X footprint
    public float length;     // world-space Z footprint
    public float topSurfaceY; // world-space Y of the tabletop surface, set by DeviceModelBuilder.BuildTable

    // Final resting position, captured before the reveal animation starts moving
    // transform.position toward it — ContainsXZ must check against this, not the
    // live (mid-animation) position, or placement validity flickers during the reveal.
    private Vector3 finalCenter;

    // Reveal animation played once when the table is first placed. Only the
    // horizontal footprint animates — width (X) then length (Z), the two
    // dimensions the player's two placement clicks actually determined.
    // Height (Y) is never animated: since the cube's pivot is centered,
    // growing it from 0 would expand both up AND down from that center,
    // sinking the table below the ground mid-animation and z-fighting with
    // it while paper-thin. ContainsXZ below uses the fixed width/length
    // fields rather than transform.localScale, so table-placement validity
    // checks are correct even mid-animation — only the visual/collider size
    // is briefly squashed.
    private const float AxisAnimDuration = 0.16f;

    // A literal 0 scale on X or Z makes the transform matrix singular for that
    // frame, which produces undefined normals and a bright/garbage flash before
    // the coroutine's first yield. Use an imperceptible epsilon instead.
    private const float MinAxisScale = 0.001f;

    // Gives finalCenter a correct default the moment this table exists — needed
    // because it's a plain private field (not [SerializeField]), so it never
    // round-trips through a baked level-template scene (see
    // LevelTemplateAuthoringWindow): without this, a template-loaded table's
    // finalCenter would silently reset to (0,0,0) instead of its real position,
    // breaking ContainsXZ/ClearanceAt for any table not sitting exactly at the
    // world origin (e.g. Level 3's router table) and letting cables dip through
    // it. SyncFinalCenter()/PlayBuildAnimation() still override this correctly
    // for the normal live-placement flow, since both run after Awake.
    void Awake() => finalCenter = transform.position;

    void OnEnable()  => s_all.Add(this);
    void OnDisable() => s_all.Remove(this);

    public bool ContainsXZ(Vector3 worldPos)
    {
        Vector3 c = finalCenter;
        return Mathf.Abs(worldPos.x - c.x) <= width * 0.5f &&
               Mathf.Abs(worldPos.z - c.z) <= length * 0.5f;
    }

    public static bool AnyContains(Vector3 worldPos)
    {
        for (int i = 0; i < s_all.Count; i++)
            if (s_all[i] != null && s_all[i].ContainsXZ(worldPos))
                return true;
        return false;
    }

    // True if a rectangular footprint centered at `center` with the given
    // width/length would overlap any placed table's footprint — same AABB
    // test ContainsXZ uses per-corner. `exclude` skips one table (e.g. the
    // one currently being dragged, or none yet for a brand-new placement) so
    // it never "overlaps" itself. Shared by TableManager for both the
    // reposition-drag check and the initial two-click placement check, so
    // the two flows can't drift apart.
    public static bool WouldOverlap(Vector3 center, float width, float length, Table exclude = null)
    {
        float halfW = width * 0.5f, halfL = length * 0.5f;
        for (int i = 0; i < s_all.Count; i++)
        {
            Table other = s_all[i];
            if (other == null || other == exclude) continue;

            Vector3 otherPos = other.finalCenter;
            bool overlapX = Mathf.Abs(center.x - otherPos.x) < halfW + other.width * 0.5f;
            bool overlapZ = Mathf.Abs(center.z - otherPos.z) < halfL + other.length * 0.5f;
            if (overlapX && overlapZ) return true;
        }
        return false;
    }

    // Highest tabletop surface any table occupies at this XZ position, or 0 if
    // none — used by Cable to keep its sag from dipping through a table it
    // passes over. Takes the max in case footprints overlap.
    public static float ClearanceAt(Vector3 worldPos)
    {
        float best = 0f;
        for (int i = 0; i < s_all.Count; i++)
            if (s_all[i] != null && s_all[i].ContainsXZ(worldPos))
                best = Mathf.Max(best, s_all[i].topSurfaceY);
        return best;
    }

    // Tabletop-surface point at this table's center — for a caller (e.g.
    // Level3Guide's "drop the router here" marker) that wants to point at the
    // table itself rather than a device sitting on it, same surface-level
    // convention as NetworkDevice.VisualTopPoint().
    public Vector3 VisualMarkerPoint() => new Vector3(finalCenter.x, topSurfaceY, finalCenter.z);

    // Read-only view of finalCenter — the table's true resting position, valid
    // even while the build-reveal animation is still moving transform.position
    // from the clicked corner toward it. TutorialManager uses this (not
    // transform.position) to place device-drop markers, since OnTablePlaced
    // fires synchronously while that animation is still mid-flight.
    public Vector3 FinalCenter => finalCenter;

    // Keeps ContainsXZ in sync while TableManager drags an already-placed
    // table around — unlike PlayBuildAnimation's grow, a user-driven
    // reposition has no "live position lags the real target" case to guard
    // against, so finalCenter can just track transform.position directly.
    public void SyncFinalCenter() => finalCenter = transform.position;

    // originPoint is the player's first click — the corner the table should
    // visually grow from, staying pinned there while the opposite corner
    // sweeps out to its final position, rather than growing from the center.
    public void PlayBuildAnimation(Vector3 originPoint)
    {
        finalCenter = transform.position;
        StopAllCoroutines();
        StartCoroutine(BuildAnimationRoutine(originPoint));
    }

    IEnumerator BuildAnimationRoutine(Vector3 originPoint)
    {
        Vector3 target = transform.localScale; // normally Vector3.one
        transform.localScale = new Vector3(MinAxisScale, target.y, MinAxisScale); // height fixed, no vertical animation
        transform.position   = new Vector3(originPoint.x, finalCenter.y, originPoint.z);

        yield return AnimateAxis(0, target.x, originPoint.x, finalCenter.x); // width  — grows from the clicked corner
        yield return AnimateAxis(2, target.z, originPoint.z, finalCenter.z); // length — grows from the clicked corner
    }

    IEnumerator AnimateAxis(int axis, float targetScale, float originCoord, float finalCoord)
    {
        float elapsed = 0f;
        while (elapsed < AxisAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / AxisAnimDuration);
            float eased = 1f - (1f - t) * (1f - t); // ease-out (quadratic)
            SetAxis(axis, Mathf.Lerp(MinAxisScale, targetScale, eased));
            SetPositionAxis(axis, Mathf.Lerp(originCoord, finalCoord, eased));
            yield return null;
        }
        SetAxis(axis, targetScale);
        SetPositionAxis(axis, finalCoord);
    }

    void SetAxis(int axis, float value)
    {
        Vector3 s = transform.localScale;
        switch (axis)
        {
            case 0: s.x = value; break;
            case 1: s.y = value; break;
            default: s.z = value; break;
        }
        transform.localScale = s;
    }

    void SetPositionAxis(int axis, float value)
    {
        Vector3 p = transform.position;
        if (axis == 0) p.x = value;
        else if (axis == 2) p.z = value;
        transform.position = p;
    }
}
