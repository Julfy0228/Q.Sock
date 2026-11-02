using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Q.Sock.Core;

public sealed record DocumentationPage(string Path, string Title, string? RequiresModule = null);

public sealed record DocumentationIndex(int Version, string Home, DocumentationPage[] Pages);

public sealed record DocumentationResource(byte[] Bytes, bool FromCache);

public sealed class DocumentationClient(
    HttpClient http,
    IClientPreferences preferences,
    Uri? origin = null
)
{
    public static readonly Uri PublishedOrigin = new("https://julfy0228.github.io/Q.Sock.Docs/");
    public Uri Origin { get; } = origin ?? PublishedOrigin;

    public Uri Resolve(string path, Uri? current = null)
    {
        var address = new Uri(current ?? Origin, path);
        if (
            address.Scheme != Origin.Scheme
            || address.Authority != Origin.Authority
            || !address.AbsolutePath.StartsWith(Origin.AbsolutePath, StringComparison.Ordinal)
        )
            throw new ArgumentException("Ссылка находится вне руководства.");
        return address;
    }

    public bool IsInternal(Uri address) =>
        address.Scheme == Origin.Scheme
        && address.Authority == Origin.Authority
        && address.AbsolutePath.StartsWith(Origin.AbsolutePath, StringComparison.Ordinal);

    public async Task<DocumentationResource> Read(
        Uri address,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsInternal(address))
            throw new ArgumentException("Ресурс находится вне руководства.");
        var resource = new UriBuilder(address) { Fragment = "" }.Uri;
        var key =
            "docs-"
            + Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(resource.AbsoluteUri))
            );
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await http.GetAsync(
                resource,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );
            response.EnsureSuccessStatusCode();
            const int maximum = 2 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maximum)
                throw new InvalidDataException("Файл справки слишком большой.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + read > maximum)
                    throw new InvalidDataException("Файл справки слишком большой.");
                output.Write(buffer, 0, read);
            }
            var bytes = output.ToArray();
            try
            {
                await preferences.Write(key, Convert.ToBase64String(bytes));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            return new(bytes, false);
        }
        catch (Exception error)
            when (error is HttpRequestException or OperationCanceledException
                && !cancellationToken.IsCancellationRequested
            )
        {
            var cached = await preferences.Read(key);
            if (cached is null)
                throw new HttpRequestException(
                    "Не удалось загрузить справку. Проверьте подключение к Интернету.",
                    error
                );
            return new(Convert.FromBase64String(cached), true);
        }
    }

    public async Task<DocumentationIndex> LoadIndex(CancellationToken cancellationToken = default)
    {
        var resource = await Read(Resolve("navigation.json"), cancellationToken);
        var index =
            JsonSerializer.Deserialize(resource.Bytes, DocumentationJson.Default.DocumentationIndex)
            ?? throw new InvalidDataException("Оглавление справки пустое.");
        foreach (var page in index.Pages)
            Resolve(page.Path);
        return index;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DocumentationIndex))]
internal partial class DocumentationJson : JsonSerializerContext;
