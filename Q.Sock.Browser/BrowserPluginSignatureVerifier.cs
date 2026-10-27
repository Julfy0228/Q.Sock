using System.Runtime.InteropServices.JavaScript;
using Q.Sock.Core.Plugins;

namespace Q.Sock.Browser;

public sealed partial class BrowserPluginSignatureVerifier : IPluginSignatureVerifier
{
    [JSImport("verifyPluginSignature", "qsock")]
    private static partial Task<bool> VerifySignature(
        string publisher,
        string payload,
        string signature
    );

    public async Task Verify(PluginPackage package, byte[] bytes)
    {
        package.ValidateAsset(bytes);
        if (
            !await VerifySignature(
                package.PublisherKey,
                Convert.ToBase64String(package.SigningPayload()),
                package.Signature
            )
        )
            throw new InvalidDataException("Подпись пакета недействительна.");
    }
}
