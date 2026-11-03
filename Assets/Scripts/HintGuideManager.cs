using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Puts a star-costing hint's HintGuide on screen with the same markers a
/// tutorial step uses, so a stuck player in a challenge level gets shown, not
/// just told. A specific hint marks where (devices, and a button's clicking
/// mouse); the answer also pulses the button to press (PulsingHighlight).
///
/// Follows the task whose costing hint was opened most recently and is still
/// incomplete. A guide stays up until its task is done — the player paid a
/// star for it, and a hint tier can't be reopened — and when that task
/// completes, the next most recent paid-for guide (if any) takes over. A free
/// nudge doesn't change which guide shows.
///
/// Owns its own markers rather than borrowing TutorialUIManager's, so the two
/// can never re-target each other's (same one-owner-per-purpose convention as
/// WorldPulseMarker's callers). Only built alongside TaskUIManager (see
/// GameBootstrap.SetupUI) — the levels whose hints players can actually open.
/// </summary>
public class HintGuideManager : MonoBehaviour
{
    public static HintGuideManager Instance { get; private set; }

    // Device lookups can be whole-network ping sweeps (HintTargets.
    // FirstUnreachablePair, CriticalCableEnds), so they're re-run a few times
    // a second rather than every frame — a marker follows its device's
    // movement on its own in between. The button lookup is cheap and runs
    // every frame, so a flyout opening moves the marker off the arrow and onto
    // the tool right away.
    const float DeviceRefreshInterval = 0.25f;

    static readonly Predicate<TaskManager.GameTask> IsDone = t => t.IsComplete;

    readonly List<TaskManager.GameTask> guided = new(); // oldest → most recent costing hint
    TaskManager subscribedTo;

    readonly WorldPulseMarker[] markers = new WorldPulseMarker[2];
    readonly NetworkDevice[] markedDevices = new NetworkDevice[2];
    readonly MouseGesture[] markedGestures = new MouseGesture[2];
    UIPulseMarker uiMarker;
    PulsingHighlight highlight;
    RectTransform markedButton;
    bool highlighting;
    HintGuide shownGuide;
    float nextDeviceRefresh;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    void Start()
    {
        subscribedTo = TaskManager.Instance;
        if (subscribedTo != null) subscribedTo.OnHintUsed += HandleHintUsed;
    }

    void OnDestroy()
    {
        if (subscribedTo != null) subscribedTo.OnHintUsed -= HandleHintUsed;
    }

    void Build()
    {
        for (int i = 0; i < markers.Length; i++)
            markers[i] = new GameObject($"HintMarker{i}").AddComponent<WorldPulseMarker>();

        // Same slot as TutorialUIManager's Guide_Canvas: above the CLI panel
        // (20) so a marker pointing into it draws on top, below the warning
        // windows (25) and concept cards (30) that should cover it.
        var canvasGo = new GameObject("HintGuide_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 21;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        // No GraphicRaycaster — nothing on this canvas is ever clickable.

        uiMarker = UIPulseMarker.Create(canvasGo.GetComponent<RectTransform>(), "HintUiMarker");
        // Renders nothing of its own — pulses whichever button the guide names.
        highlight = gameObject.AddComponent<PulsingHighlight>();
    }

    void HandleHintUsed(TaskManager.GameTask task)
    {
        if (task.CurrentGuide == null) return; // a free nudge, or nothing to point at
        guided.Remove(task);
        guided.Add(task);
    }

    void LateUpdate()
    {
        guided.RemoveAll(IsDone);
        var task = guided.Count > 0 ? guided[guided.Count - 1] : null;
        // Guidance clears the instant a level starts leaving, same as
        // TutorialUIManager's in GameBootstrap.ExitSequenceRoutine.
        var guide = task != null && !GameBootstrap.ExitStarted ? task.CurrentGuide : null;
        if (guide == null) { HideAll(); return; }

        if (guide != shownGuide || Time.unscaledTime >= nextDeviceRefresh)
        {
            shownGuide = guide;
            nextDeviceRefresh = Time.unscaledTime + DeviceRefreshInterval;
            var (a, b) = guide.Devices != null ? guide.Devices() : default;
            MarkDevice(0, a, guide.Gesture);
            MarkDevice(1, b, guide.Gesture);
        }
        MarkButton(guide.Button?.Invoke(), task.HighestHintTierUsed >= TaskManager.AnswerTier);
    }

    void HideAll()
    {
        if (shownGuide == null) return;
        shownGuide = null;
        MarkDevice(0, null, default);
        MarkDevice(1, null, default);
        MarkButton(null, false);
    }

    void MarkDevice(int i, NetworkDevice device, MouseGesture gesture)
    {
        if (device == null)
        {
            markers[i].Hide();
            markedDevices[i] = null;
            return;
        }
        // Same target as last time — keep its animation running.
        if (ReferenceEquals(device, markedDevices[i]) && gesture == markedGestures[i] && markers[i].gameObject.activeSelf) return;

        markedDevices[i] = device;
        markedGestures[i] = gesture;
        markers[i].Show(device.VisualTopPoint(), device, gesture);
    }

    void MarkButton(RectTransform button, bool pulse)
    {
        if (button != null && !button.gameObject.activeInHierarchy) button = null;

        if (!ReferenceEquals(button, markedButton))
        {
            markedButton = button;
            if (button != null) uiMarker.Show(GuideAnchor.Ui(() => markedButton));
            else uiMarker.Hide();
            if (highlighting)
            {
                highlight.Hide();
                highlight.Attach(null);
                highlighting = false;
            }
        }

        // Out of the way while the player is carrying a device out of the
        // sidebar, same as the tutorial's own palette trail.
        var drag = DragDropManager.Instance;
        uiMarker.SetSuppressed(drag != null && drag.IsDragging);

        bool wantPulse = pulse && button != null;
        if (wantPulse && !highlighting)
        {
            highlight.Attach(button);
            highlight.Show();
        }
        else if (!wantPulse && highlighting)
        {
            highlight.Hide();
            highlight.Attach(null);
        }
        highlighting = wantPulse;
    }
}
