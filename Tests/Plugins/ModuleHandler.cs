using System.Net;

namespace Q.Sock.Tests.Plugins;

internal sealed class ModuleHandler(byte[] bytes) : HttpMessageHandler
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
