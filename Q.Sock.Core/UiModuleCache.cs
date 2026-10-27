using System.Security.Cryptography;
using System.Text.Json;
using Q.Sock.Shared.Models;

namespace Q.Sock.Core;

public sealed class UiModuleCache(string directory)
{
    public async Task<Dictionary<string, JsonDocument>> Update(
        MessengerClient client,
        CancellationToken ct = default
    )
    {
        var manifest = await client.PublicGet<UiManifest>("api/ui/manifest", ct);
        var origin = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(client.Server.AbsoluteUri))
        );
        var dir = Path.Combine(directory, origin);
        Directory.CreateDirectory(dir);
        var keep = new HashSet<string>();
        var result = new Dictionary<string, JsonDocument>();
        foreach (var m in manifest.Modules)
        {
            if (
                !System.Text.RegularExpressions.Regex.IsMatch(m.Id, "^[a-z0-9-]{1,50}$")
                || !System.Text.RegularExpressions.Regex.IsMatch(m.Sha256, "^[a-fA-F0-9]{64}$")
                || !m.AssetUrl.StartsWith("/modules/")
                || m.AssetUrl.Contains("..")
            )
                throw new InvalidDataException("Некорректный манифест.");
            var name = m.Id + "-" + m.Sha256 + ".json";
            keep.Add(name);
            var path = Path.Combine(dir, name);
            byte[] bytes;
            if (File.Exists(path))
                bytes = await File.ReadAllBytesAsync(path, ct);
            else
            {
                using var http = new HttpClient();
                bytes = await http.GetByteArrayAsync(new Uri(client.Server, m.AssetUrl), ct);
            }
            if (
                bytes.Length > 1024 * 1024
                || !Convert
                    .ToHexString(SHA256.HashData(bytes))
                    .Equals(m.Sha256, StringComparison.OrdinalIgnoreCase)
            )
                throw new InvalidDataException("Хеш модуля не совпадает.");
            await File.WriteAllBytesAsync(path, bytes, ct);
            result[m.Id] = JsonDocument.Parse(bytes);
        }
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
            if (!keep.Contains(Path.GetFileName(path)))
                File.Delete(path);
        return result;
    }
}
