using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Live on-screen readout of TopologyDetector's classification of whatever the
/// player currently has wired up. Built only for LevelManager.DevSandboxLevel
/// (see GameBootstrap.SetupUI) — the existing "Play the scene directly in the
/// Editor" scratch mode, everything unlocked and nothing saved — so this stays
/// a dev-only toy for trying the detector out, not something a real player in
/// a numbered level ever sees. Not part of any level's tasks or win condition.
/// </summary>
public class TopologyDebugOverlay : MonoBehaviour
{
    const float PollInterval = 0.35f;

    TextMeshProUGUI label;

    void Start()
    {
        BuildCanvas();
        StartCoroutine(PollRoutine());
    }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("TopologyDebug_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 6; // under NotificationManager's banner (7), above TaskUIManager (5)

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        var panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGo.transform, false);
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);

        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(0f, 1f);
        panelRect.anchorMax        = new Vector2(0f, 1f);
        panelRect.pivot            = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(16, -16);
        panelRect.sizeDelta        = new Vector2(460, 36);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(panel.transform, false);
        label           = labelGo.AddComponent<TextMeshProUGUI>();
        label.fontSize  = 15;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color     = new Color(0.55f, 1f, 0.60f, 1f); // terminal-green, matches CLIPanelManager's look
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(12, 2);
        lrt.offsetMax = new Vector2(-12, -2);
    }

    IEnumerator PollRoutine()
    {
        var wait = new WaitForSeconds(PollInterval);
        while (true)
        {
            Refresh();
            yield return wait;
        }
    }

    void Refresh()
    {
        if (label == null) return;
        var devices = Object.FindObjectsByType<NetworkDevice>(FindObjectsSortMode.None);
        var result  = TopologyDetector.Classify(devices);
        label.text  = $"Topology: {result.Summary}";
    }
}
