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
    // Just the load — Cable now self-repairs its own endpoint links and
    // registers itself with CableManager in its own Awake() (see Cable.
    // TryRestoreLinks), since that has to work whether this scene is merged
    // in here or opened and played completely standalone.
    void Awake()
    {
        int level = LevelManager.Instance.CurrentLevel;
        SceneManager.LoadScene(LevelTemplateScene.SceneName(level), LoadSceneMode.Additive);
    }
}
