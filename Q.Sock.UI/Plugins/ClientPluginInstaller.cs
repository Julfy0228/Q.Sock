using Q.Sock.Core;
using Q.Sock.Core.Plugins;

namespace Q.Sock.UI.Plugins;

public sealed class ClientPluginInstaller(
    IPluginCache cache,
    PublisherTrustStore trust,
    IPluginSignatureVerifier? signatures = null
)
{
    public async Task<LoadedClientPlugin?> Install(
        MessengerClient client,
        PluginPackage package,
        Func<PluginPackage, Task<bool>> confirm,
        CancellationToken cancellation = default
    )
    {
        package.ValidateMetadata();
        var serverKey = PluginCacheKeys.Server(client.Server);
        var key = PluginCacheKeys.Package(package);
        var bytes = await cache.Read(serverKey, key);
        if (bytes is not null)
        {
            try
            {
                await (signatures ?? new ManagedPluginSignatureVerifier()).Verify(package, bytes);
            }
            catch (Exception exception)
                when (exception
                        is InvalidDataException
                            or System.Security.Cryptography.CryptographicException
                            or FormatException
                )
            {
                bytes = null;
            }
        }
        bytes ??= await client.DownloadLimited(package.AssetUrl, 20 * 1024 * 1024, cancellation);
        cancellation.ThrowIfCancellationRequested();
        await (signatures ?? new ManagedPluginSignatureVerifier()).Verify(package, bytes);
        if (!await trust.IsTrusted(client.Server, package))
        {
            if (!await confirm(package))
                return null;
            await trust.Trust(client.Server, package);
        }
        cancellation.ThrowIfCancellationRequested();
        var plugin = await new TrustedPluginLoader(trust, signatures).Load(
            client.Server,
            package,
            bytes
        );
        try
        {
            await cache.Write(serverKey, key, bytes);
        }
        catch
        {
            await plugin.DisposeAsync();
            throw;
        }
        return plugin;
    }
}
