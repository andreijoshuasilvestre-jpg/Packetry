using UnityEngine;

/// <summary>
/// Marks a device display in the palette zone.
/// DragDropManager reads DeviceType and spawns a clone when this item is clicked.
/// </summary>
public class PaletteItem : MonoBehaviour
{
    public DeviceType deviceType;

    // Hover highlight support
    private Renderer[] renderers;
    private Color[] originalColors;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            originalColors[i] = renderers[i].material.color;
    }

    public void SetHover(bool on)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Color c = originalColors[i];
            renderers[i].material.color = on ? Color.Lerp(c, Color.white, 0.3f) : c;
        }
    }
}
