using UnityEngine;

/// <summary>
/// Marks an object (the camera/light LevelTemplateBaker bakes into every
/// level-template scene — see LevelTemplateAuthoringWindow) as existing only
/// for previewing that template scene on its own in the Editor. Self-destroys
/// the instant a GameBootstrap turns up anywhere in the loaded scene set —
/// which is exactly what happens when TemplateTopologyLoader merges this
/// scene additively into SampleScene during real gameplay, since
/// GameBootstrap.SetupCamera()/SetupLighting() already built the camera/light
/// the game actually uses by that point. Without this, normal play would end
/// up with two "MainCamera"-tagged Cameras (and an extra AudioListener/Light)
/// at once. Only when the template scene is opened and played standalone —
/// no GameBootstrap anywhere — does the object this is attached to survive,
/// which is the whole point: letting a level's starting topology be previewed
/// without routing through the full game bootstrap.
/// </summary>
public class TemplatePreviewOnly : MonoBehaviour
{
    void Awake()
    {
        if (Object.FindAnyObjectByType<GameBootstrap>() != null)
            Destroy(gameObject);
    }
}
