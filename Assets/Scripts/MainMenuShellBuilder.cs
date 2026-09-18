using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds the static main-menu shell — logo + the fixed row of menu buttons
/// (Play/Tutorial/Settings/Feedback/Skip Tutorial (Dev)) — as a flat,
/// undistorted layout under a Screen Space - Overlay canvas (no perspective
/// tilt, no cursor-parallax drift), so it renders identically in the Scene
/// view and in Play without needing a camera. Pure structure: assigns
/// MenuButtonFX's sprite/color fields but never calls Fx.Init() and never
/// wires Button.onClick — MainMenuBootstrap does both once this shell
/// actually lives under a real Canvas (see MainMenuShellView).
///
/// Called from two places that must never visually diverge: MainMenuUIBaker
/// (Editor tool, bakes the result once into
/// Assets/Resources/MainMenuPrefabs/MainMenuShell.prefab) and
/// MainMenuBootstrap's runtime fallback (used only if that prefab hasn't
/// been generated yet, or nothing is pre-placed in the scene).
/// </summary>
public static class MainMenuShellBuilder
{
    const string SpriteFolder = "MainMenuUI/";
    const float LogoWidth   = 620f;
    const float ButtonWidth = 560f;
    const float MenuMargin  = 210f; // shared left/right breathing room for the whole menu block (logo + buttons)
    const float ButtonGap   = 36f;

    static readonly string[] ButtonOrder = { "Play", "Tutorial", "Settings", "Feedback", "Skip Tutorial (Dev)" };

    /// <summary>Builds the MenuRoot hierarchy under `parent` and returns its view.</summary>
    public static MainMenuShellView Build(RectTransform parent, TMP_FontAsset font, Color textColor)
    {
        var rootGo = new GameObject("MenuRoot", typeof(RectTransform));
        rootGo.transform.SetParent(parent, false);
        var rootRt = rootGo.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        rootRt.pivot     = new Vector2(0.5f, 0.5f);

        var view = rootGo.AddComponent<MainMenuShellView>();
        view.Slide = rootGo.AddComponent<SlidePanel>();

        BuildLogo(rootRt, view);
        BuildButtons(rootRt, font, textColor, view);

        return view;
    }

    static void BuildLogo(RectTransform root, MainMenuShellView view)
    {
        var sprite = Resources.Load<Sprite>(SpriteFolder + "Logo");
        if (sprite == null) return;

        float aspect = sprite.rect.width / sprite.rect.height;
        float height = LogoWidth / aspect;
        float logoX  = MenuMargin;

        var anchorGo = new GameObject("Logo", typeof(RectTransform));
        anchorGo.transform.SetParent(root, false);
        var anchorRt = anchorGo.GetComponent<RectTransform>();
        anchorRt.anchorMin        = new Vector2(0f, 0.5f);
        anchorRt.anchorMax        = new Vector2(0f, 0.5f);
        anchorRt.pivot            = new Vector2(0f, 0.5f);
        anchorRt.anchoredPosition = new Vector2(logoX, 0f);
        anchorRt.sizeDelta        = new Vector2(LogoWidth, height);

        var visualGo = new GameObject("Visual", typeof(RectTransform));
        visualGo.transform.SetParent(anchorGo.transform, false);
        var vrt = visualGo.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;

        var img = visualGo.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;

        view.LogoVisual = vrt;
    }

    static void BuildButtons(RectTransform root, TMP_FontAsset font, Color textColor, MainMenuShellView view)
    {
        var normalSprite = Resources.Load<Sprite>(SpriteFolder + "BlankButton");
        var hoverSprite  = Resources.Load<Sprite>(SpriteFolder + "BlankButton_Hover");

        float aspect       = normalSprite != null ? normalSprite.rect.width / normalSprite.rect.height : 3.26f;
        float buttonHeight = ButtonWidth / aspect;
        float totalHeight  = ButtonOrder.Length * buttonHeight + (ButtonOrder.Length - 1) * ButtonGap;

        var stackGo = new GameObject("ButtonStack", typeof(RectTransform));
        stackGo.transform.SetParent(root, false);
        var stackRt = stackGo.GetComponent<RectTransform>();
        stackRt.anchorMin        = new Vector2(1f, 0.5f);
        stackRt.anchorMax        = new Vector2(1f, 0.5f);
        stackRt.pivot            = new Vector2(1f, 0.5f);
        stackRt.anchoredPosition = new Vector2(-MenuMargin, 0f);
        stackRt.sizeDelta        = new Vector2(ButtonWidth, totalHeight);

        // "StackVisual" name/level kept (rather than parenting buttons
        // directly under ButtonStack) so the hierarchy shape matches what it
        // was when this carried the diegetic tilt — no functional need for
        // the extra level now, but no reason to reshuffle it either.
        var stackVisualGo = new GameObject("StackVisual", typeof(RectTransform));
        stackVisualGo.transform.SetParent(stackGo.transform, false);
        var stackVisualRt = stackVisualGo.GetComponent<RectTransform>();
        stackVisualRt.anchorMin = Vector2.zero;
        stackVisualRt.anchorMax = Vector2.one;
        stackVisualRt.offsetMin = Vector2.zero;
        stackVisualRt.offsetMax = Vector2.zero;

        float cursorY = 0f;
        foreach (var label in ButtonOrder)
        {
            var refs = CreateMenuButton(stackVisualRt, label, new Vector2(0f, cursorY), buttonHeight, font, textColor, normalSprite, hoverSprite);
            cursorY -= buttonHeight + ButtonGap;

            switch (label)
            {
                case "Play":                 view.Play         = refs; break;
                case "Tutorial":              view.Tutorial     = refs; break;
                case "Settings":              view.Settings     = refs; break;
                case "Feedback":              view.Feedback     = refs; break;
                case "Skip Tutorial (Dev)":   view.SkipTutorial = refs; break;
            }
        }
    }

    static MenuButtonRefs CreateMenuButton(Transform parent, string label, Vector2 anchoredPos, float height,
                                            TMP_FontAsset font, Color textColor, Sprite normalSprite, Sprite hoverSprite)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = new Vector2(0f, height);

        var visualGo = new GameObject("Visual", typeof(RectTransform));
        visualGo.transform.SetParent(go.transform, false);
        var vrt = visualGo.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;

        // Glow is a child of Visual (not a sibling) so hand-moving/resizing
        // Visual in the Editor carries the glow along with it. First child
        // so it renders behind Background/Label. MenuButtonFX.ClickRoutine
        // compensates its own scale animation for the inherited parent scale
        // this creates, so the glow's peak size still matches its old,
        // unscaled-sibling look exactly.
        var glowGo = new GameObject("Glow", typeof(RectTransform));
        glowGo.transform.SetParent(visualGo.transform, false);
        var glowImg = glowGo.AddComponent<Image>();
        glowImg.raycastTarget = false;
        // Invisible at rest — Fx.Init() (deferred to MainMenuBootstrap's
        // wiring step, see class doc) reassigns this using glowColor once
        // live, but a pre-placed/baked instance is never guaranteed to have
        // Init() run on it before being viewed in the Editor, so it needs a
        // sane default here too rather than Unity's opaque-white Image default.
        glowImg.color = new Color(0f, 0f, 0f, 0f);
        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin        = new Vector2(0.5f, 0.5f);
        grt.anchorMax        = new Vector2(0.5f, 0.5f);
        grt.pivot            = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = Vector2.zero;
        grt.sizeDelta        = new Vector2(ButtonWidth * 1.15f, height * 1.6f);

        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(visualGo.transform, false);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.sprite = normalSprite;
        bgImg.preserveAspect = true; // scale, don't stretch — the container is already sized to this sprite's aspect
        var brt = bgGo.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(visualGo.transform, false);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text          = label.ToUpperInvariant();
        tmp.font          = font;
        tmp.fontSize      = 42;
        tmp.color         = textColor;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.fontStyle     = FontStyles.Bold;
        tmp.raycastTarget = false;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        var btn = go.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None; // FX component owns all visual feedback
        btn.targetGraphic = bgImg;

        var fx = go.AddComponent<MenuButtonFX>();
        fx.visualRect      = vrt;
        fx.backgroundImage = bgImg;
        fx.glowImage       = glowImg;
        fx.normalSprite    = normalSprite;
        fx.hoverSprite     = hoverSprite;
        fx.glowColor       = GameTheme.ElectricBlue;
        // Fx.Init() and Button.onClick wiring happen in MainMenuBootstrap,
        // once this shell lives under a real Canvas — see MainMenuShellView.

        return new MenuButtonRefs { Button = btn, Fx = fx, VisualRect = vrt };
    }
}
