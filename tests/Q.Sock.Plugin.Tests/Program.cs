using System.Net;
using System.Security.Cryptography;
using Q.Sock.Core;
using Q.Sock.Core.Plugins;
using Q.Sock.Shared.Models.Auth;
using Q.Sock.UI.Plugins;

var source =
    args.FirstOrDefault() ?? "Q.Sock.Plugins.Moodle/bin/Debug/net10.0/Q.Sock.Plugins.Moodle.dll";
var bytes = await File.ReadAllBytesAsync(source);
using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var package = new PluginPackage
{
    Id = "moodle",
    Version = "1.0.0",
    Target = "avalonia",
    Title = "Планирование занятий",
    EntryType = "Q.Sock.Plugins.Moodle.MoodleClientPlugin",
    Slot = "lesson-planning",
    Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
    PublisherKey = Convert.ToBase64String(publisher.ExportSubjectPublicKeyInfo()),
    AssetUrl = "/api/ui/plugins/assets/moodle/avalonia/test",
};
Sign(package, publisher);
var preferences = new MemoryPreferences();
var trust = new PublisherTrustStore(preferences);
var loader = new TrustedPluginLoader(trust);
var server = new Uri("https://module-test.example/");
Check(!HasModule(), "Тестовый модуль не должен входить в зависимости приложения.");
await Denied(() => loader.Load(server, package, bytes));
Check(!HasModule(), "Модуль был загружен до подтверждения издателя.");
await trust.Trust(server, package);
await using var loaded = await loader.Load(server, package, bytes);
Check(loaded.Instance.Id == "moodle", "Не удалось создать реальный отдельно собранный модуль.");
Check(loaded.Instance is IClientPlugin, "Контракт дублируется между контекстами загрузки.");
await Denied(() => loader.Load(new Uri("https://another-server.example/"), package, bytes));
var title = package.Title;
package.Title += " подмена";
await Invalid(() => loader.Load(server, package, bytes));
package.Title = title;
var altered = bytes.ToArray();
altered[^1] ^= 1;
await Invalid(() => loader.Load(server, package, altered));
var signature = package.Signature;
package.Signature = "";
await Invalid(() => loader.Load(server, package, bytes));
package.Signature = signature;

var directory = Path.Combine("artifacts", "plugin-tests", Guid.NewGuid().ToString("N"));
var cache = new FilePluginCache(directory);
var handler = new ModuleHandler(bytes);
await using var client = new MessengerClient(server.AbsoluteUri, handler);
client.Restore(
    new AuthResponse
    {
        AccessToken = "synthetic-test-token",
        RefreshToken = "synthetic-refresh",
        ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
    }
);
var installer = new ClientPluginInstaller(cache, trust);
await using var installed =
    await installer.Install(
        client,
        package,
        _ => throw new InvalidOperationException("Повторное доверие не должно запрашиваться.")
    ) ?? throw new InvalidOperationException("Пакет не установлен.");
Check(handler.Downloads == 1, "Первый пакет должен скачиваться с сервера.");
await using var cached = await installer.Install(client, package, _ => Task.FromResult(false));
Check(
    cached is not null && handler.Downloads == 1,
    "Повторный запуск должен использовать проверенный кэш."
);

var serverKey = PluginCacheKeys.Server(server);
var key = PluginCacheKeys.Package(package);
await cache.Write(serverKey, key, "corrupted cache"u8.ToArray());
await using var repaired = await installer.Install(client, package, _ => Task.FromResult(false));
Check(repaired is not null && handler.Downloads == 2, "Испорченный кэш должен загружаться заново.");
await cache.Write(serverKey, "obsolete", bytes);
var anotherKey = PluginCacheKeys.Server(new Uri("https://another-server.example/"));
await cache.Write(anotherKey, "another", bytes);
await cache.Prune(serverKey, [key]);
Check(
    await cache.Read(serverKey, "obsolete") is null
        && await cache.Read(anotherKey, "another") is not null,
    "Очистка должна удалять только устаревшие пакеты текущего сервера."
);
package.Version = "1.1.0";
Sign(package, publisher);
await using var update = await loader.Load(server, package, bytes);
Check(
    update.Instance.GetType().Assembly != loaded.Instance.GetType().Assembly,
    "Обновление должно загружаться в отдельный контекст."
);
using var changedPublisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
package.PublisherKey = Convert.ToBase64String(changedPublisher.ExportSubjectPublicKeyInfo());
Sign(package, changedPublisher);
await Denied(() => loader.Load(server, package, bytes));
Console.WriteLine(
    "PASS: unsigned and untrusted rejection before load, real DLL loading, shared contract, origin isolation, metadata and byte tampering, cache reuse and repair, scoped pruning, updates and publisher rotation."
);

static bool HasModule() =>
    AppDomain
        .CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "Q.Sock.Plugins.Moodle");

static void Sign(PluginPackage package, ECDsa publisher) =>
    package.Signature = Convert.ToBase64String(
        publisher.SignData(
            package.SigningPayload(),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        )
    );

static void Check(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

static async Task Denied(Func<Task<LoadedClientPlugin>> action)
{
    try
    {
        await using var result = await action();
    }
    catch (UnauthorizedAccessException)
    {
        return;
    }
    throw new InvalidOperationException("Неодобренный модуль получил право запуска.");
}

static async Task Invalid(Func<Task<LoadedClientPlugin>> action)
{
    try
    {
        await using var result = await action();
    }
    catch (InvalidDataException)
    {
        return;
    }
    throw new InvalidOperationException("Подменённый модуль прошёл проверку.");
}

sealed class MemoryPreferences : IClientPreferences
{
    private readonly Dictionary<string, string> values = [];

    public Task<string?> Read(string key) => Task.FromResult(values.GetValueOrDefault(key));

    public Task Write(string key, string value)
    {
        values[key] = value;
        return Task.CompletedTask;
    }
}

sealed class ModuleHandler(byte[] bytes) : HttpMessageHandler
{
    public int Downloads { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Downloads++;
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
        );
    }
}
