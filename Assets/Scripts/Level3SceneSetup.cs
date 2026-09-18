using UnityEngine;

/// <summary>
/// Level 3's ("Router CLI Basics") first-time default content — a single
/// table with a Router already sitting on it, nothing else. Only
/// instantiated by GameBootstrap.SetupLevelContent when Level 3 has no saved
/// topology yet (see SaveManager.HasSavedTopology) and no authored template
/// scene exists for it (see LevelTemplateScene.Exists) — once a save exists,
/// TopologyLoader restores that instead.
///
/// This level teaches CLI mode navigation only (see Level3Guide) — no
/// cabling, no PCs, no addressing — so there's nothing here to cable or
/// address either; the Router starts completely unconfigured, same as real
/// IOS out of the box.
///
/// Deliberately built in Awake(), not Start() — Level3Guide.InitNextFrame
/// (added later in GameBootstrap's own Awake, see SetupLevelObjectives)
/// scans for pre-existing NetworkDevices when IT starts, and Awake calls for
/// every object created during GameBootstrap.Awake() all finish before any
/// Start() runs, so this ordering guarantees the router already exists by
/// then regardless of component add order (same reasoning as
/// Level2SceneSetup).
/// </summary>
public class Level3SceneSetup : MonoBehaviour
{
    const float TableWidth = 1.8f;
    const float TableLength = 1.8f;
    static readonly Vector3 TableCenter = new Vector3(0f, 0f, 0f);

    void Awake()
    {
        Table table = BuildTable();
        BuildRouter(new Vector3(0f, table.topSurfaceY, 0f));

        // First snapshot so a player who quits before triggering any other
        // save-worthy event still resumes into this exact starting layout
        // instead of an empty level (see AutoSaveManager.SaveNow).
        AutoSaveManager.Instance?.SaveNow();
    }

    Table BuildTable()
    {
        var go = DeviceModelBuilder.BuildTable(TableWidth, TableLength);
        go.transform.position = TableCenter;
        var table = go.GetComponent<Table>();
        table.SyncFinalCenter();
        return table;
    }

    static NetworkDevice BuildRouter(Vector3 position)
    {
        GameObject go = DeviceModelBuilder.BuildRouter();
        go.transform.position = position;
        SettleOnTable(go);
        return go.GetComponent<NetworkDevice>();
    }

    static void SettleOnTable(GameObject go)
    {
        go.GetComponent<NetworkDevice>().isPaletteItem = false;
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true; // matches DragDropManager.Drop's resting state
    }
}
