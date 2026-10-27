using System.Reflection;
using System.Runtime.Loader;
using Q.Sock.Core.Plugins;

namespace Q.Sock.UI.Plugins;

public sealed class LoadedClientPlugin(
    PluginPackage package,
    IClientPlugin instance,
    AssemblyLoadContext context
) : IAsyncDisposable
{
    public PluginPackage Package { get; } = package;
    public IClientPlugin Instance { get; } = instance;

    public async ValueTask DisposeAsync()
    {
        if (Instance is IAsyncDisposable asynchronous)
            await asynchronous.DisposeAsync();
        else if (Instance is IDisposable disposable)
            disposable.Dispose();
        if (context.IsCollectible)
            context.Unload();
    }
}

public sealed class TrustedPluginLoader(
    PublisherTrustStore trust,
    IPluginSignatureVerifier? signatures = null
)
{
    public async Task<LoadedClientPlugin> Load(Uri server, PluginPackage package, byte[] bytes)
    {
        await (signatures ?? new ManagedPluginSignatureVerifier()).Verify(package, bytes);
        if (!await trust.IsTrusted(server, package))
            throw new UnauthorizedAccessException(
                "Издатель модуля ещё не одобрен для этого сервера."
            );
        var context = new ClientPluginLoadContext(
            $"{package.Id}:{package.Version}:{package.Sha256}"
        );
        IClientPlugin? instance = null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var assembly = context.LoadFromStream(stream);
            var entry = assembly.GetType(package.EntryType, throwOnError: true)!;
            if (
                !typeof(IClientPlugin).IsAssignableFrom(entry)
                || entry.IsAbstract
                || !entry.IsPublic
            )
                throw new InvalidDataException("Модуль не реализует контракт клиента.");
            instance =
                (IClientPlugin?)Activator.CreateInstance(entry)
                ?? throw new InvalidDataException("Не удалось создать модуль.");
            if (instance.Id != package.Id)
                throw new InvalidDataException("Идентификатор сборки не совпадает с пакетом.");
            return new LoadedClientPlugin(package, instance, context);
        }
        catch
        {
            try
            {
                if (instance is IAsyncDisposable asynchronous)
                    await asynchronous.DisposeAsync();
                else if (instance is IDisposable disposable)
                    disposable.Dispose();
            }
            finally
            {
                if (context.IsCollectible)
                    context.Unload();
            }
            throw;
        }
    }

    private sealed class ClientPluginLoadContext(string name)
        : AssemblyLoadContext(
            name,
            isCollectible: !OperatingSystem.IsBrowser() && !OperatingSystem.IsAndroid()
        )
    {
        protected override Assembly? Load(AssemblyName requested) =>
            Default.Assemblies.FirstOrDefault(assembly =>
                assembly.GetName().Name == requested.Name
            );
    }
}
