using System.Runtime.InteropServices.JavaScript;
using Q.Sock.UI;

namespace Q.Sock.Browser;

public sealed partial class BrowserQrScanner : IQrScanner
{
    [JSImport("scanQr", "qsock")]
    private static partial Task<string> ScanQr();

    public async Task<string?> Scan()
    {
        var value = await ScanQr();
        return value.Length == 0 ? null : value;
    }
}
