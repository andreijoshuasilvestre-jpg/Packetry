using UnityEngine;

/// <summary>
/// Level 2's ("IP Allocation") first-time default content — a table with two
/// PCs and a Switch, already cabled but deliberately left UNADDRESSED. Only
/// instantiated by GameBootstrap.SetupLevelContent when Level 2 has no saved
/// topology yet (see SaveManager.HasSavedTopology); once a save exists,
/// TopologyLoader restores that instead so the player's own changes (giving
/// the PCs addresses) aren't overwritten on replay.
///
/// Deliberately built in Awake(), not Start() — Level2Guide.Start() (added
/// later in GameBootstrap's own Awake, see SetupLevelObjectives) scans for
/// pre-existing NetworkDevices when IT starts, and Awake calls for every
/// object created during GameBootstrap.Awake() all finish before any
/// Start() runs, so this ordering guarantees those devices already exist by
/// then regardless of component add order.
///
/// No addressing here on purpose (contrast with the Level3_Template scene,
/// whose PCs are pre-addressed) — this level's whole point is teaching the player
/// how to assign an IP address at all, via DeviceAddressPopup, so the two
/// PCs start blank and Level2Guide's stages walk them through it.
/// </summary>
public class Level2SceneSetup : MonoBehaviour
{
    const float TableWidth = 3.6f;
    const float TableLength = 1.8f;
    static readonly Vector3 TableCenter = new Vector3(0f, 0f, 0f);

    void Awake()
    {
        Table table = BuildTable();
        NetworkDevice pc1 = BuildPc(new Vector3(-1.2f, table.topSurfaceY, 0f));
        NetworkDevice pc2 = BuildPc(new Vector3(1.2f, table.topSurfaceY, 0f));
        NetworkDevice sw = BuildSwitch(new Vector3(0f, table.topSurfaceY, 0f));

        if (CableManager.Instance != null)
        {
            var freePorts = sw.GetFreePorts();
            CableManager.Instance.CreateCable(pc1, 0, sw, freePorts[0]);
            CableManager.Instance.CreateCable(pc2, 0, sw, freePorts[1]);
        }

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

    static NetworkDevice BuildPc(Vector3 position)
    {
        GameObject go = DeviceModelBuilder.BuildPC();
        go.transform.position = position;
        SettleOnTable(go);
        return go.GetComponent<NetworkDevice>();
    }

    static NetworkDevice BuildSwitch(Vector3 position)
    {
        GameObject go = DeviceModelBuilder.BuildSwitch();
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
