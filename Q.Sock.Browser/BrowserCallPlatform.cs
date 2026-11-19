using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.AspNetCore.SignalR.Client;
using Q.Sock.Core;
using Q.Sock.Shared.Models;
using Q.Sock.UI.Calls;

namespace Q.Sock.Browser;

public sealed class BrowserCallPlatform : ICallPlatform
{
    public ICallSession Create(MessengerClient client, Action<string> notice) =>
        new BrowserCallSession(client, notice);
}

internal sealed class BrowserCallSession : ICallSession
{
    private readonly MessengerClient client;
    private readonly Action<string> notice;
    private readonly List<IDisposable> subscriptions = [];
    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly TaskCompletionSource ready = new();
    private MessengerTheme theme = new();
    private bool disposed;

    public Control View { get; }
    public event Action<CallMediaState>? Changed;

    public BrowserCallSession(MessengerClient client, Action<string> notice)
    {
        this.client = client;
        this.notice = notice;
        View = new BrowserVideoHost(id, () =>
        {
            UpdateTheme();
            ready.TrySetResult();
        });
        BrowserMediaExports.Sessions.Add(id, this);
        foreach (var eventName in new[] { "RtcSignal", "PeerLeft", "CallEnded", "CallModerated" })
        {
            var capturedName = eventName;
            subscriptions.Add(
                client.Connection!.On<JsonElement>(
                    capturedName,
                    payload =>
                    {
                        if (!disposed && ready.Task.IsCompletedSuccessfully)
                            BrowserMedia.Receive(id, capturedName, payload.GetRawText());
                    }
                )
            );
        }
        client.Connection!.Reconnecting += Reconnecting;
    }

    private async Task Reconnecting(Exception? exception)
    {
        notice("Соединение потеряно. Подключитесь к звонку повторно после восстановления сети.");
        if (!disposed)
            await BrowserMedia.Command(id, "leave", "{}");
    }

    public async Task Join(CallInfo call)
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var ice = await client.Get<JsonElement>("api/calls/ice");
        var members = await client.Get<MemberInfo[]>($"api/chats/{call.ChatId}/members");
        var profile = await client.Get<Profile>("api/me");
        var data = JsonSerializer.Serialize(
            new BrowserMediaJoin
            {
                Call = call,
                AvatarColors = members.ToDictionary(
                    member => member.UserId.ToString(),
                    member => Q.Sock.Core.AvatarColors.For(member.UserId, AvatarKind.User)
                ),
                LocalAvatarColor = Q.Sock.Core.AvatarColors.For(profile.Id, AvatarKind.User),
                Ice = ice,
                ConnectionId = client.Connection!.ConnectionId!,
                Names = members.ToDictionary(
                    member => member.UserId.ToString(),
                    member =>
                        string.IsNullOrWhiteSpace(member.DisplayName)
                            ? member.UserName
                            : member.DisplayName
                ),
            },
            BrowserJsonContext.Default.BrowserMediaJoin
        );
        await BrowserMedia.Command(id, "join", data);
    }

    public Task ToggleMicrophone() => BrowserMedia.Command(id, "mic", "{}");

    public Task ToggleCamera() => BrowserMedia.Command(id, "camera", "{}");

    public Task ToggleScreen() => BrowserMedia.Command(id, "share", "{}");

    public void ApplyTheme(MessengerTheme theme)
    {
        this.theme = theme;
        if (!disposed && ready.Task.IsCompletedSuccessfully)
            UpdateTheme();
    }

    private void UpdateTheme() =>
        BrowserMedia.SetTheme(
            id,
            theme.Colors["background"],
            theme.Colors["panel"],
            theme.Colors["muted"],
            theme.Colors["text"]
        );

    internal void Update(string json) =>
        Changed?.Invoke(
            JsonSerializer.Deserialize(json, BrowserJsonContext.Default.CallMediaState)!
        );

    internal void Notice(string message) => notice(message);

    internal async Task<string> Invoke(string method, string arguments)
    {
        var values = JsonSerializer.Deserialize(
            arguments,
            BrowserJsonContext.Default.JsonElementArray
        )!;
        if (method == "JoinCall")
        {
            var result = await client.Connection!.InvokeAsync<JsonElement>(
                method,
                values[0].GetGuid()
            );
            return result.GetRawText();
        }
        if (method == "LeaveCall")
            await client.Connection!.InvokeAsync(method);
        else if (method == "Signal")
            await client.Connection!.InvokeAsync(
                method,
                values[0].GetString(),
                values[1].GetString(),
                values[2]
            );
        else
            throw new InvalidOperationException("Неизвестная команда медиамоста.");
        return "null";
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;
        disposed = true;
        try
        {
            if (ready.Task.IsCompletedSuccessfully)
                await BrowserMedia.Command(id, "dispose", "{}");
        }
        finally
        {
            foreach (var subscription in subscriptions)
                subscription.Dispose();
            client.Connection!.Reconnecting -= Reconnecting;
            BrowserMediaExports.Sessions.Remove(id);
        }
    }
}

internal sealed class BrowserVideoHost(string id, Action ready) : NativeControlHost
{
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = new JSObjectControlHandle(BrowserMedia.CreateHost(id));
        ready();
        return handle;
    }
}

internal static partial class BrowserMedia
{
    [JSImport("media.createHost", "qsock")]
    public static partial JSObject CreateHost(string id);

    [JSImport("media.command", "qsock")]
    public static partial Task Command(string id, string command, string data);

    [JSImport("media.setTheme", "qsock")]
    public static partial void SetTheme(string id, string background, string panel, string muted, string text);

    [JSImport("media.receive", "qsock")]
    public static partial void Receive(string id, string name, string data);
}

public static partial class BrowserMediaExports
{
    internal static readonly Dictionary<string, BrowserCallSession> Sessions = [];

    [JSExport]
    public static Task<string> Invoke(string id, string method, string arguments) =>
        Sessions[id].Invoke(method, arguments);

    [JSExport]
    public static void Update(string id, string state)
    {
        if (Sessions.TryGetValue(id, out var session))
            session.Update(state);
    }

    [JSExport]
    public static void Notice(string id, string message)
    {
        if (Sessions.TryGetValue(id, out var session))
            session.Notice(message);
    }
}
