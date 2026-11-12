using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Q.Sock.Core;
using Q.Sock.Shared.Models;

namespace Q.Sock.Tests.Signaling;

internal sealed class TestUser(MessengerClient client, Profile profile)
{
    private readonly Channel<JsonElement> calls = Channel.CreateUnbounded<JsonElement>();
    public MessengerClient Client { get; } = client;
    public Profile Profile { get; } = profile;

    public static async Task<TestUser> Create(string address, string prefix)
    {
        var client = new MessengerClient(address);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        await client.Login(
            prefix + suffix,
            "test-only-Password49",
            Guid.NewGuid().ToString(),
            "Signaling test",
            register: true
        );
        var user = new TestUser(client, await client.Get<Profile>("api/me"));
        client.IncomingCall += call => user.calls.Writer.TryWrite(call);
        await client.Connect();
        return user;
    }

    public Task<JsonElement> NextCall() =>
        calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

    public async Task ExpectNoCall()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await calls.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return;
        }
        throw new InvalidOperationException("Пришёл неожиданный входящий вызов.");
    }

    public Task<JsonElement> Join(Guid callId) =>
        Client.Connection!.InvokeAsync<JsonElement>("JoinCall", callId);
}
