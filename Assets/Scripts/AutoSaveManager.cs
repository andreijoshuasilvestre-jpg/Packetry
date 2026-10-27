using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Subscribes to every event that changes a level's placed content and
/// re-saves that level's topology/task snapshot whenever one fires — this is
/// what makes "progress is saved" true even if the player quits mid-level,
/// not just on level completion (see TutorialManager.CompleteTutorial /
/// Level2Guide / Level3Guide for the completion-time save, which calls
/// SaveNow too).
///
/// Deliberately level-agnostic, same reasoning as TaskManager itself — it
/// doesn't know or care which level is active, it just snapshots whatever
/// LevelManager.CurrentLevel currently is.
/// </summary>
public class AutoSaveManager : MonoBehaviour
{
    public static AutoSaveManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped += HandleDeviceDropped;
        if (CableManager.Instance != null)
        {
            CableManager.Instance.OnCableCreated += HandleCableChanged;
            CableManager.Instance.OnCableRemoved += HandleCableChanged;
        }
        if (TableManager.Instance != null)
        {
            TableManager.Instance.OnTablePlaced  += HandleTableChanged;
            TableManager.Instance.OnTableResized += HandleTableResized;
        }
        if (TaskManager.Instance != null) TaskManager.Instance.OnTasksChanged += HandleTasksChanged;
        if (DeviceRemoverManager.Instance != null) DeviceRemoverManager.Instance.OnDeviceRemoved += HandleDeviceRemoved;
        if (TableRemoverManager.Instance != null) TableRemoverManager.Instance.OnTableRemoved += HandleTableRemoved;
        // Router interface IPs only ever change through CLI commands, which
        // aren't covered by any event above — save once the player is done
        // editing a device's CLI panel, not on every keystroke.
        if (CLIPanelManager.Instance != null) CLIPanelManager.Instance.OnPanelClosed += HandlePanelClosed;
    }

    void OnDestroy()
    {
        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped -= HandleDeviceDropped;
        if (CableManager.Instance != null)
        {
            CableManager.Instance.OnCableCreated -= HandleCableChanged;
            CableManager.Instance.OnCableRemoved -= HandleCableChanged;
        }
        if (TableManager.Instance != null)
        {
            TableManager.Instance.OnTablePlaced  -= HandleTableChanged;
            TableManager.Instance.OnTableResized -= HandleTableResized;
        }
        if (TaskManager.Instance != null) TaskManager.Instance.OnTasksChanged -= HandleTasksChanged;
        if (DeviceRemoverManager.Instance != null) DeviceRemoverManager.Instance.OnDeviceRemoved -= HandleDeviceRemoved;
        if (TableRemoverManager.Instance != null) TableRemoverManager.Instance.OnTableRemoved -= HandleTableRemoved;
        if (CLIPanelManager.Instance != null) CLIPanelManager.Instance.OnPanelClosed -= HandlePanelClosed;
    }

    void HandleDeviceDropped(NetworkDevice device, bool isNewDevice) => SaveNow();
    void HandleCableChanged(Cable cable) => SaveNow();
    void HandleTableChanged(Table table) => SaveNow();
    void HandleTableResized(Table table, Vector3 oldCenter, float oldWidth, float oldLength) => SaveNow();
    void HandleTasksChanged() => SaveNow();
    void HandleDeviceRemoved(NetworkDevice device) => SaveNow();
    void HandleTableRemoved(Table table) => SaveNow();
    void HandlePanelClosed(NetworkDevice device) => SaveNow();

    /// <summary>Captures the live scene and writes it to disk for the current level.
    /// Public so level-setup scripts (Level2SceneSetup, Level3SceneSetup, Level7SceneSetup) can trigger
    /// the very first snapshot right after building their default content, instead of
    /// waiting for the player to cause a qualifying event first.</summary>
    public void SaveNow()
    {
        if (LevelManager.Instance == null) return;
        // The level is tearing down (see GameBootstrap.ExitStarted) — a panel
        // closing on the way out mustn't re-save over a restart's cleared progress.
        if (GameBootstrap.ExitStarted) return;
        int level = LevelManager.Instance.CurrentLevel;

        SaveManager.SaveTopologySnapshot(level, SceneStateCapture.Capture());

        if (TaskManager.Instance != null)
        {
            var tasks = new List<TaskStatus>();
            foreach (var t in TaskManager.Instance.Tasks) tasks.Add(new TaskStatus(t.Description, t.IsComplete));
            SaveManager.SaveTaskSnapshot(level, tasks);
        }

        SaveManager.Save();
    }
}
