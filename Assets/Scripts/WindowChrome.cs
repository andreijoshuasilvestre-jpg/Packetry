using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drag-by-title-bar behavior for a floating annotation/panel — attach to a
/// title bar's own Image (raycastTarget must stay true so it receives drag
/// events), point windowRoot at the RectTransform that should actually move
/// (the panel itself, not the title bar) and canvas at the owning Canvas.
/// Delta is divided by canvas.scaleFactor so a drag tracks the cursor 1:1
/// regardless of the player's UI Scale setting (same convention
/// CompoundSidebarGroup/DragDropManager use for screen-space UI math
/// elsewhere in this project).
/// </summary>
public class DraggableWindowChrome : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    public RectTransform windowRoot;
    public Canvas canvas;

    // Fires once per drag gesture, the moment it starts — callers
    // (TutorialUIManager) use this to stop their own auto-repositioning from
    // fighting a manual drag until the player is shown a fresh step.
    public System.Action OnUserDragged;

    public void OnBeginDrag(PointerEventData eventData) => OnUserDragged?.Invoke();

    public void OnDrag(PointerEventData eventData)
    {
        if (windowRoot == null) return;
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        windowRoot.anchoredPosition += eventData.delta / scale;
    }
}

/// <summary>
/// Builds a small Windows-style title bar (drag-grip + title label + optional
/// close "x") as a child strip sitting directly above a panel's own top edge
/// — shared chrome for every floating annotation in the game (TutorialUIManager's
/// callout, ConceptCardManager's concept cards, AnnotationWarningManager's
/// error/warning windows) so all of them read as the same draggable "window"
/// and the player can always shove one out of the way instead of it blocking
/// the canvas underneath. Anchored via top-center fractions (not the parent's
/// own pivot), so it tracks correctly even against panels whose height
/// changes at runtime (e.g. TutorialUIManager's auto-sized callout).
/// </summary>
public static class WindowChromeBuilder
{
    public const float BarHeight = 26f; // public: TutorialUIManager's placement counts the bar as part of the callout's footprint
    static readonly Color32 BarFill   = new Color32(18, 26, 40, 255);
    static readonly Color32 BarBorder = new Color32(45, 70, 110, 255);
    static readonly Color32 BarTrim   = new Color32(5, 8, 13, 255);

    /// <summary>Builds the title bar as a child of `panel` and wires it to drag
    /// `windowRoot`. Returns the close button (null if showCloseButton is false)
    /// so the caller can wire its own dismiss behavior.</summary>
    public static Button Build(Transform panel, RectTransform windowRoot, Canvas canvas, TMP_FontAsset font,
        string title, float width, Color accentColor, bool showCloseButton, System.Action onUserDragged = null)
    {
        var barGo = new GameObject("TitleBar", typeof(RectTransform));
        barGo.transform.SetParent(panel, false);
        var barRt = barGo.GetComponent<RectTransform>();
        barRt.anchorMin        = new Vector2(0.5f, 1f);
        barRt.anchorMax        = new Vector2(0.5f, 1f);
        barRt.pivot             = new Vector2(0.5f, 0f);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta        = new Vector2(width, BarHeight);

        var bg = barGo.AddComponent<Image>();
        bg.sprite = PlateSpriteBuilder.GetSprite(BarFill, BarBorder, BarTrim, 2, 8, 4);
        bg.type = Image.Type.Sliced;

        var gripGo = new GameObject("Grip", typeof(RectTransform));
        gripGo.transform.SetParent(barGo.transform, false);
        var grip = gripGo.AddComponent<Image>();
        grip.color = accentColor;
        grip.raycastTarget = false;
        var gripRt = gripGo.GetComponent<RectTransform>();
        gripRt.anchorMin        = new Vector2(0f, 0.5f);
        gripRt.anchorMax        = new Vector2(0f, 0.5f);
        gripRt.pivot             = new Vector2(0f, 0.5f);
        gripRt.anchoredPosition = new Vector2(8f, 0f);
        gripRt.sizeDelta        = new Vector2(6f, 14f);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(barGo.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font      = font;
        label.text      = title;
        label.fontSize  = 13;
        label.fontStyle = FontStyles.Bold;
        label.color     = new Color(0.85f, 0.90f, 0.97f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(20f, 0f);
        labelRt.offsetMax = new Vector2(showCloseButton ? -28f : -8f, 0f);

        var chrome = barGo.AddComponent<DraggableWindowChrome>();
        chrome.windowRoot = windowRoot;
        chrome.canvas = canvas;
        chrome.OnUserDragged = onUserDragged;

        if (!showCloseButton) return null;

        var closeGo = new GameObject("Close", typeof(RectTransform));
        closeGo.transform.SetParent(barGo.transform, false);
        var closeImg = closeGo.AddComponent<Image>();
        closeImg.color = new Color(1f, 1f, 1f, 0.08f);
        var closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin        = new Vector2(1f, 0.5f);
        closeRt.anchorMax        = new Vector2(1f, 0.5f);
        closeRt.pivot             = new Vector2(1f, 0.5f);
        closeRt.anchoredPosition = new Vector2(-4f, 0f);
        closeRt.sizeDelta        = new Vector2(20f, 20f);

        var xLabelGo = new GameObject("X", typeof(RectTransform));
        xLabelGo.transform.SetParent(closeGo.transform, false);
        var xLabel = xLabelGo.AddComponent<TextMeshProUGUI>();
        xLabel.font      = font;
        xLabel.text      = "x";
        xLabel.fontSize  = 14;
        xLabel.fontStyle = FontStyles.Bold;
        xLabel.color     = new Color(0.85f, 0.90f, 0.97f);
        xLabel.alignment = TextAlignmentOptions.Center;
        xLabel.raycastTarget = false;
        var xRt = xLabelGo.GetComponent<RectTransform>();
        xRt.anchorMin = Vector2.zero;
        xRt.anchorMax = Vector2.one;
        xRt.offsetMin = Vector2.zero;
        xRt.offsetMax = Vector2.zero;

        var closeBtn = closeGo.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        return closeBtn;
    }
}
