using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Typed handles onto the static level-select shell — panel scaffold, title,
/// back button, carousel frame (empty until LevelSelectManager populates it
/// with cards), and description popup — that LevelSelectShellBuilder
/// constructs. Attached to the same GameObject as LevelSelectManager
/// (LevelSelectShellBuilder adds both), so Build() can grab everything it
/// needs from one reference. Pure structure: LevelSelectManager wires every
/// click handler and MenuButtonFX.Init() call itself once these objects
/// actually exist live — baked prefab, runtime fallback, or pre-placed in
/// the scene — see LevelSelectManager.Build().
/// </summary>
public class LevelSelectShellView : MonoBehaviour
{
    public SlidePanel PanelSlide;
    public RectTransform ContentRt;
    public VerticalLayoutGroup ContentLayout; // padding recomputed at runtime once the level count is known — see LevelSelectManager
    public SmoothScrollRect ScrollRect;
    public LevelCarousel Carousel;

    public MenuButtonRefs BackButton;

    public GameObject DescDim;
    public SlidePanel DescSlide;
    public TextMeshProUGUI DescTitleTmp;
    public TextMeshProUGUI DescCategoryTmp;
    public TextMeshProUGUI DescBodyTmp;
    public List<Image> DescIconSlots = new List<Image>();
    public MenuButtonRefs CancelButton;
    public MenuButtonRefs PlayButton;
}
