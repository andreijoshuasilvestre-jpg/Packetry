using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Global UI-scale multiplier (SettingsPanelManager's "UI Scale" slider),
/// applied on top of every canvas's existing CanvasScaler resolution scaling
/// rather than replacing it. Implemented by shrinking the reference resolution
/// CanvasScaler measures the real screen against — a smaller reference
/// resolution makes ScaleWithScreenSize compute a larger scale factor for the
/// same physical screen size, growing every button/icon/text proportionally,
/// without touching the per-canvas layout math each of this project's ~15
/// manager-built canvases already has.
/// </summary>
public static class UIScale
{
    public static readonly Vector2 BaseReferenceResolution = new Vector2(1920f, 1080f);

    public static Vector2 ReferenceResolution =>
        BaseReferenceResolution / Mathf.Clamp(SaveManager.Settings.uiScale, 0.5f, 2f);

    /// <summary>Minimum gap (canvas units) FitToScreen keeps between a panel
    /// and the screen edge when it has to shrink that panel to fit.</summary>
    public const float ScreenMargin = 24f;

    // Every canvas built while the game is running registers its scaler here so
    // dragging SettingsPanelManager's slider restyles already-built canvases
    // immediately, not just ones built after the change (managers rebuild fresh
    // every scene load — see MusicManager's doc comment — so this list is
    // naturally scoped to whatever canvases exist in the current scene).
    static readonly List<CanvasScaler> registered = new();

    /// <summary>Call right after creating a CanvasScaler in ScaleWithScreenSize
    /// mode, instead of setting referenceResolution directly — applies the
    /// current player-chosen scale immediately and keeps it live-updatable.</summary>
    public static void Register(CanvasScaler scaler)
    {
        registered.RemoveAll(s => s == null);
        registered.Add(scaler);
        // Expand rather than the default width-only match: the canvas is then
        // guaranteed to be at least ReferenceResolution in BOTH dimensions, so
        // a window wider than 16:9 can't silently lose canvas height and clip
        // top/bottom-anchored UI. Identical to width-match at exactly 16:9.
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referenceResolution = ReferenceResolution;
    }

    /// <summary>Re-applies the current scale to every still-alive registered
    /// canvas — called by SettingsPanelManager's UI Scale slider on every drag
    /// tick so the effect previews live instead of only on next scene load.</summary>
    public static void Apply()
    {
        registered.RemoveAll(s => s == null);
        Vector2 reference = ReferenceResolution;
        foreach (var scaler in registered)
            scaler.referenceResolution = reference;
    }
}
