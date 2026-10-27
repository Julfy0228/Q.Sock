using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Q.Sock.Shared.Models.Auth;

namespace Q.Sock.Core;

public sealed class MessengerClient : IAsyncDisposable
{
    public Uri Server { get; }
    private readonly HttpClient http;
    private readonly SemaphoreSlim refresh = new(1, 1);
    public AuthResponse? Session { get; private set; }
    public HubConnection? Connection { get; private set; }
    public event Action<AuthResponse>? SessionChanged;
    public event Action<JsonElement>? Changed;
    public event Action<JsonElement>? IncomingCall;
    public event Action? SessionRevoked;

    public MessengerClient(string server, HttpMessageHandler? handler = null)
    {
        Server = NormalizeServer(server);
        http = handler == null ? new() : new(handler);
        http.BaseAddress = Server;
        http.Timeout = TimeSpan.FromSeconds(30);
    }

    public static Uri NormalizeServer(string value)
    {
        if (
            !Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.UserInfo != ""
            || uri.AbsolutePath != "/"
            || uri.Query != ""
            || uri.Fragment != ""
        )
            throw new ArgumentException("Укажите адрес сервера: https://messenger.example.org");
        if (uri.Scheme == "http" && !uri.IsLoopback)
            throw new ArgumentException("Для удалённого сервера требуется HTTPS.");
        return uri;
    }

    public void Restore(AuthResponse auth) => Session = auth;

    public async Task<AuthResponse> Login(
        string username,
        string password,
        string deviceId,
        string deviceName,
        bool register = false,
        CancellationToken ct = default
    )
    {
        var auth = await PublicPost<AuthResponse>(
            register ? "api/auth/register" : "api/auth/login",
            new
            {
                userName = username,
                password,
                deviceId,
                deviceName,
            },
            ct
        );
        Session = auth;
        SessionChanged?.Invoke(auth);
        return auth;
    }

    public async Task<T> PublicGet<T>(string path, CancellationToken ct = default)
    {
        using var r = await http.GetAsync(Local(path), ct);
        return await Read<T>(r, ct);
    }

    public async Task<T> PublicPost<T>(string path, object body, CancellationToken ct = default)
    {
        using var r = await http.PostAsJsonAsync(Local(path), body, ct);
        return await Read<T>(r, ct);
    }

    public async Task<T> Get<T>(string path, CancellationToken ct = default)
    {
        using var r = await Send(HttpMethod.Get, path, null, ct);
        return await Read<T>(r, ct);
    }

    public async Task<T> Post<T>(string path, object? body = null, CancellationToken ct = default)
    {
        using var r = await Send(HttpMethod.Post, path, body, ct);
        return await Read<T>(r, ct);
    }

    public async Task<T> Delete<T>(string path, CancellationToken ct = default)
    {
        using var response = await Send(HttpMethod.Delete, path, null, ct);
        return await Read<T>(response, ct);
    }

    public async Task Mutate(
        string path,
        HttpMethod method,
        object? body = null,
        CancellationToken ct = default
    )
    {
        using var r = await Send(method, path, body, ct);
        if (!r.IsSuccessStatusCode)
            await Throw(r, ct);
    }

    public async Task<byte[]> Download(string path, CancellationToken ct = default) =>
        await Download(path, HttpMethod.Get, ct);

    public async Task<byte[]> DownloadLimited(
        string path,
        int maximumBytes,
        CancellationToken ct = default
    )
    {
        using var response = await Send(
            HttpMethod.Get,
            path,
            null,
            ct,
            HttpCompletionOption.ResponseHeadersRead
        );
        if (!response.IsSuccessStatusCode)
            await Throw(response, ct);
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("Размер модуля превышает допустимый предел.");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var destination = new MemoryStream();
        var buffer = new byte[65536];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (destination.Length + read > maximumBytes)
                throw new InvalidDataException("Размер модуля превышает допустимый предел.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return destination.ToArray();
    }

    public async Task<byte[]> Download(
        string path,
        HttpMethod method,
        CancellationToken ct = default
    )
    {
        using var r = await Send(method, path, null, ct);
        if (!r.IsSuccessStatusCode)
            await Throw(r, ct);
        return await r.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<T> Upload<T>(
        string path,
        Stream stream,
        string name,
        CancellationToken ct = default
    )
    {
        await EnsureToken(ct);
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(stream), "file", name);
        using var request = new HttpRequestMessage(HttpMethod.Post, Local(path)) { Content = form };
        request.Headers.Authorization = new("Bearer", Session!.AccessToken);
        using var r = await http.SendAsync(request, ct);
        return await Read<T>(r, ct);
    }

    private static string Local(string path)
    {
        if (!path.StartsWith("/"))
            path = "/" + path;
        if (path.StartsWith("//") || path.Contains('\\') || path.Contains(".."))
            throw new ArgumentException("Допускаются только локальные пути API.");
        return path;
    }

    private async Task<HttpResponseMessage> Send(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead
    )
    {
        await EnsureToken(ct);
        for (int attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, Local(path));
            request.Headers.Authorization = new("Bearer", Session!.AccessToken);
            if (body != null)
                request.Content = JsonContent.Create(body);
            var r = await http.SendAsync(request, completion, ct);
            if (r.StatusCode != HttpStatusCode.Unauthorized || attempt != 0)
                return r;
            r.Dispose();
            await Refresh(ct);
        }
    }

    public async Task EnsureToken(CancellationToken ct = default)
    {
        if (Session == null)
            throw new InvalidOperationException("Войдите в аккаунт.");
        if (Session.ExpiresAtUtc < DateTime.UtcNow.AddMinutes(1))
            await Refresh(ct);
    }

    public async Task Refresh(CancellationToken ct = default)
    {
        await refresh.WaitAsync(ct);
        try
        {
            if (Session == null)
                throw new InvalidOperationException("Нет сессии.");
            Session = await PublicPost<AuthResponse>(
                "api/auth/refresh",
                new { Session.SessionId, Session.RefreshToken },
                ct
            );
            SessionChanged?.Invoke(Session);
        }
        finally
        {
            refresh.Release();
        }
    }

    public async Task Connect(CancellationToken ct = default)
    {
        if (Connection != null)
            await Connection.DisposeAsync();
        Connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(Server, "chat"),
                o =>
                    o.AccessTokenProvider = async () =>
                    {
                        await EnsureToken();
                        return Session!.AccessToken;
                    }
            )
            .WithAutomaticReconnect()
            .Build();
        Connection.On<JsonElement>("Changed", x => Changed?.Invoke(x));
        Connection.On<JsonElement>("IncomingCall", x => IncomingCall?.Invoke(x));
        Connection.On(
            "SessionRevoked",
            () =>
            {
                Session = null;
                SessionRevoked?.Invoke();
            }
        );
        Connection.Reconnected += async _ =>
        {
            Changed?.Invoke(JsonSerializer.SerializeToElement(new { kind = "reconnected" }));
            await Task.CompletedTask;
        };
        await Connection.StartAsync(ct);
    }

    private static async Task<T> Read<T>(HttpResponseMessage r, CancellationToken ct)
    {
        if (!r.IsSuccessStatusCode)
            await Throw(r, ct);
        return (await r.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!;
    }

    private static async Task Throw(HttpResponseMessage r, CancellationToken ct)
    {
        var text = await r.Content.ReadAsStringAsync(ct);
        try
        {
            using var d = JsonDocument.Parse(text);
            if (d.RootElement.TryGetProperty("error", out var e))
                text = e.GetString()!;
            else if (d.RootElement.TryGetProperty("status", out _))
                text = r.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Неверные данные входа или сессия завершена.",
                    HttpStatusCode.Forbidden => "Недостаточно прав для этого действия.",
                    HttpStatusCode.NotFound => "Запрошенный объект не найден.",
                    HttpStatusCode.TooManyRequests =>
                        "Слишком много запросов. Повторите немного позже.",
                    _ => $"Сервер вернул ошибку {(int)r.StatusCode}.",
                };
        }
        catch (JsonException) { }
        throw new HttpRequestException(text.Length > 500 ? text[..500] : text, null, r.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (Connection != null)
            await Connection.DisposeAsync();
        http.Dispose();
        refresh.Dispose();
    }
}
