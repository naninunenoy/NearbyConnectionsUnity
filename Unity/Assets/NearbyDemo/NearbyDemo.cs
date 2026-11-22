using System;
using System.Linq;
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
    private DemoAlbumView album;
    private bool albumAccess;
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
        album = new DemoAlbumView(view.OverlayRoot);
        albumAccess = DemoAlbum.HasAccess();
        groupView = new DemoGroupView(view.Content, view.OverlayRoot,
            () => Run(() => group.SetReady(!group.Ready)), () => Run(group.StartScene),
            text => Run(() => group.SendText(text)), OpenAlbum, EnableAlbum,
            cell => Run(() => group.Move(cell)), () => Run(group.CycleColor));

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
            if (!IsEcho)
            {
                Run(() => group.Disconnect(id));
                if (!group.Joined)
                {
                    album.Close();
                    groupView.Reset();
                }
            }
        };
        connection.Ended += () =>
        {
            group.Reset();
            groupView.Reset();
            album.Close();
        };
        connection.Received += (peer, bytes) =>
            Run(() => IsEcho ? echo.Receive(peer, bytes) : group.Receive(peer, bytes));
        group.Changed += RenderGroup;
        group.Started += () => SelectTab(DemoTab.SharedScene);
        view.SetStatus("Choose Host on one device and Join on another.");
        Render();
    }

    private Task StartSession(bool host)
    {
        if (!IsEcho) RefreshAlbumAccess();
        return connection.Start(host, SystemInfo.deviceName, IsEcho ? DemoEcho.ServiceId : DemoGroup.ServiceId,
            IsEcho ? echoStrategy : NearbyStrategy.Star);
    }

    private void SelectTab(DemoTab selected)
    {
        if (connection.Active && IsEcho != (selected == DemoTab.Echo)) return;
        if (selected != tab) album.Close();
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

    private void RenderGroup()
    {
        view.PlacePeers(tab == DemoTab.SharedScene && group.InScene && !connection.Requests.Any());
        groupView.Render(tab, group, connection.IsHost, connection.Active, DemoAlbum.Available, albumAccess);
    }

    private void EnableAlbum()
    {
        RefreshAlbumAccess();
        if (albumAccess) return;
        DemoAlbum.RequestAccess();
        connection.Report("Allow access to photos before starting a session.");
    }

    private void OpenAlbum()
    {
        if (group.Joined && albumAccess) Run(() => album.Open(bytes => RunAction(() => group.SendPhoto(bytes))));
    }

    private void Run(Func<Task> action) => _ = RunAction(action);

    private async Task RunAction(Func<Task> action)
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
        else if (!paused) RefreshAlbumAccess();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused) RefreshAlbumAccess();
    }

    private void RefreshAlbumAccess()
    {
        if (!DemoAlbum.Available || groupView == null) return;
        bool access = DemoAlbum.HasAccess();
        if (albumAccess == access) return;
        albumAccess = access;
        if (access && !connection.Active) connection.Report("Photo access enabled. Host a room or join a nearby host.");
        RenderGroup();
    }

    private void OnDestroy()
    {
        if (view == null) return;
        connection.Changed -= Render;
        connection.StatusChanged -= view.SetStatus;
        group.Changed -= RenderGroup;
        if (connection.Active) connection.End("Session ended.");
        else
        {
            groupView?.Reset();
            album?.Close();
        }
    }
}
