using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The 2D view: a flat, Cisco-Packet-Tracer-style logical diagram of the
/// network — each device as a symbol with its name and addresses, each cable
/// as a line in its subnet color with a status light at either end showing
/// whether that port is ready to carry traffic. Opened from ViewControlsHud's
/// "2D" button as a floating, draggable window, so it sits beside the 3D
/// scene rather than replacing it: the two stay live side by side.
///
/// The layout is the 3D scene seen from above as a floor plan, turned to
/// match the camera's facing (to the nearest quarter turn), so whatever's on
/// the left in 3D is on the left here too — which makes it easy to tell which
/// PC is which. It's stretched to fill the window and devices too close to
/// read are nudged apart; those are the only liberties it takes.
/// </summary>
public class NetworkDiagramPanel : MonoBehaviour
{
    public static NetworkDiagramPanel Instance { get; private set; }

    public bool IsOpen => windowGo != null && windowGo.activeSelf;

    const string FontResourcePath = "Fonts/PixeloidMono SDF";
    const int    SortingOrder     = 15; // above the HUD (10), below the CLI (20) so a CLI opened alongside sits on top

    static readonly Vector2 WindowSize = new Vector2(640f, 450f);
    const float Margin        = 20f;  // from the screen edge, matching ViewControlsHud
    const float InsetPad      = 10f;  // window edge to the black diagram inset
    const float LegendHeight  = 26f;
    const float GraphPadX     = 70f;  // keeps labels inside the inset
    const float GraphPadTop   = 36f;
    const float GraphPadBottom = 56f; // names/addresses hang below each icon
    const float IconSize      = 40f;
    const float MinNodeGap    = 96f;  // closer than this and two nodes get pushed apart
    const float MinWorldSpan  = 5f;   // world units — stops one lone device being blown up to fill the inset
    const float LineThickness = 3f;
    const float PortDotOffset = 30f;  // canvas units from a node's center to its port light

    static readonly Color WindowFill   = new Color(0.06f, 0.08f, 0.13f, 0.97f);
    static readonly Color WindowBorder = new Color(0.16f, 0.34f, 0.55f, 1f);
    static readonly Color Trim         = new Color(0.02f, 0.03f, 0.05f, 1f);
    static readonly Color InsetFill    = new Color(0.02f, 0.025f, 0.04f, 1f);
    static readonly Color InsetBorder  = new Color(0.12f, 0.20f, 0.32f, 1f);
    static readonly Color NameColor    = new Color(0.92f, 0.95f, 1f, 1f);
    static readonly Color DetailColor  = new Color(0.62f, 0.80f, 0.95f, 1f);
    static readonly Color PortColor    = new Color(0.62f, 0.68f, 0.78f, 1f);
    static readonly Color HintColor    = new Color(0.55f, 0.66f, 0.80f, 1f);

    /// <summary>Whether one end of a cable can carry traffic yet.</summary>
    enum PortState { Ready, NeedsAddress, Off }

    static Color StateColor(PortState s) => s switch
    {
        PortState.Ready        => GameTheme.Success,
        PortState.NeedsAddress => GameTheme.Warning,
        _                      => GameTheme.Danger,
    };

    static string StateText(PortState s) => s switch
    {
        PortState.Ready        => "ready",
        PortState.NeedsAddress => "needs an IP address",
        _                      => "switched off",
    };

    class Node
    {
        public GameObject Go;
        public RectTransform Rt;
        public TextMeshProUGUI Name, Detail;
        public Vector2 Pos;
    }

    class Link
    {
        public GameObject Go;
        public RectTransform Line;
        public Image LineImg;
        public Image DotA, DotB;
        public TextMeshProUGUI PortA, PortB;
    }

    TMP_FontAsset font;
    Canvas canvas;
    GameObject windowGo;
    RectTransform windowRt, graphRt, linksRoot, nodesRoot;
    TextMeshProUGUI emptyLabel;

    readonly Dictionary<NetworkDevice, Node> nodes = new();
    readonly Dictionary<Cable, Link> links = new();
    readonly List<NetworkDevice> deviceScratch = new();
    readonly List<NetworkDevice> staleDevices = new();
    readonly List<Cable> staleCables = new();
    readonly HashSet<Cable> liveCables = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        font = Resources.Load<TMP_FontAsset>(FontResourcePath);
        Build();
        windowGo.SetActive(false);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (windowGo == null || IsOpen) return;
        windowGo.SetActive(true);
        Refresh();
        AudioManager.Instance?.PlaySfx(SfxId.PanelPrompt);
    }

    public void Close()
    {
        if (!IsOpen) return;
        HoverTooltip.Hide();
        windowGo.SetActive(false);
    }

    void Update()
    {
        if (!IsOpen) return;

        if (GameBootstrap.ExitStarted) { Close(); return; }

        // Esc closes it — unless the CLI is up, which owns Esc for itself.
        bool cliOpen = CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen;
        if (!cliOpen && !InteractionMode.IsSuspended &&
            Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        Refresh();
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    void Refresh()
    {
        CollectDevices(deviceScratch);
        emptyLabel.gameObject.SetActive(deviceScratch.Count == 0);

        // Drop nodes for devices that are gone (removed, or picked back up).
        staleDevices.Clear();
        foreach (var kv in nodes)
            if (kv.Key == null || !deviceScratch.Contains(kv.Key)) staleDevices.Add(kv.Key);
        foreach (var d in staleDevices) { if (nodes[d].Go != null) Destroy(nodes[d].Go); nodes.Remove(d); }

        foreach (var d in deviceScratch)
            if (!nodes.ContainsKey(d)) nodes[d] = BuildNode(d);

        LayoutNodes();

        foreach (var d in deviceScratch)
        {
            Node n = nodes[d];
            n.Rt.anchoredPosition = n.Pos;
            n.Name.text   = d.deviceName;
            n.Detail.text = DetailFor(d);
        }

        RefreshLinks();
    }

    static void CollectDevices(List<NetworkDevice> into)
    {
        into.Clear();
        var dragging = DragDropManager.Instance;
        foreach (var d in NetworkDevice.AllDevices)
        {
            if (d == null || d.isPaletteItem || !d.gameObject.activeInHierarchy) continue;
            if (dragging != null && dragging.IsDragging && dragging.IsHolding(d.gameObject) && !dragging.IsRepositioning) continue;
            into.Add(d);
        }
    }

    // World positions flattened onto floor-plan axes, stretched to fill the
    // inset, then pushed apart where they'd overlap.
    void LayoutNodes()
    {
        if (deviceScratch.Count == 0) return;

        // The axes are the camera's facing rounded to the nearest 90°: the
        // camera usually sits at 45°, and flattening an axis-aligned table
        // from there lines everything up along one diagonal. Rounded, a table
        // reads as rows and columns, while "near you" still sits at the
        // bottom and "on your left" on the left.
        float yaw = IsometricCamera.Instance != null ? IsometricCamera.Instance.TargetYaw
                  : Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;
        Quaternion facing = Quaternion.Euler(0f, Mathf.Round(yaw / 90f) * 90f, 0f);
        Vector3 right = facing * Vector3.right, forward = facing * Vector3.forward;

        var flat = new Vector2[deviceScratch.Count];
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < deviceScratch.Count; i++)
        {
            Vector3 p = deviceScratch[i].transform.position;
            flat[i] = new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, forward));
            min = Vector2.Min(min, flat[i]);
            max = Vector2.Max(max, flat[i]);
        }

        // Each axis stretched on its own: a diagram needs left/right and
        // near/far kept, not true proportions, and one long table would
        // otherwise leave most of the inset empty.
        Rect area = GraphArea();
        Vector2 span = Vector2.Max(max - min, new Vector2(MinWorldSpan, MinWorldSpan));
        var scale = new Vector2(area.width / span.x, area.height / span.y);
        Vector2 worldCenter = (min + max) * 0.5f;

        for (int i = 0; i < deviceScratch.Count; i++)
            nodes[deviceScratch[i]].Pos = area.center + Vector2.Scale(flat[i] - worldCenter, scale);

        // A few relaxation passes: devices a hand-width apart on the same
        // table would otherwise draw on top of each other.
        for (int pass = 0; pass < 12; pass++)
        {
            for (int i = 0; i < deviceScratch.Count; i++)
            for (int j = i + 1; j < deviceScratch.Count; j++)
            {
                Node a = nodes[deviceScratch[i]], b = nodes[deviceScratch[j]];
                Vector2 delta = b.Pos - a.Pos;
                float dist = delta.magnitude;
                if (dist >= MinNodeGap) continue;
                Vector2 dir = dist > 0.01f ? delta / dist : new Vector2(1f, 0.3f).normalized;
                Vector2 push = dir * ((MinNodeGap - dist) * 0.5f);
                a.Pos -= push;
                b.Pos += push;
            }
            foreach (var d in deviceScratch)
            {
                Node n = nodes[d];
                n.Pos = new Vector2(Mathf.Clamp(n.Pos.x, area.xMin, area.xMax), Mathf.Clamp(n.Pos.y, area.yMin, area.yMax));
            }
        }
    }

    // In graphRt's local space (centered pivot).
    Rect GraphArea()
    {
        Vector2 size = graphRt.rect.size;
        return Rect.MinMaxRect(-size.x * 0.5f + GraphPadX, -size.y * 0.5f + GraphPadBottom,
                                size.x * 0.5f - GraphPadX,  size.y * 0.5f - GraphPadTop);
    }

    void RefreshLinks()
    {
        liveCables.Clear();
        var cm = CableManager.Instance;
        if (cm != null)
        {
            foreach (var cable in cm.Cables)
            {
                if (cable == null || cable.DeviceA == null || cable.DeviceB == null) continue;
                if (!nodes.TryGetValue(cable.DeviceA, out Node a) || !nodes.TryGetValue(cable.DeviceB, out Node b)) continue;
                liveCables.Add(cable);
                if (!links.TryGetValue(cable, out Link link)) links[cable] = link = BuildLink();
                PlaceLink(link, cable, a.Pos, b.Pos);
            }
        }

        staleCables.Clear();
        foreach (var kv in links)
            if (kv.Key == null || !liveCables.Contains(kv.Key)) staleCables.Add(kv.Key);
        foreach (var c in staleCables) { if (links[c].Go != null) Destroy(links[c].Go); links.Remove(c); }
    }

    void PlaceLink(Link link, Cable cable, Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;
        Vector2 dir = length > 0.01f ? delta / length : Vector2.right;

        link.Line.anchoredPosition = (from + to) * 0.5f;
        link.Line.sizeDelta = new Vector2(length, LineThickness);
        link.Line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        Color c = cable.BaseColor;
        link.LineImg.color = new Color(c.r, c.g, c.b, 1f);

        float offset = Mathf.Min(PortDotOffset, length * 0.4f);
        PlaceEnd(link.DotA, link.PortA, from + dir * offset, dir, cable.DeviceA, cable.PortA);
        PlaceEnd(link.DotB, link.PortB, to - dir * offset, -dir, cable.DeviceB, cable.PortB);
    }

    static void PlaceEnd(Image dot, TextMeshProUGUI label, Vector2 at, Vector2 dir, NetworkDevice device, int port)
    {
        dot.rectTransform.anchoredPosition = at;
        dot.color = StateColor(StateOf(device, port));

        // Port names only on router ends — those are what the CLI's
        // "interface ethN" refers to. A PC has a single port and a switch's
        // port numbers mean nothing to configure, so labeling them was clutter.
        bool named = device.deviceType == DeviceType.Router;
        label.gameObject.SetActive(named);
        if (!named) return;
        Vector2 normal = new Vector2(-dir.y, dir.x);
        if (normal.y < 0f) normal = -normal;
        label.rectTransform.anchoredPosition = at + normal * 11f;
        label.text = "eth" + port;
    }

    static PortState StateOf(NetworkDevice device, int port)
    {
        switch (device.deviceType)
        {
            case DeviceType.PC:
                var addr = device.GetComponent<DeviceAddress>();
                return addr != null && addr.HasAddress ? PortState.Ready : PortState.NeedsAddress;
            case DeviceType.Router:
                var cfg = device.GetComponent<RouterInterfaceConfig>();
                var iface = cfg != null ? cfg.Get(port) : null;
                if (iface == null || iface.shutdown) return PortState.Off;
                return string.IsNullOrEmpty(iface.ip) ? PortState.NeedsAddress : PortState.Ready;
            default:
                return PortState.Ready; // a switch passes traffic through without configuration
        }
    }

    static string WithPrefix(string ip, string mask)
    {
        int prefix = SubnetMath.MaskToPrefixLength(mask);
        return prefix >= 0 ? $"{ip}/{prefix}" : $"{ip} {mask}";
    }

    static string DetailFor(NetworkDevice d)
    {
        switch (d.deviceType)
        {
            case DeviceType.PC:
                var addr = d.GetComponent<DeviceAddress>();
                return addr != null && addr.HasAddress
                    ? WithPrefix(addr.Ip, addr.Mask)
                    : $"<color=#{ColorUtility.ToHtmlStringRGB(GameTheme.Warning)}>no IP yet</color>";
            case DeviceType.Router:
                var cfg = d.GetComponent<RouterInterfaceConfig>();
                if (cfg == null) return "";
                var sb = new StringBuilder();
                for (int i = 0; i < cfg.Count; i++)
                {
                    var iface = cfg.Get(i);
                    if (iface == null || string.IsNullOrEmpty(iface.ip)) continue;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append($"eth{i} {WithPrefix(iface.ip, iface.mask)}");
                }
                return sb.ToString();
            default:
                return "";
        }
    }

    TooltipContent NodeTooltip(NetworkDevice d)
    {
        if (d == null) return null;
        var body = new StringBuilder();
        string footer;
        switch (d.deviceType)
        {
            case DeviceType.PC:
            {
                var addr = d.GetComponent<DeviceAddress>();
                if (addr != null && addr.HasAddress)
                    body.Append($"IP address  {addr.Ip}\nSubnet mask {addr.Mask} (/{SubnetMath.MaskToPrefixLength(addr.Mask)})");
                else
                    body.Append("No IP address yet. It can't talk to anything until it has one.");
                footer = "Right-click the PC in the 3D view to set its address.";
                break;
            }
            case DeviceType.Router:
            {
                var cfg = d.GetComponent<RouterInterfaceConfig>();
                body.Append("Connects different subnets. Each port is the gateway for the subnet behind it.\n");
                for (int i = 0; i < d.PortCount; i++)
                {
                    var iface = cfg != null ? cfg.Get(i) : null;
                    string ip = iface != null && !string.IsNullOrEmpty(iface.ip) ? WithPrefix(iface.ip, iface.mask) : "no address";
                    PortState state = StateOf(d, i);
                    string cabled = d.GetPortCable(i) != null ? "" : "  (no cable)";
                    body.Append($"\neth{i}  {ip}  <color=#{ColorUtility.ToHtmlStringRGB(StateColor(state))}>{StateText(state)}</color>{cabled}");
                }
                footer = "Right-click the router in the 3D view to configure it.";
                break;
            }
            default:
            {
                int used = 0;
                for (int i = 0; i < d.PortCount; i++) if (d.GetPortCable(i) != null) used++;
                body.Append($"Connects devices on the same subnet. Needs no setup.\nPorts in use: {used} of {d.PortCount}");
                footer = "Every PC on one switch should share a subnet.";
                break;
            }
        }

        return new TooltipContent
        {
            Title    = d.deviceName,
            Tag      = d.deviceType.ToString().ToUpperInvariant(),
            TagColor = DiagramIconBuilder.ColorFor(d.deviceType),
            Body     = body.ToString(),
            Footer   = footer,
        };
    }

    // ── Construction ─────────────────────────────────────────────────────────

    void Build()
    {
        var canvasGo = new GameObject("NetworkDiagram_Canvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Docked above the view controls in the bottom-right corner by
        // default; the title bar drags it anywhere.
        windowGo = NewRect("DiagramWindow", canvasGo.transform);
        windowRt = (RectTransform)windowGo.transform;
        windowRt.anchorMin = windowRt.anchorMax = new Vector2(1f, 0f);
        windowRt.pivot     = new Vector2(1f, 0f);
        windowRt.sizeDelta = WindowSize;
        windowRt.anchoredPosition = new Vector2(-Margin, ViewControlsHud.BottomRightReservedHeight + 4f);
        var bg = windowGo.AddComponent<Image>();
        bg.sprite = PlateSpriteBuilder.GetSprite(WindowFill, WindowBorder, Trim, borderThickness: 2, chamfer: 6, pixelScale: 4);
        bg.type = Image.Type.Sliced;
        OccupiesScreen.Add(windowGo, OccupantLayer.Panel);

        var closeBtn = WindowChromeBuilder.Build(windowGo.transform, windowRt, canvas, font,
            "Network Diagram (2D)", WindowSize.x, GameTheme.Info, showCloseButton: true);
        closeBtn?.onClick.AddListener(Close);

        // Black inset the diagram draws on.
        var inset = NewRect("Inset", windowGo.transform);
        var insetRt = (RectTransform)inset.transform;
        insetRt.anchorMin = Vector2.zero;
        insetRt.anchorMax = Vector2.one;
        insetRt.offsetMin = new Vector2(InsetPad, InsetPad + LegendHeight);
        insetRt.offsetMax = new Vector2(-InsetPad, -InsetPad);
        var insetImg = inset.AddComponent<Image>();
        insetImg.sprite = PlateSpriteBuilder.GetSprite(InsetFill, InsetBorder, Trim, borderThickness: 2, chamfer: 5, pixelScale: 3);
        insetImg.type = Image.Type.Sliced;
        inset.AddComponent<RectMask2D>();

        var graph = NewRect("Graph", inset.transform);
        graphRt = (RectTransform)graph.transform;
        Stretch(graphRt);
        linksRoot = (RectTransform)NewRect("Links", graph.transform).transform;
        Stretch(linksRoot);
        nodesRoot = (RectTransform)NewRect("Nodes", graph.transform).transform;
        Stretch(nodesRoot);

        emptyLabel = NewText("Empty", inset.transform, 13f, HintColor, TextAlignmentOptions.Center);
        emptyLabel.text = "Nothing placed yet.\nDevices and cables you add show up here.";
        Stretch(emptyLabel.rectTransform);

        BuildLegend();
    }

    void BuildLegend()
    {
        var row = NewRect("Legend", windowGo.transform);
        var rt = (RectTransform)row.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(InsetPad + 4f, InsetPad - 2f);
        rt.offsetMax = new Vector2(-InsetPad, InsetPad - 2f + LegendHeight);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        foreach (PortState s in new[] { PortState.Ready, PortState.NeedsAddress, PortState.Off })
        {
            var dot = NewRect("Dot", row.transform).AddComponent<Image>();
            dot.color = StateColor(s);
            var dle = dot.gameObject.AddComponent<LayoutElement>();
            dle.preferredWidth = dle.preferredHeight = dle.minWidth = dle.minHeight = 8f;
            var label = NewText("Label", row.transform, 11f, HintColor, TextAlignmentOptions.MidlineLeft);
            label.text = "port " + StateText(s) + "   ";
            label.enableWordWrapping = false;
        }
        var colors = NewText("Colors", row.transform, 11f, HintColor, TextAlignmentOptions.MidlineLeft);
        colors.text = "Lines: subnet colors";
        colors.enableWordWrapping = false;
    }

    Node BuildNode(NetworkDevice device)
    {
        var go = NewRect("Node_" + device.deviceName, nodesRoot);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(IconSize, IconSize);

        var icon = go.AddComponent<Image>();
        icon.sprite = DiagramIconBuilder.GetSprite(device.deviceType);
        icon.preserveAspect = true;

        var name = NewText("Name", go.transform, 11f, NameColor, TextAlignmentOptions.Top);
        name.fontStyle = FontStyles.Bold;
        name.enableWordWrapping = false;
        name.raycastTarget = false;
        var nrt = name.rectTransform;
        nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0f);
        nrt.pivot = new Vector2(0.5f, 1f);
        nrt.anchoredPosition = new Vector2(0f, -2f);
        nrt.sizeDelta = new Vector2(160f, 14f);

        var detail = NewText("Detail", go.transform, 10f, DetailColor, TextAlignmentOptions.Top);
        detail.enableWordWrapping = false;
        detail.raycastTarget = false;
        var drt = detail.rectTransform;
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0f);
        drt.pivot = new Vector2(0.5f, 1f);
        drt.anchoredPosition = new Vector2(0f, -16f);
        drt.sizeDelta = new Vector2(180f, 40f);

        HoverTooltip.Attach(go, () => NodeTooltip(device), TooltipSide.Left, delay: 0.25f);

        return new Node { Go = go, Rt = rt, Name = name, Detail = detail };
    }

    Link BuildLink()
    {
        var go = NewRect("Link", linksRoot);
        Stretch((RectTransform)go.transform);

        var line = NewRect("Line", go.transform).AddComponent<Image>();
        line.raycastTarget = false;
        var lrt = line.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.pivot = new Vector2(0.5f, 0.5f);

        Image Dot()
        {
            var dot = NewRect("PortLight", go.transform).AddComponent<Image>();
            dot.raycastTarget = false;
            var drt = dot.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.sizeDelta = new Vector2(9f, 9f);
            return dot;
        }

        TextMeshProUGUI Port()
        {
            var t = NewText("Port", go.transform, 9f, PortColor, TextAlignmentOptions.Center);
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            var trt = t.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(48f, 12f);
            return t;
        }

        return new Link { Go = go, Line = lrt, LineImg = line, DotA = Dot(), DotB = Dot(), PortA = Port(), PortB = Port() };
    }

    static GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = Vector2.zero; // a fresh RectTransform defaults to 100x100
        return go;
    }

    TextMeshProUGUI NewText(string name, Transform parent, float size, Color color, TextAlignmentOptions align)
    {
        var tmp = NewRect(name, parent).AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
