using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MessagePack;
using UnityEngine;

internal sealed class DemoEcho
{
    public const string ServiceId = "com.example.nearbyconnections.demo";
    private readonly DemoConnection connection;
    private readonly Dictionary<string, (Guid token, double time)> pings = new();

    public DemoEcho(DemoConnection connection)
    {
        this.connection = connection;
        connection.Disconnected += id => pings.Remove(id);
        connection.Ended += pings.Clear;
    }

    public async Task Send(string endpoint)
    {
        var message = new EchoMessage { Token = Guid.NewGuid() };
        pings[endpoint] = (message.Token, Time.realtimeSinceStartupAsDouble);
        connection.Report($"Waiting for echo from {endpoint}…");
        await connection.Send(endpoint, Pack(message));
    }

    public async Task Receive(string endpoint, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 64) return;
        EchoMessage message;
        try
        {
            message = MessagePackSerializer.Deserialize<EchoMessage>(bytes, MessagePackSerializerOptions.DefaultAot);
        }
        catch (MessagePackSerializationException)
        {
            return;
        }

        if (message == null || message.Token == Guid.Empty) return;
        if (!message.IsReply)
        {
            message.IsReply = true;
            int session = connection.SessionVersion;
            byte[] reply = Pack(message);
            await connection.Send(endpoint, reply);
            if (connection.SessionVersion == session)
                connection.Report($"Echoed {reply.Length} bytes to {endpoint}.");
        }
        else if (pings.TryGetValue(endpoint, out var ping) && message.Token == ping.token)
        {
            pings.Remove(endpoint);
            double ms = (Time.realtimeSinceStartupAsDouble - ping.time) * 1000;
            connection.Report($"Verified {bytes.Length} bytes from {endpoint}. RTT {ms:F1} ms.");
        }
    }

    private static byte[] Pack(EchoMessage message) =>
        MessagePackSerializer.Serialize(message, MessagePackSerializerOptions.DefaultAot);
}

[MessagePackObject]
internal sealed partial class EchoMessage
{
    [Key(0)] public bool IsReply { get; set; }
    [Key(1)] public Guid Token { get; set; }
}
