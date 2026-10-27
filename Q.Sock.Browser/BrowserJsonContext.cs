using System.Text.Json;
using System.Text.Json.Serialization;
using Q.Sock.Shared.Models;
using Q.Sock.UI.Calls;

namespace Q.Sock.Browser;

[JsonSerializable(typeof(List<BrowserAccount>))]
[JsonSerializable(typeof(BrowserMediaJoin))]
[JsonSerializable(typeof(CallMediaState))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSerializable(typeof(string[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
internal partial class BrowserJsonContext : JsonSerializerContext;

internal sealed class BrowserMediaJoin
{
    public required CallInfo Call { get; init; }
    public required JsonElement Ice { get; init; }
    public required string ConnectionId { get; init; }
    public required Dictionary<string, string> AvatarColors { get; init; }
    public required string LocalAvatarColor { get; init; }
    public required Dictionary<string, string> Names { get; init; }
}
