using UnityEngine;

/// <summary>
/// Creates three display devices (PC, Switch, Router) in the palette zone
/// at the near edge of the play area. These are visual-only; clicking them
/// tells DragDropManager to spawn a clone.
/// </summary>
public class DevicePalette : MonoBehaviour
{
    [SerializeField] private float paletteZ      = -13f;  // world-Z of the palette row
    [SerializeField] private float paletteY      =  0.0f; // rest height
    [SerializeField] private float itemSpacing   =  4.5f; // X gap between items
    [SerializeField] private float labelHeight   =  1.6f; // Y offset for label

    private static readonly DeviceType[] Types =
    {
        DeviceType.PC,
        DeviceType.Switch,
        DeviceType.Router
    };

    void Start()
    {
        for (int i = 0; i < Types.Length; i++)
        {
            float x = (i - (Types.Length - 1) * 0.5f) * itemSpacing;
            SpawnPaletteItem(Types[i], new Vector3(x, paletteY, paletteZ));
        }
    }

    void SpawnPaletteItem(DeviceType type, Vector3 worldPos)
    {
        // Build a fresh display copy
        GameObject go = DeviceModelBuilder.BuildTemplate(type);
        go.SetActive(true);
        go.name = $"Palette_{type}";
        go.transform.SetParent(transform, false);
        go.transform.position = worldPos;

        // Make it static — kinematic, no gravity
        var nd = go.GetComponent<NetworkDevice>();
        if (nd != null) nd.isPaletteItem = true;

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        // Tag it so DragDropManager can identify it
        var pi = go.AddComponent<PaletteItem>();
        pi.deviceType = type;

        // Floating label
        CreateLabel(type.ToString(), go.transform, new Vector3(0, labelHeight, 0));
    }

    void CreateLabel(string text, Transform parent, Vector3 localOffset)
    {
        var go = new GameObject("Label_" + text);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localOffset;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);

        var tm = go.AddComponent<TextMesh>();
        tm.text          = text;
        tm.characterSize = 0.18f;
        tm.fontSize      = 32;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = Color.white;

        // Always face the camera (handled in Update via a billboard helper)
        go.AddComponent<BillboardLabel>();
    }
}
