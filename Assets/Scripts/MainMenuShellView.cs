using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Typed handles onto the static main-menu shell (logo + the fixed row of
/// menu buttons) that MainMenuShellBuilder constructs — either baked once
/// into Assets/Resources/MainMenuPrefabs/MainMenuShell.prefab by
/// MainMenuUIBaker, instantiated from that prefab at runtime, or built live
/// as a last-resort fallback if the prefab is missing (see
/// MainMenuBootstrap). MainMenuBootstrap reads these fields to wire click
/// handlers and MenuButtonFX.Init() itself —
/// this component is pure structure, no behavior of its own.
/// </summary>
public class MainMenuShellView : MonoBehaviour
{
    public RectTransform LogoVisual;
    public CanvasGroup LogoRevealGroup; // see MenuRevealFlash / MainMenuBootstrap.PlayFirstAppearanceReveal
    public Image LogoGlow;
    public SlidePanel LogoSlide; // logo's own in/out-of-frame motion — see MainMenuBootstrap.PlayMenuExit/PlayMenuEnter

    public MenuButtonRefs Play;
    public MenuButtonRefs Settings;
    public MenuButtonRefs Feedback;
    public MenuButtonRefs Quit;
}
