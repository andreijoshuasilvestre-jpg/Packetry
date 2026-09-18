using UnityEngine;

/// <summary>
/// Level 7's ("Network Troubleshooting") first-time default content — a
/// table with two departments (2 PCs + a Switch each) already bridged by a
/// Router, same shape as Level 6's own end-state, but seeded with three
/// independent, intentional faults for the player to find and fix. Only
/// instantiated by GameBootstrap.SetupLevelContent when Level 7 has no saved
/// topology yet (see SaveManager.HasSavedTopology); once a save exists,
/// TopologyLoader restores that instead so a player's in-progress fixes
/// aren't overwritten on replay.
///
/// Deliberately built in Awake(), not Start() — Level7Challenge.Start()
/// (added later in GameBootstrap's own Awake, see SetupLevelObjectives)
/// scans for pre-existing NetworkDevices when IT starts, and Awake calls for
/// every object created during GameBootstrap.Awake() all finish before any
/// Start() runs, so this ordering guarantees these devices already exist by
/// then regardless of component add order (same reasoning as
/// Level2SceneSetup).
///
/// The three seeded faults (see comments at each call site below):
///  1. PC A2's cable to Switch A is never created — a physical fault,
///     surfaced by Ping as "No cable path connects these devices."
///  2. PC B2 is addressed onto the wrong subnet (192.168.99.x instead of
///     192.168.20.x) — a configuration fault only visible by inspecting/
///     comparing addresses, not from any Router show command.
///  3. The Router's eth1 interface is configured with the correct IP for
///     Department B but never gets "no shutdown" — a configuration fault
///     directly visible via "show ip interface brief" ("administratively
///     down"), unlike fault 2.
/// </summary>
public class Level7SceneSetup : MonoBehaviour
{
    // Sized generously for 7 devices (2 Switches, 4 PCs, 1 Router) — much
    // larger than a single-device table, so every device's footprint sits
    // comfortably apart with room to re-cable. Tune visually in-editor if
    // anything overlaps.
    const float TableWidth = 7.6f;
    const float TableLength = 3.0f;
    static readonly Vector3 TableCenter = new Vector3(0f, 0f, 0f);

    const string SubnetMask = "255.255.255.0";

    const string DeptAIp1 = "192.168.10.10";
    const string DeptAIp2 = "192.168.10.11";
    const string DeptBIp1 = "192.168.20.10";
    // Fault 2: wrong subnet entirely (should be 192.168.20.x to match PC B1).
    const string DeptBIp2Wrong = "192.168.99.11";

    const string RouterEth0Ip = "192.168.10.1";
    const string RouterEth1Ip = "192.168.20.1";

    void Awake()
    {
        Table table = BuildTable();
        float y = table.topSurfaceY;

        NetworkDevice swA = BuildSwitch(new Vector3(-1.9f, y, 0f));
        NetworkDevice pcA1 = BuildAddressedPc(new Vector3(-3.1f, y, -0.55f), DeptAIp1);
        NetworkDevice pcA2 = BuildAddressedPc(new Vector3(-3.1f, y, 0.55f), DeptAIp2);

        NetworkDevice router = BuildRouter(new Vector3(0f, y, 0f));

        NetworkDevice swB = BuildSwitch(new Vector3(1.9f, y, 0f));
        NetworkDevice pcB1 = BuildAddressedPc(new Vector3(3.1f, y, -0.55f), DeptBIp1);
        NetworkDevice pcB2 = BuildAddressedPc(new Vector3(3.1f, y, 0.55f), DeptBIp2Wrong);

        if (CableManager.Instance != null)
        {
            CableManager.Instance.CreateCable(pcA1, 0, swA, swA.GetFreePorts()[0]);
            // Fault 1: PC A2 is deliberately left uncabled — no CreateCable
            // call for it. Its port stays free.
            CableManager.Instance.CreateCable(swA, swA.GetFreePorts()[0], router, 0);

            CableManager.Instance.CreateCable(pcB1, 0, swB, swB.GetFreePorts()[0]);
            CableManager.Instance.CreateCable(pcB2, 0, swB, swB.GetFreePorts()[0]);
            CableManager.Instance.CreateCable(swB, swB.GetFreePorts()[0], router, 1);
        }

        ConfigureRouter(router);

        // First snapshot so a player who quits before triggering any other
        // save-worthy event still resumes into this exact broken starting
        // layout instead of an empty level (see AutoSaveManager.SaveNow).
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

    static NetworkDevice BuildAddressedPc(Vector3 position, string ip)
    {
        GameObject go = DeviceModelBuilder.BuildPC();
        go.transform.position = position;
        SettleOnTable(go);

        var addr = go.AddComponent<DeviceAddress>();
        addr.SetAddress(ip, SubnetMask);

        var nd = go.GetComponent<NetworkDevice>();
        nd.SetPortActive(0, true); // mirrors DeviceAddressPopup.Apply — addressed interface counts as "up"
        return nd;
    }

    static NetworkDevice BuildSwitch(Vector3 position)
    {
        GameObject go = DeviceModelBuilder.BuildSwitch();
        go.transform.position = position;
        SettleOnTable(go);
        return go.GetComponent<NetworkDevice>();
    }

    static NetworkDevice BuildRouter(Vector3 position)
    {
        GameObject go = DeviceModelBuilder.BuildRouter();
        go.transform.position = position;
        SettleOnTable(go);
        return go.GetComponent<NetworkDevice>();
    }

    // Configured through the same RouterCLI/CLIPanelManager session path a
    // player would use — not a shortcut straight into NetworkDevice state —
    // so "show running-config"/"show ip interface brief" reflect real seeded
    // state when the player investigates.
    static void ConfigureRouter(NetworkDevice router)
    {
        if (CLIPanelManager.Instance == null) return;
        RouterCLI cli = CLIPanelManager.Instance.GetOrCreateSession(router);

        cli.Execute("enable");
        cli.Execute("configure terminal");

        cli.Execute("interface eth0");
        cli.Execute($"ip address {RouterEth0Ip} {SubnetMask}");
        cli.Execute("no shutdown");
        cli.Execute("exit");

        cli.Execute("interface eth1");
        cli.Execute($"ip address {RouterEth1Ip} {SubnetMask}");
        // Fault 3: deliberately no "no shutdown" here — eth1 stays
        // administratively down (RouterCLI's real-IOS default).
        cli.Execute("exit");

        cli.Execute("end");
    }

    static void SettleOnTable(GameObject go)
    {
        go.GetComponent<NetworkDevice>().isPaletteItem = false;
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true; // matches DragDropManager.Drop's resting state
    }
}
