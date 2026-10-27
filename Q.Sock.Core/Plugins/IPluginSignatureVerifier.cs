namespace Q.Sock.Core.Plugins;

public interface IPluginSignatureVerifier
{
    Task Verify(PluginPackage package, byte[] bytes);
}

public sealed class ManagedPluginSignatureVerifier : IPluginSignatureVerifier
{
    public Task Verify(PluginPackage package, byte[] bytes)
    {
        package.Verify(bytes);
        return Task.CompletedTask;
    }
}
