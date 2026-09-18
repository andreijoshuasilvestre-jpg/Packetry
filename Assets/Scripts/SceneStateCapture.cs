using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Bridge between the live scene and TopologySave — the only place that
/// knows how to turn placed Tables/NetworkDevices/Cables into save data and
/// back. Shared by AutoSaveManager (capture, on every relevant event) and
/// TopologyLoader (restore, once at level start) so there's exactly one
/// implementation of "what a saved level looks like" to keep in sync.
/// </summary>
public static class SceneStateCapture
{
    // ── Capture ──────────────────────────────────────────────────────────────

    public static TopologySave Capture()
    {
        var data = new TopologySave();

        foreach (var table in Table.AllTables)
        {
            if (table == null) continue;
            Vector3 c = table.FinalCenter;
            data.tables.Add(new TableSave { x = c.x, y = c.y, z = c.z, width = table.width, length = table.length });
        }

        var devices = UnityEngine.Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude)
            .Where(d => !d.isPaletteItem)
            .ToList();

        foreach (var device in devices)
        {
            var addr = device.GetComponent<DeviceAddress>();
            bool hasAddress = addr != null && addr.HasAddress;

            string subnetId = "";
            if (hasAddress)
            {
                subnetId = SubnetId(addr.Ip, addr.Mask);
                AddSubnetIfMissing(data, addr.Ip, addr.Mask, subnetId);
            }

            var portActive = new bool[device.PortCount];
            for (int i = 0; i < device.PortCount; i++) portActive[i] = device.IsPortActive(i);

            var interfaces = new List<InterfaceConfigSave>();
            if (device.deviceType == DeviceType.Router)
            {
                var ifaceCfg = device.GetComponent<RouterInterfaceConfig>();
                if (ifaceCfg != null)
                    for (int i = 0; i < ifaceCfg.Count; i++)
                    {
                        var pc = ifaceCfg.Get(i);
                        interfaces.Add(new InterfaceConfigSave
                        {
                            port = i,
                            ip = pc.ip ?? "",
                            mask = pc.mask ?? "",
                            shutdown = pc.shutdown
                        });
                    }
            }

            Vector3 pos = device.transform.position;
            data.devices.Add(new DeviceSave
            {
                deviceId = device.SaveId,
                subnetId = subnetId,
                deviceType = device.deviceType.ToString(),
                x = pos.x,
                y = pos.y,
                z = pos.z,
                rotY = device.transform.eulerAngles.y,
                ipAddress = hasAddress ? addr.Ip : "",
                ipMask = hasAddress ? addr.Mask : "",
                portActive = portActive,
                interfaces = interfaces
            });
        }

        if (CableManager.Instance != null)
        {
            foreach (var cable in CableManager.Instance.Cables)
            {
                if (cable == null || cable.DeviceA == null || cable.DeviceB == null) continue;
                data.cables.Add(new CableSave
                {
                    sourceDeviceId = cable.DeviceA.SaveId,
                    sourcePort = cable.DeviceA.GetPortIndexForCable(cable),
                    targetDeviceId = cable.DeviceB.SaveId,
                    targetPort = cable.DeviceB.GetPortIndexForCable(cable)
                });
            }
        }

        return data;
    }

    static string SubnetId(string ip, string mask)
    {
        uint net = SubnetMath.NetworkAddress(ip, mask);
        return $"{(net >> 24) & 0xFF}.{(net >> 16) & 0xFF}.{(net >> 8) & 0xFF}.{net & 0xFF}";
    }

    static void AddSubnetIfMissing(TopologySave data, string ip, string mask, string subnetId)
    {
        if (data.subnets.Any(s => s.subnetId == subnetId)) return;

        uint net = SubnetMath.NetworkAddress(ip, mask);
        data.subnets.Add(new SubnetSave
        {
            subnetId = subnetId,
            subnetName = $"{subnetId}/{SubnetMath.MaskToPrefixLength(mask)}",
            subnetColorHex = ColorUtility.ToHtmlStringRGB(GameTheme.SubnetTint(net)),
            gatewayAddress = "" // no "designated gateway" concept exists in-game yet
        });
    }

    // ── Restore ──────────────────────────────────────────────────────────────

    /// <summary>Rebuilds tables, then devices (with address/port state), then cables,
    /// from a previously captured TopologySave. Mirrors Level2SceneSetup's own build
    /// order for the same table-before-devices-before-cables reason.</summary>
    public static void Restore(TopologySave data)
    {
        if (data == null) return;

        foreach (var t in data.tables)
        {
            var go = DeviceModelBuilder.BuildTable(t.width, t.length);
            go.transform.position = new Vector3(t.x, t.y, t.z);
            go.GetComponent<Table>().SyncFinalCenter();
        }

        var devicesById = new Dictionary<string, NetworkDevice>();
        foreach (var d in data.devices)
        {
            if (!Enum.TryParse(d.deviceType, out DeviceType type)) continue;

            GameObject go = type switch
            {
                DeviceType.PC => DeviceModelBuilder.BuildPC(),
                DeviceType.Switch => DeviceModelBuilder.BuildSwitch(),
                DeviceType.Router => DeviceModelBuilder.BuildRouter(),
                _ => DeviceModelBuilder.BuildPC()
            };
            go.transform.position = new Vector3(d.x, d.y, d.z);
            go.transform.rotation = Quaternion.Euler(0f, d.rotY, 0f);

            var nd = go.GetComponent<NetworkDevice>();
            nd.SaveId = d.deviceId;
            nd.isPaletteItem = false;

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            if (SubnetMath.IsDottedQuad(d.ipAddress) && SubnetMath.IsDottedQuad(d.ipMask))
            {
                var restoredAddr = go.AddComponent<DeviceAddress>();
                restoredAddr.SetAddress(d.ipAddress, d.ipMask);
            }

            if (d.portActive != null)
                for (int i = 0; i < d.portActive.Length && i < nd.PortCount; i++)
                    nd.SetPortActive(i, d.portActive[i]);

            // DeviceModelBuilder.BuildRouter() already attached+sized a
            // RouterInterfaceConfig above; just fill in the saved per-port values.
            if (type == DeviceType.Router && d.interfaces != null)
            {
                var ifaceCfg = go.GetComponent<RouterInterfaceConfig>();
                foreach (var ic in d.interfaces)
                {
                    var pc = ifaceCfg.Get(ic.port);
                    if (pc == null) continue;
                    pc.ip       = ic.ip ?? "";
                    pc.mask     = ic.mask ?? "";
                    pc.shutdown = ic.shutdown;
                }
            }

            devicesById[d.deviceId] = nd;
        }

        if (CableManager.Instance != null)
        {
            foreach (var c in data.cables)
            {
                if (!devicesById.TryGetValue(c.sourceDeviceId, out var a)) continue;
                if (!devicesById.TryGetValue(c.targetDeviceId, out var b)) continue;
                CableManager.Instance.CreateCable(a, c.sourcePort, b, c.targetPort);
            }
        }
    }
}
