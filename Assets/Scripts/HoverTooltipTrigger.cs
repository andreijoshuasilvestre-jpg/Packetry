using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Per-target hover hook — see HoverTooltip.Attach. Only pointer
/// enter/exit, deliberately: claiming press or drag events here would steal
/// them from the button/ScrollRect the target already belongs to.</summary>
public class HoverTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Func<TooltipContent> Content;
    public TooltipSide Side = TooltipSide.Right;
    public float Delay = HoverTooltip.DefaultDelay;

    public void OnPointerEnter(PointerEventData eventData) => HoverTooltip.Enter(this);
    public void OnPointerExit(PointerEventData eventData)  => HoverTooltip.Exit(this);
    void OnDisable() => HoverTooltip.Exit(this);
}
