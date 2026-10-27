using System.Security.Cryptography;
using System.Text;

namespace Q.Sock.Core.Plugins;

public sealed class PluginPackage
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public const int CurrentApiVersion = 2;
    public int ApiVersion { get; set; } = CurrentApiVersion;
    public string Target { get; set; } = "";
    public string AssetUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string PublisherKey { get; set; } = "";
    public string Signature { get; set; } = "";
    public string Title { get; set; } = "";
    public string EntryType { get; set; } = "";
    public string Slot { get; set; } = "action";

    public byte[] SigningPayload() =>
        Encoding.UTF8.GetBytes(
            string.Join(
                "\n",
                Id,
                Version,
                ApiVersion,
                Target,
                AssetUrl,
                Sha256,
                Title,
                EntryType,
                Slot
            )
        );

    public string PublisherFingerprint()
    {
        var key = Convert.FromBase64String(PublisherKey);
        return Convert.ToHexString(SHA256.HashData(key));
    }

    public void ValidateAsset(ReadOnlySpan<byte> asset)
    {
        ValidateMetadata();
        if (asset.Length > 20 * 1024 * 1024)
            throw new InvalidDataException("Пакет несовместим с этой версией клиента.");
        var hash = Convert.ToHexString(SHA256.HashData(asset));
        if (!hash.Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Контрольная сумма пакета не совпадает.");
    }

    public void Verify(ReadOnlySpan<byte> asset)
    {
        ValidateAsset(asset);
        using var publisher = ECDsa.Create();
        publisher.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublisherKey), out _);
        if (
            !publisher.VerifyData(
                SigningPayload(),
                Convert.FromBase64String(Signature),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation
            )
        )
            throw new InvalidDataException("Подпись пакета недействительна.");
    }

    public void ValidateMetadata()
    {
        if (ApiVersion is < 1 or > CurrentApiVersion || Target is not ("avalonia" or "web" or "android"))
            throw new InvalidDataException("Несовместимая версия API или платформа модуля.");
        if (Id is null || !System.Text.RegularExpressions.Regex.IsMatch(Id, "^[a-z0-9-]{1,50}$"))
            throw new InvalidDataException("Некорректный идентификатор модуля.");
        if (
            Sha256 is null
            || !System.Text.RegularExpressions.Regex.IsMatch(Sha256, "^[a-fA-F0-9]{64}$")
        )
            throw new InvalidDataException("Некорректная контрольная сумма модуля.");
        if (
            Version is null
            || !System.Text.RegularExpressions.Regex.IsMatch(
                Version,
                "^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$"
            )
        )
            throw new InvalidDataException("Некорректная версия модуля.");
        if (
            Slot is not ("action" or "workspace" or "lesson-planning")
            || Title is null
            || Title.Length > 160
        )
            throw new InvalidDataException("Некорректное назначение модуля.");
        if (
            EntryType is null
            || EntryType.Length > 200
            || (
                EntryType.Length > 0
                && !System.Text.RegularExpressions.Regex.IsMatch(
                    EntryType,
                    "^[A-Za-z_][A-Za-z0-9_.+]*$"
                )
            )
        )
            throw new InvalidDataException("Некорректная точка входа модуля.");
        if (
            AssetUrl is null
            || !(
                AssetUrl.StartsWith("/modules/", StringComparison.Ordinal)
                || AssetUrl.StartsWith("/api/ui/plugins/assets/", StringComparison.Ordinal)
            )
            || AssetUrl.Contains("..")
            || AssetUrl.Contains('%')
            || AssetUrl.Contains('\\')
            || AssetUrl.Contains('?')
            || AssetUrl.Contains('#')
        )
            throw new InvalidDataException("Некорректный адрес пакета.");
    }
}
