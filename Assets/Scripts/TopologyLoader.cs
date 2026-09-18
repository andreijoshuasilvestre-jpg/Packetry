using System.Collections;
using System.Linq;
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

        StartCoroutine(ApplySavedTaskStateNextFrame(progress));
    }

    // TaskManager tasks are registered by that level's own *Objectives
    // component in ITS Start() — deferring one frame guarantees registration
    // has already happened, regardless of Start() call order between sibling
    // components created in the same GameBootstrap.Awake() pass.
    IEnumerator ApplySavedTaskStateNextFrame(LevelProgress progress)
    {
        yield return null;

        if (TaskManager.Instance == null) yield break;

        foreach (var saved in progress.tasks)
        {
            if (!saved.status) continue;
            var task = TaskManager.Instance.Tasks.FirstOrDefault(t => t.Description == saved.description);
            if (task != null) task.IsComplete = true;
        }
    }
}
