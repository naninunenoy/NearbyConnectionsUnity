using System;
using System.Threading.Tasks;
using NearbyConnections;
using UnityEngine;

internal sealed class NearbyDemo : MonoBehaviour
{
    private readonly DemoConnection connection = new();
    private DemoEcho echo;
    private DemoGroup group;
    private DemoView view;
    private DemoGroupView groupView;
    private DemoTab tab;
    private NearbyStrategy echoStrategy;

    private bool IsEcho => tab == DemoTab.Echo;

    private void Awake()
    {
        echo = new DemoEcho(connection);
        group = new DemoGroup(connection);
        view = new DemoView(transform);
        view.BuildTabs(SelectTab);
        view.BuildConnection(
            () => Run(() => StartSession(true)),
            () => Run(() => StartSession(false)),
            () => connection.End("Session ended."),
            () => { echoStrategy = echoStrategy == NearbyStrategy.Star ? NearbyStrategy.PointToPoint : NearbyStrategy.Star; Render(); });
        groupView = new DemoGroupView(view.Content, view.OverlayRoot,
            () => Run(() => group.SetReady(!group.Ready)), () => Run(group.StartScene),
            text => Run(() => group.SendText(text)), cell => Run(() => group.Move(cell)), () => Run(group.CycleColor));

        connection.Changed += Render;
        connection.StatusChanged += view.SetStatus;
        connection.Opened += () =>
        {
            if (!IsEcho && connection.IsHost) group.Host(connection.LocalName);
        };
        connection.Connected += (id, name) =>
        {
            if (!IsEcho) Run(() => group.Connect(id, name));
        };
        connection.Disconnected += id =>
        {
            if (!IsEcho) Run(() => group.Disconnect(id));
        };
        connection.Ended += () =>
        {
            group.Reset();
            groupView.Reset();
        };
        connection.Received += (peer, bytes) =>
            Run(() => IsEcho ? echo.Receive(peer, bytes) : group.Receive(peer, bytes));
        group.Changed += RenderGroup;
        group.Started += () => SelectTab(DemoTab.SharedScene);
        view.SetStatus("Choose Host on one device and Join on another.");
        Render();
    }

    private Task StartSession(bool host) => connection.Start(host, SystemInfo.deviceName,
        IsEcho ? DemoEcho.ServiceId : DemoGroup.ServiceId, IsEcho ? echoStrategy : NearbyStrategy.Star);

    private void SelectTab(DemoTab selected)
    {
        if (connection.Active && IsEcho != (selected == DemoTab.Echo)) return;
        tab = selected;
        Render();
        view.ResetScroll();
    }

    private void Render()
    {
        view.SetTab(tab, connection.Active);
        view.SetConnectionControls(connection.Active, IsEcho, echoStrategy);
        view.ClearPeers();
        if (IsEcho || connection.Peers.Count == 0)
        {
            foreach (var endpoint in connection.Discovered)
                view.Connect(endpoint.Name, () => Run(() => connection.Connect(endpoint.Id)));
        }
        foreach (var request in connection.Requests)
            view.Confirm(request.Endpoint.Name, request.AuthenticationCode,
                () => Run(() => connection.Respond(request.Id, true)),
                () => Run(() => connection.Respond(request.Id, false)));
        foreach (var peer in connection.Peers)
        {
            var id = peer.Key;
            view.Peer(peer.Value, () => Run(() => connection.Disconnect(id)),
                IsEcho ? () => Run(() => echo.Send(id)) : null);
        }
        RenderGroup();
    }

    private void RenderGroup() => groupView.Render(tab, group, connection.IsHost);

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

    private void Update() => view?.UpdateSafeArea();

    private void OnApplicationPause(bool paused)
    {
        if (paused && connection.Active) connection.End("Session ended in the background. Rejoin when ready.");
    }

    private void OnDestroy()
    {
        if (connection.Active) connection.End("Session ended.");
        else groupView?.Reset();
    }
}
