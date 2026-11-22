using System;
using NearbyConnections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

internal enum DemoTab { Echo, Lobby, SharedScene }

internal sealed class DemoView
{
    private readonly RectTransform safe;
    private readonly RectTransform body;
    private readonly ScrollRect scroll;
    private Button[] tabs;
    public RectTransform Content => body;
    public RectTransform OverlayRoot => safe;
    private readonly RectTransform peers;
    private readonly Text status;
    private Button hostButton;
    private Button joinButton;
    private Button stopButton;
    private Button strategyButton;

    public DemoView(Transform parent)
    {
        var root = DemoUi.Rect(parent, "Demo Canvas");
        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(720, 1280);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        root.gameObject.AddComponent<GraphicRaycaster>();
        var backdrop = DemoUi.Rect(root, "Background");
        DemoUi.Fill(backdrop);
        DemoUi.Surface(backdrop, DemoUi.Background);

        if (EventSystem.current == null)
        {
            var events = new GameObject("Demo Event System", typeof(EventSystem));
            events.transform.SetParent(parent, false);
            events.AddComponent<InputSystemUIInputModule>();
        }

        safe = DemoUi.Rect(root, "Safe Area");
        UpdateSafeArea();
        DemoUi.Stack(safe, 24);
        DemoUi.Label(safe, "Nearby Connections", 44, true);
        status = DemoUi.Label(safe, "", 23);
        status.color = DemoUi.Muted;
        body = DemoUi.Scroll(safe, "Demo Content", out scroll);
        scroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        peers = DemoUi.Rect(body, "Peers");
        DemoUi.Stack(peers);
    }

    public void BuildTabs(Action<DemoTab> select)
    {
        var row = DemoUi.Row(safe, "Demo Tabs");
        row.SetSiblingIndex(1);
        tabs = new[]
        {
            DemoUi.Button(row, "Echo", () => select(DemoTab.Echo)),
            DemoUi.Button(row, "Lobby", () => select(DemoTab.Lobby)),
            DemoUi.Button(row, "Shared scene", () => select(DemoTab.SharedScene))
        };
    }

    public void SetTab(DemoTab tab, bool active)
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            var choice = (DemoTab)i;
            tabs[i].targetGraphic.color = choice == tab ? DemoUi.Accent : DemoUi.ButtonColor;
            tabs[i].interactable = !active || (tab == DemoTab.Echo) == (choice == DemoTab.Echo);
        }
    }

    public void ResetScroll() => scroll.verticalNormalizedPosition = 1;

    public void BuildConnection(Action host, Action join, Action end, Action strategy)
    {
        var controls = DemoUi.Rect(body, "Connection Controls");
        controls.SetAsFirstSibling();
        DemoUi.Stack(controls);
        strategyButton = DemoUi.Button(controls, "Connection strategy", strategy);
        var row = DemoUi.Row(controls, "Session Actions");
        hostButton = DemoUi.Button(row, "Host", host, true);
        joinButton = DemoUi.Button(row, "Join", join, true);
        stopButton = DemoUi.Button(controls, "End session", end);
    }

    public void SetConnectionControls(bool active, bool echo, NearbyStrategy strategy)
    {
        strategyButton.GetComponentInChildren<Text>().text = strategy == NearbyStrategy.Star
            ? "Strategy  ·  Star" : "Strategy  ·  Point to point";
        strategyButton.gameObject.SetActive(echo);
        strategyButton.interactable = !active;
        hostButton.transform.parent.gameObject.SetActive(!active);
        stopButton.gameObject.SetActive(active);
    }

    public void SetStatus(string value) => status.text = value;

    public void ClearPeers() => DemoUi.Clear(peers);

    public void Connect(string name, Action connect) => DemoUi.Button(peers, $"Connect to {name}", connect);

    public void Confirm(string name, string code, Action accept, Action reject)
    {
        DemoUi.Label(peers, $"Compare the code on both devices for {name}", 24);
        DemoUi.Label(peers, code, 42, true);
        var row = DemoUi.Row(peers, "Connection Approval");
        DemoUi.Button(row, "Codes match", accept, true);
        DemoUi.Button(row, "Reject", reject);
    }

    public void Peer(string name, Action disconnect, Action echo)
    {
        DemoUi.Label(peers, name, 26, true);
        var row = DemoUi.Row(peers, "Connected Device");
        if (echo != null) DemoUi.Button(row, "Send echo", echo, true);
        DemoUi.Button(row, "Disconnect", disconnect);
    }

    public void UpdateSafeArea()
    {
        if (Screen.width <= 0 || Screen.height <= 0) return;
        Rect area = Screen.safeArea;
        var size = new Vector2(Screen.width, Screen.height);
        safe.anchorMin = area.min / size;
        safe.anchorMax = area.max / size;
        safe.offsetMin = safe.offsetMax = Vector2.zero;
    }
}
