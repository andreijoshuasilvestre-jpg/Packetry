using System;
using System.Collections.Generic;

/// <summary>
/// Plain-data save schema, serialized to disk as JSON by SaveManager (see
/// SaveManager.Save/Load) via JsonUtility — every class here must stay
/// [Serializable] and field-only (no properties/methods JsonUtility can't
/// see) for that to work.
///
/// Modeled on the project's ERD with a few deliberate differences — see the
/// plan doc for the full reasoning:
///  - No Tutorial / Player_Tutorial_Progress tables: nothing in the game
///    produces that data yet (no video-tutorial system), so persisting it
///    would just be dead JSON. Settings *is* included (see SettingsData)
///    now that SettingsPanelManager actually writes to it.
///  - Player collapses to a single implicit local profile (no accounts).
///  - Subnet rows are derived from device IP/mask at save time rather than
///    authored separately — there's no "create a subnet" action in-game.
///  - Devices/Cable/Table carry a few extra fields (rotation, port state,
///    table footprint) the ERD didn't include, needed to restore a scene
///    faithfully.
///  - Settings (added once a Settings UI existed — see SettingsPanelManager)
///    is a single global block, not per-level: it applies the same
///    regardless of which level is loaded. Grows field-by-field as each
///    settings feature is built rather than all at once.
/// </summary>
[Serializable]
public class SaveData
{
    public string playerId;     // Player.Player_ID — GUID, single local profile
    public string dateCreated;  // Player.Date_Created — ISO 8601, set once
    public string lastPlayed;   // Player.Last_Played — ISO 8601, updated on every Save()
    public List<LevelProgress> levels = new List<LevelProgress>();
    public SettingsData settings = new SettingsData();
}

/// <summary>Global player settings — not tied to any one level. Field defaults here
/// are what a save.json written before this feature existed silently gets on next
/// load, since JsonUtility leaves fields absent from old JSON at their initializer.</summary>
[Serializable]
public class SettingsData
{
    public float musicVolume = 0.8f;
    public float sfxVolume   = 0.8f;

    // Multiplier applied on top of every UI canvas's existing CanvasScaler
    // resolution scaling (see UIScale) — an accessibility-style "make
    // everything bigger/smaller" control, independent of screen resolution.
    public float uiScale = 1f;

    // Ordered palette slots for subnet coloring (see GameTheme.SubnetTint) —
    // starts at 5 and only grows via the settings carousel's '+' button.
    // Every subnet is hashed into one of these slots, so slots without a
    // player-chosen override still show (and are used by) GameTheme's own
    // generated default color for that slot index.
    public int subnetColorSlotCount = 5;
    public List<SubnetColorOverride> subnetColorOverrides = new List<SubnetColorOverride>();
}

/// <summary>One player-recolored palette slot — absent entries just mean
/// "keep the auto-generated default color for that slot".</summary>
[Serializable]
public class SubnetColorOverride
{
    public int slotIndex;
    public string colorHex; // RGB hex, e.g. "D9524D" — see ColorUtility.ToHtmlStringRGB
}

/// <summary>Progress_Data — one row per level this player has ever loaded.</summary>
[Serializable]
public class LevelProgress
{
    public int levelId;
    public bool completionStatus;
    public int score; // reserved — nothing computes a score yet, kept for schema fidelity
    public int starsEarned; // 1-3, from TaskManager.ComputeStars() at completion time; 0 = not yet completed
    public List<TaskStatus> tasks = new List<TaskStatus>();
    public TopologySave topology = new TopologySave();
}

/// <summary>Task — keyed by description since TaskManager.GameTask has no numeric ID
/// (tasks are registered at runtime by each level's own *Objectives component).</summary>
[Serializable]
public class TaskStatus
{
    public string description;
    public bool status;

    public TaskStatus() { }
    public TaskStatus(string description, bool status)
    {
        this.description = description;
        this.status = status;
    }
}

/// <summary>Topology — this level's full saved scene state.</summary>
[Serializable]
public class TopologySave
{
    public List<TableSave> tables = new List<TableSave>();
    public List<DeviceSave> devices = new List<DeviceSave>();
    public List<CableSave> cables = new List<CableSave>();
    public List<SubnetSave> subnets = new List<SubnetSave>();
}

/// <summary>Not an ERD entity — Table is its own component in this codebase, distinct
/// from Devices, and must exist before devices can be placed back onto it.</summary>
[Serializable]
public class TableSave
{
    public float x, y, z;
    public float width, length;
}

/// <summary>Devices.</summary>
[Serializable]
public class DeviceSave
{
    public string deviceId;   // FK target for CableSave — NetworkDevice.SaveId
    public string subnetId;   // FK to SubnetSave.subnetId; empty if unaddressed
    public string deviceType; // DeviceType.ToString()
    public float x, y, z;
    public float rotY;        // yaw — not in the ERD, needed to restore rotated devices
    public string ipAddress;
    public string ipMask;
    public bool[] portActive; // per-port up/down state — not in the ERD, drives Cable's packet-flow cosmetic

    // Router-only: per-interface IP/mask/shutdown, mirroring RouterInterfaceConfig
    // (see its doc comment). Empty/absent for non-router devices, or for a port
    // the player never configured — "keep them empty" rather than omit the row,
    // so a restored router still shows every eth<N> in "show ip interface brief".
    public List<InterfaceConfigSave> interfaces = new List<InterfaceConfigSave>();
}

/// <summary>One router interface's saved config — see DeviceSave.interfaces.</summary>
[Serializable]
public class InterfaceConfigSave
{
    public int port;
    public string ip = "";
    public string mask = "";
    public bool shutdown = true;
}

/// <summary>Cable.</summary>
[Serializable]
public class CableSave
{
    public string cableType = "Ethernet"; // reserved — Cable.cs has no type concept today, always this value
    public string sourceDeviceId;
    public int sourcePort;
    public string targetDeviceId;
    public int targetPort;
}

/// <summary>Subnet — derived from the distinct (ip &amp; mask) network addresses present
/// among a level's devices at save time, not separately authored by the player.</summary>
[Serializable]
public class SubnetSave
{
    public string subnetId;   // the network address in dotted form, e.g. "192.168.1.0"
    public string subnetName;
    public string subnetColorHex;
    public string gatewayAddress;
}
