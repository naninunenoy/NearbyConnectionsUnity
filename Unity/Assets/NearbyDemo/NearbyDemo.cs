using System;
using System.Threading.Tasks;
using NearbyConnections;
using UnityEngine;

internal sealed class NearbyDemo : MonoBehaviour
{
    private readonly DemoConnection connection = new();
    private DemoEcho echo;
    private DemoView view;
    private NearbyStrategy echoStrategy;

    private void Awake()
    {
        echo = new DemoEcho(connection);
        view = new DemoView(transform);
        connection.Changed += Render;
        connection.StatusChanged += view.SetStatus;
        connection.Received += (peer, bytes) => Run(() => echo.Receive(peer, bytes));
        view.BuildConnection(
            () => Run(() => StartSession(true)),
            () => Run(() => StartSession(false)),
            () => connection.End("Session ended."),
            () => { echoStrategy = echoStrategy == NearbyStrategy.Star ? NearbyStrategy.PointToPoint : NearbyStrategy.Star; Render(); });
        view.SetStatus("Choose Host on one device and Join on another.");
        Render();
    }

    private Task StartSession(bool host) => connection.Start(host, SystemInfo.deviceName, DemoEcho.ServiceId, echoStrategy);

    private void Render()
    {
        view.SetConnectionControls(connection.Active, echoStrategy);
        view.ClearPeers();
        foreach (var endpoint in connection.Discovered)
            view.Connect(endpoint.Name, () => Run(() => connection.Connect(endpoint.Id)));
        foreach (var request in connection.Requests)
            view.Confirm(request.Endpoint.Name, request.AuthenticationCode,
                () => Run(() => connection.Respond(request.Id, true)),
                () => Run(() => connection.Respond(request.Id, false)));
        foreach (var peer in connection.Peers)
            view.Peer(peer.Value, () => Run(() => connection.Disconnect(peer.Key)),
                () => Run(() => echo.Send(peer.Key)));
    }

    private async void Run(Func<Task> action)
    {
        int version = connection.SessionVersion;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version == connection.SessionVersion) connection.Report(ex.Message);
        }
    }

    private void Update() => view.UpdateSafeArea();

    private void OnApplicationPause(bool paused)
    {
        if (paused && connection.Active) connection.End("Session ended in the background. Rejoin when ready.");
    }

    private void OnDestroy() => connection.End("Session ended.");
}
