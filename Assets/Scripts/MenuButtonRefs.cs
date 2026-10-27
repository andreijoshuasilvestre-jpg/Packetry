using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One button's wireable pieces, shared by MainMenuShellView and
/// LevelSelectShellView. A shell builder (MainMenuShellBuilder /
/// LevelSelectShellBuilder) fills in Button/Fx/VisualRect as pure structure
/// — sprites, colors, layout — but never calls Fx.Init() and never wires
/// Button.onClick. The live owner (MainMenuBootstrap / LevelSelectManager)
/// does both once the button actually sits under a real Canvas, whether that
/// happened via the runtime fallback builder, Resources.Load + Instantiate,
/// or a pre-placed scene instance — see each shell builder's class doc.
/// </summary>
[System.Serializable]
public class MenuButtonRefs
{
    public Button Button;
    public MenuButtonFX Fx; // null for a locked card button (see LevelSelectManager.CreateButton) — no hover FX on those
    public RectTransform VisualRect; // the part MenuButtonFX bounce-scales on click
    public SlidePanel Slide; // null unless the owner wants this button to slide in/out of frame individually (see MainMenuBootstrap's menu-level-select transition)
}
