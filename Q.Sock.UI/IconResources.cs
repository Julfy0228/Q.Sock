using System.IO.Compression;
using Avalonia.Platform;

namespace Q.Sock.UI;

internal static class IconResources
{
    public static Stream Open(string catalog) =>
        new GZipStream(
            AssetLoader.Open(new Uri($"avares://Q.Sock.UI/Resources/{catalog}-icons.json.gz")),
            CompressionMode.Decompress
        );
}
