using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds a standalone task-list canvas (separate from NetworkUIManager's HUD)
/// showing each TaskManager task with a label and an auto-checking checkbox.
/// Purely a display of TaskManager's state — it never sets IsComplete itself.
///
/// Visually matches the CLI redesign's chamfered-plate look (see
/// CLIPanelManager's "Drag Commands" sidebar) — a navy/blue PlateSpriteBuilder
/// plate with PixeloidMono text, rather than NetworkUIManager's older flat
/// sidebar color — so every HUD panel in the game now reads as one consistent
/// visual family. Fully layout-group driven — panel and row heights are never
/// hardcoded, they grow to fit wrapped task text; a sliced plate sprite
/// tolerates that auto-resize exactly like a flat color would.
/// </summary>
public class TaskUIManager : MonoBehaviour
{
    const string FontResourcePath = "Fonts/PixeloidMono SDF";
    TMP_FontAsset _font;

    // Same palette family as CLIPanelManager's sidebar/Run/queue-zone plates —
    // duplicated here (rather than shared) because each HUD panel class builds
    // its own UI independently (see ConfirmDialogManager/TutorialUIManager for
    // the same pattern), not because the colors are meant to drift apart.
    private static readonly Color TrimColor     = new Color(0.02f, 0.03f, 0.05f, 1f);
    private static readonly Color PanelFill     = new Color(0.055f, 0.07f, 0.11f, 1f); // navy
    private static readonly Color PanelBorder   = new Color(0.14f, 0.32f, 0.50f, 1f);  // blue
    private static readonly Color HeaderColor   = Color.white;
    private static readonly Color LabelColor    = new Color(0.88f, 0.92f, 0.97f, 1f);
    private static readonly Color BoxEmptyFill   = new Color(0.02f, 0.02f, 0.02f, 1f); // matches CLI's black queue-zone plate
    private static readonly Color BoxEmptyBorder = new Color(0.27f, 0.27f, 0.29f, 1f);
    private static readonly Color BoxDoneFill    = new Color(0.35f, 0.75f, 0.45f, 1f); // matches CLI's Run button plate
    private static readonly Color BoxDoneBorder  = new Color(0.15f, 0.45f, 0.22f, 1f);

    private const float PanelWidth     = 380f; // bigger than the old 260 baseline, to comfortably fit larger text
    private const float Padding        = 18f;
    private const float HeaderHeight   = 36f;
    private const float RowSpacing     = 14f;
    private const float RowInnerSpacing = 14f; // gap between checkbox and label within a row
    private const float CheckboxSize   = 24f;
    private const int   HeaderFontSize = 20;
    private const int   LabelFontSize  = 18;
    private const int   CheckFontSize  = 18;

    private RectTransform panelRect;
    private Transform rowContainer;
    private Sprite boxEmptySprite;
    private Sprite boxDoneSprite;
    private readonly List<(TaskManager.GameTask task, Image box, TextMeshProUGUI check)> rows = new();

    void Start()
    {
        _font = Resources.Load<TMP_FontAsset>(FontResourcePath);
        boxEmptySprite = SmallPlate(BoxEmptyFill, BoxEmptyBorder);
        boxDoneSprite  = SmallPlate(BoxDoneFill, BoxDoneBorder);

        BuildCanvas();

        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTasksChanged += RebuildRows;

        RebuildRows();
    }

    // Same thin-bordered small-plate family CLIPanelManager's chips/buttons
    // use (borderThickness=1, chamfer=2, pixelScale=2 → 6px border) — safe at
    // this checkbox's 24px size, where a PNG button's baked-in large border
    // would overlap/break (see CLIPanelManager.SmallPlate's own doc comment).
    static Sprite SmallPlate(Color fill, Color border) =>
        PlateSpriteBuilder.GetSprite(fill, border, TrimColor, borderThickness: 1, chamfer: 2, pixelScale: 2);

    void OnDestroy()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTasksChanged -= RebuildRows;
    }

    void Update()
    {
        // Cheap per-row refresh; RebuildRows only runs when the task list/state changes.
        foreach (var (task, box, check) in rows)
        {
            box.sprite = task.IsComplete ? boxDoneSprite : boxEmptySprite;
            check.enabled = task.IsComplete;
        }
    }

    // ── Canvas ───────────────────────────────────────────────────────────────

    void BuildCanvas()
    {
        var canvasGo = new GameObject("Task_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5; // below HUD_Canvas (10)

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("TaskPanel");
        panel.transform.SetParent(canvasGo.transform, false);
        var bg = panel.AddComponent<Image>();
        bg.sprite = PlateSpriteBuilder.GetSprite(PanelFill, PanelBorder, TrimColor,
            borderThickness: 2, chamfer: 5, pixelScale: 5);
        bg.type = Image.Type.Sliced;

        panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(1, 1);
        panelRect.anchorMax        = new Vector2(1, 1);
        panelRect.pivot            = new Vector2(1, 1);
        panelRect.anchoredPosition = new Vector2(-20, -20);
        panelRect.sizeDelta        = new Vector2(PanelWidth, HeaderHeight + Padding * 2);

        // Panel drives its own height from its children (header + separator + rows) —
        // width stays fixed at PanelWidth, only the vertical axis auto-fits.
        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment         = TextAnchor.UpperCenter;
        layout.childControlWidth      = true;
        layout.childForceExpandWidth  = true;
        layout.childControlHeight     = false;
        layout.childForceExpandHeight = false;
        layout.spacing                = RowSpacing;
        layout.padding                = new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding);

        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // width stays at PanelWidth
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Slide-in entrance, approaching from whichever edge this panel is
        // actually pinned to (top-right here) — same language as the main
        // menu's own slide-ins (see SlidePanel), just per-panel-directional.
        var slide = panel.AddComponent<SlidePanel>();
        slide.Configure(SlidePanel.EdgeFromAnchor(panelRect), PanelWidth);
        slide.SlideIn();

        // Header — same "title sits directly on the plate" look as the CLI
        // sidebar's "Drag Commands" title (see CLIPanelManager.BuildCommandBuilderPanel),
        // no separate background/separator of its own.
        var headerGo = new GameObject("Header");
        headerGo.transform.SetParent(panel.transform, false);
        var headerTmp = headerGo.AddComponent<TextMeshProUGUI>();
        headerTmp.font      = _font;
        headerTmp.text      = "TASKS";
        headerTmp.fontSize  = HeaderFontSize;
        headerTmp.fontStyle = FontStyles.Bold;
        headerTmp.color     = HeaderColor;
        headerTmp.alignment = TextAlignmentOptions.Center;
        var headerLe = headerGo.AddComponent<LayoutElement>();
        headerLe.preferredHeight = HeaderHeight;
        headerLe.minHeight       = HeaderHeight;

        // Rows are appended directly under the panel, after the header, so
        // the same VerticalLayoutGroup positions everything in one flow.
        rowContainer = panel.transform;
    }

    // ── Rows ─────────────────────────────────────────────────────────────────

    void RebuildRows()
    {
        foreach (Transform child in rowContainer)
            if (child.name.StartsWith("Row_")) Destroy(child.gameObject);
        rows.Clear();

        if (TaskManager.Instance == null) return;

        var tasks = TaskManager.Instance.Tasks;
        for (int i = 0; i < tasks.Count; i++)
            rows.Add(CreateRow(tasks[i], i));
    }

    (TaskManager.GameTask, Image, TextMeshProUGUI) CreateRow(TaskManager.GameTask task, int index)
    {
        var row = new GameObject($"Row_{index}");
        row.transform.SetParent(rowContainer, false);

        // Row height auto-fits its tallest child (the wrapped label), same
        // pattern the sidebar uses for its own buttons (height from content,
        // not a hardcoded constant).
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment         = TextAnchor.UpperLeft;
        rowLayout.childControlWidth      = true;
        rowLayout.childControlHeight     = true;
        rowLayout.childForceExpandWidth  = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.spacing                = RowInnerSpacing;

        var rowFitter = row.AddComponent<ContentSizeFitter>();
        rowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        rowFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Checkbox — small chamfered plate (same family as CLIPanelManager's
        // param-field/Run/Clear plates) instead of a flat-color square.
        var boxGo = new GameObject("Checkbox");
        boxGo.transform.SetParent(row.transform, false);
        var box = boxGo.AddComponent<Image>();
        box.sprite = task.IsComplete ? boxDoneSprite : boxEmptySprite;
        box.type   = Image.Type.Sliced;
        var boxLe = boxGo.AddComponent<LayoutElement>();
        boxLe.preferredWidth  = CheckboxSize;
        boxLe.preferredHeight = CheckboxSize;
        boxLe.minWidth        = CheckboxSize;
        boxLe.minHeight       = CheckboxSize;
        boxLe.flexibleWidth   = 0;
        boxLe.flexibleHeight  = 0;

        // Checkmark glyph, shown only when complete
        var checkGo = new GameObject("Check");
        checkGo.transform.SetParent(boxGo.transform, false);
        var check = checkGo.AddComponent<TextMeshProUGUI>();
        check.font      = _font;
        check.text      = "V"; // avoids relying on a checkmark glyph the default TMP font may not have
        check.fontSize  = CheckFontSize;
        check.alignment = TextAlignmentOptions.Center;
        check.color     = Color.white;
        check.fontStyle = FontStyles.Bold;
        check.enabled   = task.IsComplete;
        var crt = checkGo.GetComponent<RectTransform>();
        crt.anchorMin = Vector2.zero;
        crt.anchorMax = Vector2.one;
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;

        // TextColumn — label plus (optionally) revealed hint lines and a
        // "Hint" affordance, stacked vertically. Existing tasks (no hint text
        // on any tier) render exactly as before: just the label, no extra
        // lines — this column only ever adds rows, never removes the label.
        var columnGo = new GameObject("TextColumn");
        columnGo.transform.SetParent(row.transform, false);
        var columnLayout = columnGo.AddComponent<VerticalLayoutGroup>();
        columnLayout.childAlignment         = TextAnchor.UpperLeft;
        columnLayout.childControlWidth      = true;
        columnLayout.childForceExpandWidth  = true;
        columnLayout.childControlHeight     = false;
        columnLayout.childForceExpandHeight = false;
        columnLayout.spacing                = 4f;
        var columnFitter = columnGo.AddComponent<ContentSizeFitter>();
        columnFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        columnFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        var columnLe = columnGo.AddComponent<LayoutElement>();
        columnLe.flexibleWidth = 1;
        columnLe.minWidth      = 0;

        // Label — word-wraps within the column's width, which is what makes
        // the row (and therefore the panel) grow to fit longer tasks.
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(columnGo.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font               = _font;
        label.text               = task.Description;
        label.fontSize           = LabelFontSize;
        label.color              = LabelColor;
        label.alignment          = TextAlignmentOptions.TopLeft;
        label.enableWordWrapping = true;
        label.overflowMode       = TextOverflowModes.Overflow;

        for (int tier = 1; tier <= task.HighestHintTierUsed; tier++)
        {
            string text = HintTextForTier(task, tier);
            if (string.IsNullOrEmpty(text)) continue;

            var hintLineGo = new GameObject($"Hint{tier}");
            hintLineGo.transform.SetParent(columnGo.transform, false);
            var hintLine = hintLineGo.AddComponent<TextMeshProUGUI>();
            hintLine.font               = _font;
            hintLine.text               = text;
            hintLine.fontSize           = LabelFontSize - 3;
            hintLine.color              = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.7f);
            hintLine.fontStyle          = FontStyles.Italic;
            hintLine.alignment          = TextAlignmentOptions.TopLeft;
            hintLine.enableWordWrapping = true;
        }

        int nextTier = NextHintTier(task);
        if (!task.IsComplete && nextTier > 0)
        {
            var hintBtnGo = new GameObject("HintButton");
            hintBtnGo.transform.SetParent(columnGo.transform, false);
            var hintBtn = hintBtnGo.AddComponent<Button>();
            hintBtn.transition = Selectable.Transition.None;
            var hintBtnLabel = hintBtnGo.AddComponent<TextMeshProUGUI>();
            hintBtnLabel.font      = _font;
            hintBtnLabel.text      = task.HighestHintTierUsed == 0 ? "Hint ▸" : "More hint ▸";
            hintBtnLabel.fontSize  = LabelFontSize - 3;
            hintBtnLabel.color     = GameTheme.Info;
            hintBtnLabel.alignment = TextAlignmentOptions.TopLeft;
            hintBtnLabel.raycastTarget = true;
            hintBtn.targetGraphic = hintBtnLabel;
            int tierToReveal = nextTier;
            hintBtn.onClick.AddListener(() => TaskManager.Instance?.UseHint(task, tierToReveal));
        }

        return (task, box, check);
    }

    static string HintTextForTier(TaskManager.GameTask task, int tier) => tier switch
    {
        1 => task.HintNudge,
        2 => task.HintSpecific,
        3 => task.HintAnswer,
        _ => null,
    };

    // First tier beyond what's already been used whose text is actually
    // populated — 0 if nothing further is available. Lets a level author
    // leave a tier blank (e.g. no "specific" hint, just nudge + answer)
    // without the button getting stuck offering an empty reveal.
    static int NextHintTier(TaskManager.GameTask task)
    {
        for (int tier = task.HighestHintTierUsed + 1; tier <= 3; tier++)
            if (!string.IsNullOrEmpty(HintTextForTier(task, tier))) return tier;
        return 0;
    }
}
