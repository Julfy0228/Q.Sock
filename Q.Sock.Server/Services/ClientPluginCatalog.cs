using System.Text.Json;
using Q.Sock.Core.Plugins;

namespace Q.Sock.Server.Services;

public sealed class ClientPluginCatalog(
    IWebHostEnvironment environment,
    IConfiguration configuration
)
{
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private string DirectoryPath =>
        Path.GetFullPath(
            configuration["ClientPlugins:Directory"] ?? "client-modules",
            environment.ContentRootPath
        );

    public async Task<IReadOnlyList<PluginPackage>> List(string target)
    {
        if (target is not ("avalonia" or "web" or "android"))
            throw new ApiException(400, "Неизвестная платформа клиента.");
        var result = new List<PluginPackage>();
        var root = DirectoryPath;
        if (!Directory.Exists(root))
            return result;
        foreach (
            var path in Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
        )
        {
            var package =
                JsonSerializer.Deserialize<PluginPackage>(await File.ReadAllTextAsync(path), json)
                ?? throw new InvalidDataException("Пустое описание клиентского модуля.");
            package.ValidateMetadata();
            if (package.Target != target)
                continue;
            var expected =
                $"/api/ui/plugins/assets/{package.Id}/{target}/{package.Sha256.ToLowerInvariant()}";
            if (package.AssetUrl != expected)
                throw new InvalidDataException("Адрес пакета не соответствует каталогу модулей.");
            result.Add(package);
        }
        if (result.GroupBy(package => package.Id).Any(group => group.Count() > 1))
            throw new InvalidDataException("В каталоге несколько активных версий одного модуля.");
        return result;
    }

    public async Task<byte[]> Asset(string id, string target, string hash)
    {
        var package =
            (await List(target)).SingleOrDefault(package =>
                package.Id == id && package.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase)
            ) ?? throw new ApiException(404, "Пакет модуля не найден.");
        var path = Path.Combine(DirectoryPath, package.Id, target, "module.dll");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 20 * 1024 * 1024)
            throw new ApiException(404, "Файл модуля недоступен.");
        var bytes = await File.ReadAllBytesAsync(path);
        package.Verify(bytes);
        return bytes;
    }
}
