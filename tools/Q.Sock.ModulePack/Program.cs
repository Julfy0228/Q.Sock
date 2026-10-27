using System.Security.Cryptography;
using System.Text.Json;
using Q.Sock.Core.Plugins;

if (args.Length != 8)
    throw new ArgumentException(
        "Ожидаются: DLL, каталог пакетов, ключ PEM, id, версия, название, тип точки входа, слот."
    );

var assembly = await File.ReadAllBytesAsync(args[0]);
var directory = Path.GetFullPath(args[1]);
var keyPath = Path.GetFullPath(args[2]);
using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
if (File.Exists(keyPath))
    publisher.ImportFromPem(await File.ReadAllTextAsync(keyPath));
else
{
    Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
    await File.WriteAllTextAsync(keyPath, publisher.ExportPkcs8PrivateKeyPem());
}

foreach (var target in new[] { "avalonia", "web", "android" })
{
    var hash = Convert.ToHexString(SHA256.HashData(assembly)).ToLowerInvariant();
    var package = new PluginPackage
    {
        Id = args[3],
        Version = args[4],
        Title = args[5],
        EntryType = args[6],
        Slot = args[7],
        Target = target,
        Sha256 = hash,
        AssetUrl = $"/api/ui/plugins/assets/{args[3]}/{target}/{hash}",
        PublisherKey = Convert.ToBase64String(publisher.ExportSubjectPublicKeyInfo()),
    };
    package.Signature = Convert.ToBase64String(
        publisher.SignData(
            package.SigningPayload(),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        )
    );
    package.Verify(assembly);
    var output = Path.Combine(directory, package.Id, target);
    Directory.CreateDirectory(output);
    await File.WriteAllBytesAsync(Path.Combine(output, "module.dll"), assembly);
    await File.WriteAllTextAsync(
        Path.Combine(output, "manifest.json"),
        JsonSerializer.Serialize(
            package,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        )
    );
    Console.WriteLine(
        $"{package.Id} {package.Version} {target} · {package.PublisherFingerprint()}"
    );
}
