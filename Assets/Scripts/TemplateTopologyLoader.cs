using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads this level's authored template scene (see LevelTemplateScene) additively,
/// merging its pre-placed tables/devices/cables straight into the running scene.
/// GameBootstrap.SetupLevelContent attaches this instead of TopologyLoader when the
/// player has never played this level but a designer-authored starting topology
/// exists for it — instead of / in addition to a hand-written LevelNSceneSetup.
/// </summary>
public class TemplateTopologyLoader : MonoBehaviour
{
    string sceneName;
    System.Action onLoaded;

    /// <summary>
    /// Starts the additive load. Unlike a saved-topology restore, the merge
    /// never lands inline — it arrives a frame or more later (more when this
    /// scene was itself activated through LoadingScreenManager's async load).
    /// onLoaded fires from SceneManager.sceneLoaded, i.e. right after every
    /// merged object's Awake/OnEnable but before any of them renders or runs
    /// Start(), so the caller can hide the merged topology for its build-in
    /// the same frame it appears instead of polling for it.
    ///
    /// Just the load otherwise — Cable self-repairs its own endpoint links
    /// and registers itself with CableManager in its own Awake() (see Cable.
    /// TryRestoreLinks), since that has to work whether this scene is merged
    /// in here or opened and played completely standalone.
    /// </summary>
    public void Load(int level, System.Action onLoaded)
    {
        sceneName = LevelTemplateScene.SceneName(level);
        this.onLoaded = onLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != sceneName) return;
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        var callback = onLoaded;
        onLoaded = null;
        callback?.Invoke();
    }

    void OnDestroy() => SceneManager.sceneLoaded -= HandleSceneLoaded;
}
