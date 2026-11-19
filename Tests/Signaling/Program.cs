using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Q.Sock.Core;
using Q.Sock.Shared.Models;

namespace Q.Sock.Tests.Signaling;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var address = args.FirstOrDefault() ?? "http://127.0.0.1:5210";
        var alice = await TestUser.Create(address, "Alice");
        var bob = await TestUser.Create(address, "Bob");
        var charlie = await TestUser.Create(address, "Charlie");
        try
        {
            var direct = await alice.Client.Post<JsonElement>(
                "api/chats",
                new CreateChat { Type = "direct", UserIds = [bob.Profile.Id] }
            );
            var directId = direct.GetProperty("id").GetGuid();
            var changes = Channel.CreateUnbounded<JsonElement>();
            using var changeSubscription = bob.Client.Connection!.On<JsonElement>(
                "Changed",
                change => changes.Writer.TryWrite(change)
            );
            var message = await alice.Client.Post<JsonElement>(
                $"api/chats/{directId}/messages",
                new SendMessageRequest { Text = "Realtime delivery", ClientOperationId = Guid.NewGuid() }
            );
            using var deliveryDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            JsonElement notification;
            do
            {
                notification = await changes.Reader.ReadAsync(deliveryDeadline.Token);
            } while (notification.GetProperty("kind").GetString() != "message");
            Check(
                notification.GetProperty("chatId").GetGuid() == directId
                    && notification.GetProperty("value").GetProperty("id").GetGuid()
                        == message.GetProperty("id").GetGuid(),
                "Участник чата должен сразу получить событие нового сообщения."
            );
            Check(
                (await alice.Client.Get<ChatInfo[]>("api/chats")).Single(chat => chat.Id == directId).Name
                    == bob.Profile.UserName,
                "Название личного чата должно показывать собеседника."
            );
            var call = await alice.Client.Post<CallInfo>(
                "api/calls",
                new CallRequest
                {
                    ChatId = directId,
                    RingAll = false,
                    RingUserIds = [charlie.Profile.Id],
                    ExcludedUserIds = [bob.Profile.Id],
                }
            );
            Check(
                (await bob.NextCall()).GetProperty("id").GetGuid() == call.Id,
                "Личный звонок должен вызывать собеседника независимо от фильтров."
            );
            await charlie.ExpectNoCall();
            await alice.Join(call.Id);
            await bob.Join(call.Id);
            await ExpectHubDenied(() => charlie.Join(call.Id));
            await ExpectDenied(
                () =>
                    alice.Client.Mutate(
                        $"api/calls/{call.Id}/members/{bob.Profile.Id}/mute",
                        HttpMethod.Post
                    ),
                HttpStatusCode.BadRequest
            );

            var signals = Channel.CreateUnbounded<JsonElement>();
            using var signalSubscription = bob.Client.Connection!.On<JsonElement>(
                "RtcSignal",
                value => signals.Writer.TryWrite(value)
            );
            await alice.Client.Connection!.InvokeAsync(
                "Signal",
                bob.Client.Connection!.ConnectionId!,
                "candidate",
                new { candidate = "test-only" }
            );
            var signal = await signals.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Check(
                signal.GetProperty("from").GetString() == alice.Client.Connection!.ConnectionId,
                "Сигнал должен содержать подлинного отправителя."
            );
            await ExpectHubDenied(() =>
                charlie.Client.Connection!.InvokeAsync(
                    "Signal",
                    bob.Client.Connection.ConnectionId!,
                    "candidate",
                    new { candidate = "outsider" }
                )
            );
            await bob.Client.Mutate($"api/calls/{call.Id}/end", HttpMethod.Post);
            Check(
                (await alice.Client.Get<CallInfo>($"api/calls/{call.Id}")).Status == "ended",
                "Оба участника могут завершить личный звонок."
            );

            var group = await alice.Client.Post<JsonElement>(
                "api/chats",
                new CreateChat
                {
                    Name = "Signaling test",
                    Type = "group",
                    UserIds = [bob.Profile.Id, charlie.Profile.Id],
                }
            );
            var groupId = group.GetProperty("id").GetGuid();
            var groupCall = await alice.Client.Post<CallInfo>(
                "api/calls",
                new CallRequest
                {
                    ChatId = groupId,
                    RingAll = false,
                    RingUserIds = [bob.Profile.Id],
                }
            );
            Check(
                (await bob.NextCall()).GetProperty("id").GetGuid() == groupCall.Id,
                "Выборочный групповой вызов должен вызвать выбранного участника."
            );
            await charlie.ExpectNoCall();
            await alice.Join(groupCall.Id);
            await bob.Join(groupCall.Id);
            await alice.Client.Mutate(
                $"api/calls/{groupCall.Id}/members/{bob.Profile.Id}/deny-share",
                HttpMethod.Post
            );
            await bob.Client.Connection!.InvokeAsync("LeaveCall");
            var rejoined = await bob.Join(groupCall.Id);
            Check(
                !rejoined.GetProperty("canShare").GetBoolean(),
                "Повторное подключение не должно снимать запрет демонстрации."
            );
            await alice.Client.Mutate($"api/calls/{groupCall.Id}/end", HttpMethod.Post);

            var guild = await alice.Client.Post<JsonElement>(
                "api/guilds",
                new NamedRequest { Name = "Voice room test" }
            );
            var guildId = guild.GetProperty("id").GetGuid();
            await alice.Client.Mutate(
                $"api/guilds/{guildId}/members/{bob.Profile.Id}",
                HttpMethod.Put,
                new MemberUpdate()
            );
            var room = await alice.Client.Post<JsonElement>(
                "api/chats",
                new CreateChat
                {
                    Type = "room",
                    GuildId = guildId,
                    Name = "Комната",
                }
            );
            var roomCall = await alice.Client.Post<CallInfo>(
                "api/calls",
                new CallRequest { ChatId = room.GetProperty("id").GetGuid(), RingAll = true }
            );
            await bob.ExpectNoCall();
            await alice.Join(roomCall.Id);
            await bob.Join(roomCall.Id);
            var departed = new TaskCompletionSource();
            using var leftSubscription = alice.Client.Connection!.On<JsonElement>(
                "PeerLeft",
                value =>
                {
                    if (
                        value.GetProperty("connectionId").GetString() == bob.Client.Connection!.ConnectionId
                    )
                        departed.TrySetResult();
                }
            );
            await alice.Client.Mutate(
                $"api/chats/{roomCall.ChatId}/members/{bob.Profile.Id}",
                HttpMethod.Put,
                new MemberUpdate { CanConnect = false }
            );
            await departed.Task.WaitAsync(TimeSpan.FromSeconds(6));
            await ExpectHubDenied(() => bob.Join(roomCall.Id));
            var remaining = await alice.Client.Get<JsonElement[]>($"api/calls/{roomCall.Id}/members");
            Check(
                remaining.Length == 1 && remaining[0].GetProperty("userId").GetGuid() == alice.Profile.Id,
                "Отзыв доступа должен отключать участника действующего звонка."
            );
            Console.WriteLine(
                "PASS: personal naming and ringing, recipient end, group selection, room silence, scoped signaling, moderation persistence, live access revocation."
            );
        }
        finally
        {
            await alice.Client.DisposeAsync();
            await bob.Client.DisposeAsync();
            await charlie.Client.DisposeAsync();
        }

        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static async Task ExpectHubDenied(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (HubException)
            {
                return;
            }
            throw new InvalidOperationException("Сервер разрешил недопустимую команду SignalR.");
        }

        static async Task ExpectDenied(Func<Task> action, HttpStatusCode expected)
        {
            try
            {
                await action();
            }
            catch (HttpRequestException exception) when (exception.StatusCode == expected)
            {
                return;
            }
            throw new InvalidOperationException($"Ожидался HTTP {(int)expected}.");
        }
    }
}
