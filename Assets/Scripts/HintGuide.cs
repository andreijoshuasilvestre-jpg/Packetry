using System;
using UnityEngine;

/// <summary>
/// What a star-costing hint (TaskManager.SpecificTier and past it) points at
/// on screen, in the tutorial's own marker language: up to two placed devices
/// get a WorldPulseMarker, and a HUD button gets a clicking-mouse marker —
/// plus, once the task's answer is open, the pulsing highlight (see
/// HintGuideManager). A free nudge never has one. Passed to
/// TaskManager.RegisterTask by the level that owns the task.
///
/// Every lookup is re-run while the guide is up, so one like "the first PC
/// with no cable" follows the player's progress instead of pointing at
/// whatever was true when the hint was opened — HintTargets has the common
/// ones. A lookup returning null just hides that marker.
/// </summary>
public class HintGuide
{
    // Same shape as TutorialManager.TutorialStep.WorldMarkerDevices.
    public Func<(NetworkDevice a, NetworkDevice b)> Devices;
    // What the device markers' mouse acts out — RightClick for a device
    // whose address popup or CLI opens that way.
    public MouseGesture Gesture = MouseGesture.Click;
    public Func<RectTransform> Button;

    /// <summary>This guide plus a button to press — the usual step up from a
    /// specific hint's "where" to an answer's "how".</summary>
    public HintGuide WithButton(Func<RectTransform> button) =>
        new HintGuide { Devices = Devices, Gesture = Gesture, Button = button };
}
