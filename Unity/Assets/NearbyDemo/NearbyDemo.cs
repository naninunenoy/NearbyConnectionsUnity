using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NearbyConnections;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

internal sealed class NearbyDemo : MonoBehaviour
{
    private const string ServiceId = "com.example.nearbyconnections.demo";
    private readonly Dictionary<string, NearbyEndpoint> discovered = new();
    private readonly Dictionary<long, NearbyConnectionRequest> requests = new();
    private readonly Dictionary<string, (byte[] data, double time)> pings = new();
    private NearbyClient client;
    private PanelSettings panel;
    private Label status;
    private VisualElement peers;
    private Button hostButton;
    private Button joinButton;
    private Button stopButton;
    private DropdownField strategyField;
    private NearbyStrategy strategy;
    private bool busy;
    private int session;

#if UNITY_ANDROID && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ShowDemo()
    {
        new GameObject("Nearby Android Demo").AddComponent<NearbyDemo>();
    }
#endif

    private void Awake()
    {
        panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NearbyDemoTheme");
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(720, 1280);

        var view = new GameObject("Nearby Demo UI");
        view.SetActive(false);
        view.transform.SetParent(transform, false);
        var document = view.AddComponent<UIDocument>();
        document.panelSettings = panel;
        view.SetActive(true);

        var page = new ScrollView();
        page.style.flexGrow = 1;
        page.style.paddingTop = 36;
        page.style.paddingBottom = 20;
        page.style.paddingLeft = 18;
        page.style.paddingRight = 18;
        page.style.fontSize = 24;
        page.style.color = Color.white;
        page.style.backgroundColor = new Color(0.10f, 0.14f, 0.19f);
        document.rootVisualElement.Add(page);

        var title = new Label("Nearby Connections");
        title.style.fontSize = 36;
        page.Add(title);
        strategyField = new DropdownField("Strategy", new List<string> { "Point to Point", "Star" }, 0);
        strategyField.RegisterValueChangedCallback(_ => strategy = (NearbyStrategy)strategyField.index);
        page.Add(strategyField);
        hostButton = AddButton(page, "Host (advertise)", () => StartSession(true));
        joinButton = AddButton(page, "Join (discover)", () => StartSession(false));
        stopButton = AddButton(page, "End session", () =>
        {
            ResetSession("Session ended.");
            return Task.CompletedTask;
        });
        status = new Label("Choose Host on one device and Join on the other.");
        status.style.whiteSpace = WhiteSpace.Normal;
        page.Add(status);
        peers = new VisualElement();
        page.Add(peers);
        Refresh();
    }

    private async Task StartSession(bool host)
    {
        if (busy || client != null) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        string[] missing = NearbyClient.GetMissingAndroidPermissions();
        if (missing.Length != 0)
        {
            status.text = "Grant Nearby permissions, then tap Host or Join again.";
            Permission.RequestUserPermissions(missing);
            return;
        }
#endif
        busy = true;
        Refresh();
        int attempt = ++session;
        try
        {
            var current = NearbyClient.Create(new NearbyOptions { ServiceId = ServiceId, Strategy = strategy });
            client = current;
            current.EndpointFound += endpoint =>
            {
                discovered[endpoint.Id] = endpoint;
                Refresh();
            };
            current.EndpointLost += endpoint =>
            {
                discovered.Remove(endpoint);
                Refresh();
            };
            current.ConnectionInitiated += request =>
            {
                requests[request.Id] = request;
                Refresh();
            };
            current.ConnectionResult += result =>
            {
                requests.Remove(result.RequestId);
                status.text = result.Status == NearbyConnectionStatus.Connected
                    ? $"Connected to {result.EndpointId}."
                    : $"{result.Status} ({result.NativeCode}) for {result.EndpointId}.";
                Refresh();
            };
            current.Disconnected += endpoint =>
            {
                pings.Remove(endpoint);
                status.text = $"Disconnected from {endpoint}.";
                Refresh();
            };
            current.BytesReceived += (endpoint, bytes) => HandleBytes(current, endpoint, bytes);
            current.Faulted += (endpoint, failure) =>
            {
                if (endpoint == null) ResetSession(failure.Message);
                else status.text = $"{endpoint}: {failure.Message}";
            };

            status.text = host ? "Starting advertising..." : "Starting discovery...";
            if (host) await current.StartAdvertisingAsync(SystemInfo.deviceName);
            else await current.StartDiscoveryAsync();
            if (attempt == session)
                status.text = host ? "Advertising. Compare codes when a request arrives." : "Discovering nearby hosts.";
        }
        catch (Exception failure)
        {
            if (attempt != session) return;
            ResetSession("Could not start the session.");
            ShowError(failure);
        }
        finally
        {
            if (attempt == session)
            {
                busy = false;
                Refresh();
            }
        }
    }

    private void Refresh()
    {
        strategyField.SetEnabled(client == null && !busy);
        hostButton.SetEnabled(client == null && !busy);
        joinButton.SetEnabled(client == null && !busy);
        stopButton.SetEnabled(client != null);
        peers.Clear();
        if (client == null) return;

        var current = client;
        string[] connected = current.GetConnectedEndpointIds();
        foreach (var endpoint in discovered.Values)
        {
            if (connected.Contains(endpoint.Id) || requests.Values.Any(request => request.Endpoint.Id == endpoint.Id)) continue;
            AddButton(peers, $"Connect to {endpoint.Name} ({endpoint.Id})",
                () => current.RequestConnectionAsync(endpoint.Id, SystemInfo.deviceName));
        }

        foreach (var request in requests.Values)
        {
            peers.Add(new Label($"{request.Endpoint.Name} ({request.Endpoint.Id})"));
            peers.Add(new Label($"Compare code on both devices: {request.AuthenticationCode}"));
            AddButton(peers, "Codes match — accept", () => Respond(current, request.Id, true));
            AddButton(peers, "Reject", () => Respond(current, request.Id, false));
        }

        foreach (string endpoint in connected)
        {
            AddButton(peers, $"Send echo to {endpoint}", () => SendPing(current, endpoint));
            AddButton(peers, $"Disconnect {endpoint}", () => current.DisconnectAsync(endpoint));
        }
    }

    private Task Respond(NearbyClient current, long request, bool accept)
    {
        requests.Remove(request);
        Refresh();
        return accept ? current.AcceptConnectionAsync(request) : current.RejectConnectionAsync(request);
    }

    private async Task SendPing(NearbyClient current, string endpoint)
    {
        var data = new byte[20];
        data[1] = 0x80;
        data[2] = 0xff;
        var token = Guid.NewGuid().ToByteArray();
        Buffer.BlockCopy(token, 0, data, 4, token.Length);
        pings[endpoint] = (data, Time.realtimeSinceStartupAsDouble);
        status.text = $"Waiting for echo from {endpoint}...";
        await current.SendBytesAsync(endpoint, data);
    }

    private async void HandleBytes(NearbyClient current, string endpoint, byte[] data)
    {
        if (data.Length != 20) return;
        try
        {
            if (data[0] == 0)
            {
                data[0] = 1;
                await current.SendBytesAsync(endpoint, data);
                if (client == current) status.text = $"Echoed {data.Length} bytes to {endpoint}.";
            }
            else if (data[0] == 1 && pings.TryGetValue(endpoint, out var ping))
            {
                data[0] = 0;
                if (!data.SequenceEqual(ping.data)) return;
                pings.Remove(endpoint);
                double milliseconds = (Time.realtimeSinceStartupAsDouble - ping.time) * 1000;
                status.text = $"Verified {data.Length} bytes from {endpoint}. RTT {milliseconds:F1} ms.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (client == current) ShowError(ex); }
    }

    private Button AddButton(VisualElement parent, string text, Func<Task> action)
    {
        var button = new Button(async () =>
        {
            int before = session;
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (before == session) ShowError(ex);
            }
        }) { text = text };
        button.style.minHeight = 64;
        button.style.fontSize = 22;
        parent.Add(button);
        return button;
    }

    private void ShowError(Exception failure)
    {
        status.text = failure is NearbyException nearby
            ? $"{nearby.Error} ({nearby.NativeCode?.ToString() ?? "—"}): {nearby.Message}"
            : failure.Message;
    }

    private void ResetSession(string message)
    {
        session++;
        if (client != null) _ = client.ShutdownAsync();
        client = null;
        busy = false;
        discovered.Clear();
        requests.Clear();
        pings.Clear();
        status.text = message;
        Refresh();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused && client != null) ResetSession("Session ended in the background. Choose a role to reconnect.");
    }

    private void OnDestroy()
    {
        if (client != null) _ = client.ShutdownAsync();
        if (panel != null) Destroy(panel);
    }
}
