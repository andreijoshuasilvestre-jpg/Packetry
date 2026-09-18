using System;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Static save/load facade — the project's only persistence layer (see
/// SaveData for the schema and the deviations from the ERD it's modeled on).
/// Plain static class rather than a MonoBehaviour singleton: it has no
/// per-frame behaviour and needs to be readable from both MainMenu
/// (LevelSelectManager's lock/unlock check) and SampleScene (GameBootstrap,
/// AutoSaveManager, TopologyLoader) without depending on scene lifecycle —
/// same reasoning as SubnetMath/DeviceModelBuilder already being static.
/// </summary>
public static class SaveManager
{
    const string FileName = "save.json";

    static SaveData current;

    /// <summary>The in-memory save, loaded from disk on first access.</summary>
    public static SaveData Current
    {
        get
        {
            if (current == null) Load();
            return current;
        }
    }

    static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                current = JsonUtility.FromJson<SaveData>(json);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] Failed to load {FilePath}: {e.Message}");
        }

        if (current == null) current = CreateNew();
    }

    static SaveData CreateNew()
    {
        string now = DateTime.UtcNow.ToString("o");
        return new SaveData
        {
            playerId = Guid.NewGuid().ToString(),
            dateCreated = now,
            lastPlayed = now
        };
    }

    public static void Save()
    {
        Current.lastPlayed = DateTime.UtcNow.ToString("o");
        try
        {
            string json = JsonUtility.ToJson(Current, true);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] Failed to save {FilePath}: {e.Message}");
        }
    }

    // ── Level progress ──────────────────────────────────────────────────────

    /// <summary>Finds or creates (and appends) this level's Progress_Data row.</summary>
    public static LevelProgress GetOrCreate(int levelId)
    {
        var existing = Current.levels.FirstOrDefault(l => l.levelId == levelId);
        if (existing != null) return existing;

        var created = new LevelProgress { levelId = levelId };
        Current.levels.Add(created);
        return created;
    }

    public static bool IsLevelCompleted(int levelId) =>
        Current.levels.FirstOrDefault(l => l.levelId == levelId)?.completionStatus ?? false;

    /// <summary>Level 1 is always unlocked; level N unlocks once level N-1 is completed.</summary>
    public static bool IsLevelUnlocked(int levelId) =>
        levelId <= 1 || IsLevelCompleted(levelId - 1);

    public static void MarkLevelCompleted(int levelId)
    {
        GetOrCreate(levelId).completionStatus = true;
        Save();
    }

    /// <summary>1-3, from TaskManager.ComputeStars() at the moment this level was last
    /// completed; 0 if never completed. Not touched by ClearLevelProgress — a
    /// replay keeps showing the previously-earned rating until it's completed
    /// again, same as completionStatus itself.</summary>
    public static int GetStars(int levelId) =>
        Current.levels.FirstOrDefault(l => l.levelId == levelId)?.starsEarned ?? 0;

    public static void SetStars(int levelId, int stars)
    {
        GetOrCreate(levelId).starsEarned = stars;
        Save();
    }

    /// <summary>Overwrites this level's saved topology/task snapshot in memory (does not
    /// write to disk — call Save() after, same as MarkLevelCompleted does itself).</summary>
    public static void SaveTopologySnapshot(int levelId, TopologySave topology)
    {
        GetOrCreate(levelId).topology = topology;
    }

    public static void SaveTaskSnapshot(int levelId, System.Collections.Generic.List<TaskStatus> tasks)
    {
        GetOrCreate(levelId).tasks = tasks;
    }

    /// <summary>True once this level has a previously-saved topology worth restoring
    /// instead of building that level's default starting content from scratch.</summary>
    public static bool HasSavedTopology(int levelId)
    {
        var progress = Current.levels.FirstOrDefault(l => l.levelId == levelId);
        return progress != null && progress.topology != null && progress.topology.devices.Count > 0;
    }

    // ── Settings (global, not per-level) ────────────────────────────────────

    /// <summary>Old save.json files predate SettingsData — JsonUtility leaves the
    /// field at its own `= new SettingsData()` initializer in that case, but guard
    /// anyway in case a save was ever hand-edited/corrupted to null it out.</summary>
    public static SettingsData Settings => Current.settings ??= new SettingsData();

    /// <summary>Player-chosen color for a subnet-palette slot, or null if that slot
    /// still uses GameTheme's auto-generated default.</summary>
    public static string GetSubnetSlotColorHex(int slotIndex) =>
        Settings.subnetColorOverrides.FirstOrDefault(o => o.slotIndex == slotIndex)?.colorHex;

    public static void SetSubnetSlotColor(int slotIndex, string colorHex)
    {
        var existing = Settings.subnetColorOverrides.FirstOrDefault(o => o.slotIndex == slotIndex);
        if (existing != null) existing.colorHex = colorHex;
        else Settings.subnetColorOverrides.Add(new SubnetColorOverride { slotIndex = slotIndex, colorHex = colorHex });
        Save();
    }

    /// <summary>Adds one more customizable slot (the carousel's '+' button) and
    /// returns its index.</summary>
    public static int AddSubnetColorSlot()
    {
        Settings.subnetColorSlotCount++;
        Save();
        return Settings.subnetColorSlotCount - 1;
    }

    /// <summary>Wipes this level's saved topology/task snapshot (but keeps
    /// completionStatus, so replaying a finished level never re-locks the one
    /// after it) — the next load of this level rebuilds its default starting
    /// content instead of restoring the stale attempt. Used by the in-HUD
    /// "Return" button (NetworkUIManager) to make replaying a level actually
    /// start over, not just reload the same in-progress save.</summary>
    public static void ClearLevelProgress(int levelId)
    {
        var progress = Current.levels.FirstOrDefault(l => l.levelId == levelId);
        if (progress == null) return;

        progress.topology = null;
        progress.tasks = null;
        Save();
    }
}
