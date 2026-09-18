using UnityEngine.SceneManagement;

/// <summary>
/// Naming/lookup for a level's authored starting-topology scene (see the Editor
/// "Save Play Topology As Level Template" tool, LevelTemplateAuthoringWindow).
/// A template scene holds nothing but the pre-placed tables/devices/cables for
/// one level — GameBootstrap loads it additively via TemplateTopologyLoader when
/// the player has no save of their own for that level yet. Kept as its own tiny
/// static class (rather than inlined in GameBootstrap) so both runtime and the
/// Editor tool agree on the exact same path/name without duplicating it.
/// </summary>
public static class LevelTemplateScene
{
    const string Folder = "Assets/Scenes/LevelTemplates";

    public static string SceneName(int level) => $"Level{level}_Template";
    public static string ScenePath(int level) => $"{Folder}/{SceneName(level)}.unity";

    /// <summary>True once this level's template scene has been baked and added to
    /// Build Settings — checked via build index rather than a file-existence check
    /// since SceneManager.LoadScene needs the former anyway (works identically in
    /// the Editor and in a built player).</summary>
    public static bool Exists(int level) =>
        SceneUtility.GetBuildIndexByScenePath(ScenePath(level)) >= 0;
}
