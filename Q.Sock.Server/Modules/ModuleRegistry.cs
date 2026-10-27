using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Q.Sock.Server.Services;
using Q.Sock.Shared.Models;

namespace Q.Sock.Server.Modules;

public interface IMessengerModule
{
    string Id { get; }
    Task Initialize(IServiceProvider services) => Task.CompletedTask;
    bool IsEnabled(IConfiguration configuration) => true;
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(WebApplication app);
    UiModule Describe(IWebHostEnvironment environment);
}

public sealed class ServerModuleManifest
{
    public string Id { get; set; } = "";
    public string Assembly { get; set; } = "";
    public string EntryType { get; set; } = "";
    public int ApiVersion { get; set; } = 1;
}

public sealed class ModuleRegistry
{
    private readonly List<AssemblyLoadContext> contexts = [];
    public List<IMessengerModule> Modules { get; } = [];

    public static ModuleRegistry Load(IConfiguration configuration)
    {
        var registry = new ModuleRegistry();
        var directory = Path.GetFullPath(
            configuration["ServerPlugins:Directory"]
                ?? Path.Combine(AppContext.BaseDirectory, "server-plugins")
        );
        if (!Directory.Exists(directory))
            return registry;
        foreach (
            var manifestPath in Directory
                .EnumerateFiles(directory, "module.json", SearchOption.AllDirectories)
                .Order()
        )
        {
            var manifest =
                JsonSerializer.Deserialize<ServerModuleManifest>(
                    File.ReadAllText(manifestPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                ) ?? throw new InvalidDataException($"Пустой манифест: {manifestPath}");
            if (manifest.ApiVersion != 1 || !Regex.IsMatch(manifest.Id, "^[a-z][a-z0-9.-]{0,63}$"))
                throw new InvalidDataException($"Некорректный модуль: {manifestPath}");
            var root = Path.GetDirectoryName(manifestPath)!;
            var assemblyPath = Path.GetFullPath(Path.Combine(root, manifest.Assembly));
            var relativePath = Path.GetRelativePath(root, assemblyPath);
            if (
                Path.IsPathRooted(relativePath)
                || relativePath == ".."
                || relativePath.StartsWith(".." + Path.DirectorySeparatorChar)
                || !assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            )
                throw new InvalidDataException($"Сборка вне каталога модуля: {manifestPath}");
            var context = new ServerModuleLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
            if (
                !typeof(IMessengerModule).IsAssignableFrom(type)
                || Activator.CreateInstance(type) is not IMessengerModule module
                || module.Id != manifest.Id
                || registry.Modules.Any(item => item.Id == module.Id)
            )
                throw new InvalidDataException(
                    $"Несовместимый или повторный модуль: {manifestPath}"
                );
            registry.contexts.Add(context);
            if (module.IsEnabled(configuration))
                registry.Modules.Add(module);
        }
        return registry;
    }

    private sealed class ServerModuleLoadContext(string path)
        : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver resolver = new(path);

        protected override Assembly? Load(AssemblyName name)
        {
            var shared = Default.Assemblies.FirstOrDefault(assembly =>
                AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), name)
            );
            if (shared is not null)
                return shared;
            var resolved = resolver.ResolveAssemblyToPath(name);
            return resolved is null ? null : LoadFromAssemblyPath(resolved);
        }
    }
}

public static class ModuleExtensions
{
    public static void AddMessengerModules(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var registry = ModuleRegistry.Load(configuration);
        foreach (var module in registry.Modules)
            module.ConfigureServices(services, configuration);
        services.AddSingleton(registry);
        services.AddHostedService<LaunchNonceCleanupService>();
    }

    public static void MapMessengerModules(this WebApplication app)
    {
        foreach (var module in app.Services.GetRequiredService<ModuleRegistry>().Modules)
            module.MapEndpoints(app);
        app.MapGet(
            "/api/server",
            async (
                IConfiguration configuration,
                AccessService access,
                ExternalAuthentication authentication
            ) =>
            {
                var policy = await access.Policy();
                policy.AllowRegistration &= authentication.AllowsLocalRegistration;
                return new ServerInfo
                {
                    Name = configuration["Server:Name"] ?? "Q.Sock",
                    Policy = policy,
                };
            }
        );
        app.MapGet(
            "/api/ui/manifest",
            (ModuleRegistry registry, IWebHostEnvironment environment) =>
            {
                var modules = registry
                    .Modules.Select(module => module.Describe(environment))
                    .ToList();
                var version = Convert
                    .ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(modules)))
                    .ToLowerInvariant();
                return new UiManifest { Version = version, Modules = modules };
            }
        );
    }
}
