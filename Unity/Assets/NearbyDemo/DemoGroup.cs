using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MessagePack;

internal sealed class DemoGroup
{
    public const string ServiceId = "com.example.nearbyconnections.group";
    public const int BoardSize = 5;
    public const int ColorCount = 5;
    public const int MaxText = 400;
    public const int MaxPhotoBytes = 240 * 1024;
    public const int MaxPhotoSide = 768;
    public const int MaxHistory = 24;
    public const int MaxParticipants = 12;

    private readonly DemoConnection connection;
    private readonly Dictionary<string, DemoMember> members = new();
    private readonly List<DemoChat> chat = new();
    private string hostId;
    private int memberId;
    private int nextMemberId;
    private int revision;
    private bool hasSnapshot;
    private int chatVersion;

    public event Action Changed;
    public event Action Started;
    public IReadOnlyList<DemoChat> Chat => chat;
    public DemoMember[] Members { get; private set; } = Array.Empty<DemoMember>();
    public int ChatVersion => chatVersion;
    public bool Joined => hasSnapshot;
    public bool InScene { get; private set; }
    public int Cell { get; private set; } = BoardSize * BoardSize / 2;
    public int Color { get; private set; }
    public bool Ready => Members.Any(member => member.Id == memberId && member.Ready);
    public bool CanStart => connection.IsHost && !InScene &&
        Members.Length >= 2 && Members.All(member => member.Ready);

    public DemoGroup(DemoConnection connection) => this.connection = connection;

    public void Reset()
    {
        members.Clear();
        chat.Clear();
        Members = Array.Empty<DemoMember>();
        hostId = null;
        memberId = 0;
        nextMemberId = 0;
        revision = 0;
        chatVersion++;
        hasSnapshot = false;
        InScene = false;
        Cell = BoardSize * BoardSize / 2;
        Color = 0;
        Changed?.Invoke();
    }

    public void Host(string name)
    {
        members.Add("", new DemoMember { Name = Name(name) });
        hasSnapshot = true;
        UpdateMembers();
    }

    public async Task Connect(string endpoint, string name)
    {
        if (connection.IsHost)
        {
            if (members.ContainsKey(endpoint)) return;
            if (members.Count >= MaxParticipants)
            {
                await connection.Disconnect(endpoint);
                return;
            }
            members.Add(endpoint, new DemoMember { Id = ++nextMemberId, Name = Name(name) });
            UpdateMembers();
            await ShareState();
        }
        else
        {
            if (hostId != null && hostId != endpoint)
            {
                await connection.Disconnect(endpoint);
                return;
            }
            hostId = endpoint;
            await ToHost(new DemoGroupPacket { Kind = DemoGroupKind.Sync });
        }
    }

    public async Task Disconnect(string endpoint)
    {
        if (connection.IsHost && members.Remove(endpoint))
        {
            UpdateMembers();
            await ShareState();
        }
        else if (endpoint == hostId) Reset();
    }

    public Task SetReady(bool ready) => Local(new DemoGroupPacket { Kind = DemoGroupKind.Ready, Value = ready ? 1 : 0 });
    public Task Move(int cell) => Local(new DemoGroupPacket { Kind = DemoGroupKind.Move, Value = cell });
    public Task CycleColor() => Local(new DemoGroupPacket { Kind = DemoGroupKind.Color });
    public Task SendText(string text) => Local(new DemoGroupPacket { Kind = DemoGroupKind.Text, Text = text?.Trim() });
    public Task SendPhoto(byte[] photo) => Local(new DemoGroupPacket { Kind = DemoGroupKind.Photo, Photo = photo });

    public async Task StartScene()
    {
        if (!CanStart) return;
        InScene = true;
        Changed?.Invoke();
        Started?.Invoke();
        await ShareState();
    }

    private Task Local(DemoGroupPacket packet)
    {
        if (!Joined) return Task.CompletedTask;
        return connection.IsHost ? Apply("", packet) : ToHost(packet);
    }

    private Task ToHost(DemoGroupPacket packet) => hostId == null ? Task.CompletedTask : Send(hostId, packet);

    public async Task Receive(string endpoint, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxPhotoBytes + 4096) return;
        DemoGroupPacket packet;
        try
        {
            packet = MessagePackSerializer.Deserialize<DemoGroupPacket>(bytes, MessagePackSerializerOptions.DefaultAot);
        }
        catch (MessagePackSerializationException)
        {
            return;
        }

        if (packet == null) return;
        if (connection.IsHost) await Apply(endpoint, packet);
        else if (endpoint == hostId)
        {
            if (packet.Kind == DemoGroupKind.State) AcceptState(packet);
            if (hasSnapshot && packet.Kind == DemoGroupKind.Chat && ValidChat(packet))
                AddChat(Name(packet.Name), packet.Text, packet.Photo);
        }
    }

    private async Task Apply(string endpoint, DemoGroupPacket packet)
    {
        if (!members.TryGetValue(endpoint, out var member)) return;

        switch (packet.Kind)
        {
            case DemoGroupKind.Sync when endpoint.Length != 0:
                await SendState(endpoint, member.Id);
                return;
            case DemoGroupKind.Ready when !InScene && member.Ready != (packet.Value == 1):
                member.Ready = packet.Value == 1;
                break;
            case DemoGroupKind.Move when InScene && ValidCell(packet.Value) && Cell != packet.Value:
                Cell = packet.Value;
                break;
            case DemoGroupKind.Color when InScene:
                Color = (Color + 1) % ColorCount;
                break;
            case DemoGroupKind.Text when ValidText(packet.Text):
                await Publish(new DemoGroupPacket { Kind = DemoGroupKind.Chat, Name = member.Name, Text = packet.Text });
                return;
            case DemoGroupKind.Photo when DemoPhotoBytes.Valid(packet.Photo):
                await Publish(new DemoGroupPacket { Kind = DemoGroupKind.Chat, Name = member.Name, Photo = packet.Photo });
                return;
            default:
                return;
        }

        Changed?.Invoke();
        await ShareState();
    }

    private Task Publish(DemoGroupPacket packet)
    {
        AddChat(packet.Name, packet.Text, packet.Photo);
        byte[] bytes = Pack(packet);
        return Task.WhenAll(members.Keys.Where(id => id.Length != 0).Select(id => connection.Send(id, bytes)));
    }

    private void AddChat(string sender, string text, byte[] photo)
    {
        if (chat.Count == MaxHistory) chat.RemoveAt(0);
        chat.Add(new DemoChat(sender, text, photo));
        chatVersion++;
        Changed?.Invoke();
    }

    private Task ShareState()
    {
        revision++;
        return Task.WhenAll(members.Where(pair => pair.Key.Length != 0)
            .Select(pair => SendState(pair.Key, pair.Value.Id)));
    }

    private Task SendState(string endpoint, int recipient) => Send(endpoint, new DemoGroupPacket
    {
        Kind = DemoGroupKind.State,
        Members = Members,
        Revision = revision,
        Value = recipient,
        Cell = Cell,
        Color = Color,
        InScene = InScene
    });

    private void AcceptState(DemoGroupPacket packet)
    {
        if ((hasSnapshot && packet.Revision <= revision) || !ValidCell(packet.Cell) ||
            packet.Color < 0 || packet.Color >= ColorCount || packet.Members == null ||
            packet.Members.Length == 0 || packet.Members.Length > MaxParticipants ||
            packet.Members.Any(member => member == null || string.IsNullOrWhiteSpace(member.Name) || member.Name.Length > 48) ||
            !packet.Members.Any(member => member.Id == packet.Value)) return;

        bool justStarted = !InScene && packet.InScene;
        hasSnapshot = true;
        revision = packet.Revision;
        memberId = packet.Value;
        Members = packet.Members;
        Cell = packet.Cell;
        Color = packet.Color;
        InScene = packet.InScene;
        Changed?.Invoke();
        if (justStarted) Started?.Invoke();
    }

    private void UpdateMembers()
    {
        Members = members.Values.OrderBy(member => member.Id).ToArray();
        Changed?.Invoke();
    }

    private Task Send(string endpoint, DemoGroupPacket packet) => connection.Send(endpoint, Pack(packet));
    private static byte[] Pack(DemoGroupPacket packet) =>
        MessagePackSerializer.Serialize(packet, MessagePackSerializerOptions.DefaultAot);

    private static bool ValidCell(int cell) => cell >= 0 && cell < BoardSize * BoardSize;
    private static bool ValidText(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxText;
    private static bool ValidChat(DemoGroupPacket packet) => packet.Photo == null
        ? ValidText(packet.Text) : DemoPhotoBytes.Valid(packet.Photo);

    private static string Name(string name) => string.IsNullOrWhiteSpace(name)
        ? "Nearby device" : name.Substring(0, Math.Min(48, name.Length));
}

internal sealed class DemoChat
{
    public readonly string Sender;
    public readonly string Text;
    public readonly byte[] Photo;

    public DemoChat(string sender, string text, byte[] photo) => (Sender, Text, Photo) = (sender, text, photo);
}

internal enum DemoGroupKind { None, Sync, State, Ready, Move, Color, Text, Photo, Chat }

[MessagePackObject]
internal sealed partial class DemoMember
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; }
    [Key(2)] public bool Ready { get; set; }
}

[MessagePackObject]
internal sealed partial class DemoGroupPacket
{
    [Key(0)] public DemoGroupKind Kind { get; set; }
    [Key(1)] public int Value { get; set; }
    [Key(2)] public int Revision { get; set; }
    [Key(3)] public DemoMember[] Members { get; set; }
    [Key(4)] public bool InScene { get; set; }
    [Key(5)] public int Cell { get; set; }
    [Key(6)] public int Color { get; set; }
    [Key(7)] public string Text { get; set; }
    [Key(8)] public string Name { get; set; }
    [Key(9)] public byte[] Photo { get; set; }
}
