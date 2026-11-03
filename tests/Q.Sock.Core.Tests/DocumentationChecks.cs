using System.Net;
using System.Text;
using Q.Sock.Core;

internal static class DocumentationChecks
{
    public static async Task Run()
    {
        var transport = new Transport();
        var preferences = new Preferences();
        using var http = new HttpClient(transport);
        var client = new DocumentationClient(http, preferences);
        var current = client.Resolve("guide/chats.md");
        Check(
            client
                .Resolve("../help/troubleshooting.md", current)
                .AbsolutePath.EndsWith("/help/troubleshooting.md"),
            "Относительная ссылка потеряла папку."
        );
        Check(client.Resolve("#проверка", current).Fragment.Length > 0, "Якорь потерян.");
        foreach (
            var target in new[]
            {
                "../../outside.md",
                "https://example.org/a.md",
                "file:///C:/test.md",
            }
        )
        {
            try
            {
                client.Resolve(target, current);
                throw new InvalidOperationException("Ссылка вне руководства принята.");
            }
            catch (ArgumentException) { }
        }
        transport.Content = "# Проверка\n\n*Курсив*";
        var first = await client.Read(current);
        Check(
            !first.FromCache && Encoding.UTF8.GetString(first.Bytes) == transport.Content,
            "Markdown повреждён при загрузке."
        );
        transport.Offline = true;
        var reopened = new DocumentationClient(http, preferences);
        var offline = await reopened.Read(current);
        Check(
            offline.FromCache && first.Bytes.SequenceEqual(offline.Bytes),
            "Кэш не работает без сети."
        );
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await client.Read(current, canceled.Token);
            throw new InvalidOperationException("Отмена загрузки проигнорирована.");
        }
        catch (OperationCanceledException) { }
        transport.Offline = false;
        transport.Content =
            "{\"version\":1,\"home\":\"overview.md\",\"pages\":[{\"path\":\"guide/chats.md\",\"title\":\"Чаты\"}]}";
        var index = await client.LoadIndex();
        Check(index.Pages.Single().Title == "Чаты", "Оглавление не разобрано.");
        transport.Content = new string('x', 2 * 1024 * 1024 + 1);
        try
        {
            await client.Read(client.Resolve("oversize.md"));
            throw new InvalidOperationException("Превышен лимит ресурса.");
        }
        catch (InvalidDataException) { }
        Console.WriteLine(
            "PASS: documentation relative links, origin restrictions, network loading, offline cache, cancellation, index and size limit."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Preferences : IClientPreferences
    {
        private readonly Dictionary<string, string> values = [];

        public Task<string?> Read(string key) => Task.FromResult(values.GetValueOrDefault(key));

        public Task Write(string key, string value)
        {
            values[key] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class Transport : HttpMessageHandler
    {
        public bool Offline { get; set; }
        public string Content { get; set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Offline)
                throw new HttpRequestException("Нет сети");
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Content) }
            );
        }
    }
}
