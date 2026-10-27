using UnityEngine;

/// <summary>
/// Restores a previously-saved level's topology (tables/devices/cables) and
/// task-completion state at level start. Level-agnostic — any level with a
/// saved snapshot uses this same component instead of that level's own
/// first-time *SceneSetup (see GameBootstrap.SetupLevelContent, which picks
/// one or the other based on SaveManager.HasSavedTopology).
/// </summary>
public class TopologyLoader : MonoBehaviour
{
    void Awake()
    {
        int level = LevelManager.Instance.CurrentLevel;
        var progress = SaveManager.GetOrCreate(level);
        SceneStateCapture.Restore(progress.topology);

        // Handed over now, applied later — the level's objectives register
        // their tasks a frame or more after this, and TaskManager marks each
        // one complete as it registers if the save had it complete.
        TaskManager.Instance?.RestoreCompleted(progress.tasks);
    }
}
