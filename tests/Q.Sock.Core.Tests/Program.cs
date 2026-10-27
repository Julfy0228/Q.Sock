using System.Security.Cryptography;
using Q.Sock.Core;
using Q.Sock.Core.Plugins;
using Q.Sock.UI;

Check(
    AvatarColors.Palette.Count == 32 && AvatarColors.Palette.Distinct().Count() == 32,
    "Палитра должна содержать 32 разных цвета."
);
var usedColors = new HashSet<string>();
for (var index = 0; index < 256; index++)
{
    var id = Guid.Parse($"00000000-0000-0000-0000-{index:x12}");
    var userColor = AvatarColors.For(id, AvatarKind.User);
    Check(userColor == AvatarColors.For(id, AvatarKind.User), "Цвет должен сохраняться для ID.");
    Check(
        userColor != AvatarColors.For(id, AvatarKind.Chat),
        "Типы сущностей должны иметь разные цвета для одного ID."
    );
    Check(
        userColor != AvatarColors.For(id, AvatarKind.Space),
        "Пространство должно иметь отдельное смещение."
    );
    usedColors.Add(userColor);
}
Check(usedColors.Count == 32, "Последовательные ID должны распределяться по всей палитре.");
foreach (var hex in AvatarColors.Palette)
{
    var channels = new[] { 1, 3, 5 }
        .Select(offset => Convert.ToInt32(hex.Substring(offset, 2), 16) / 255d)
        .Select(channel =>
            channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4)
        )
        .ToArray();
    var luminance = 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
    Check(1.05 / (luminance + 0.05) >= 4.5, "Буквы на аватарках должны оставаться читаемыми.");
}

var theme = new MessengerTheme { Name = "Проверка QR" };
using (var image = new MemoryStream(theme.QrPng()))
{
    var imported = MessengerTheme.Parse(QrCodeReader.Decode(image));
    Check(
        imported.Name == theme.Name && imported.Colors.SequenceEqual(theme.Colors),
        "Экспорт и чтение QR должны сохранять тему."
    );
}
var incomplete = new MessengerTheme();
incomplete.Colors.Remove("panel");
ExpectInvalid(() => MessengerTheme.Parse(incomplete.Export()));
ExpectInvalid(() => MessengerTheme.Parse("{\"version\":1,\"name\":null,\"colors\":null}"));

var code =
    "public string Value = \"// not a comment\";\n// настоящий комментарий\nvar number = 42;";
var tokens = CodeSyntax.Tokenize(code, "csharp").ToArray();
Check(
    string.Concat(tokens.Select(token => token.Text)) == code,
    "Подсветка не должна менять исходный код."
);
Check(
    tokens.Any(token => token.Kind == "string" && token.Text.Contains("not a comment")),
    "Маркер комментария внутри строки должен остаться строкой."
);
Check(
    tokens.Any(token => token.Kind == "comment" && token.Text.Contains("настоящий")),
    "Комментарий должен распознаваться отдельно."
);
Check(
    MessengerClient.NormalizeServer("https://example.org").AbsoluteUri == "https://example.org/",
    "Нормализация сервера нарушена."
);
ExpectInvalid(() => MessengerClient.NormalizeServer("http://example.org"));
ExpectInvalid(() => MessengerClient.NormalizeServer("https://user:secret@example.org"));

using (var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256))
{
    var asset = "test data only"u8.ToArray();
    var package = new PluginPackage
    {
        Id = "moodle",
        Version = "1",
        Target = "avalonia",
        AssetUrl = "/modules/test.dll",
        Sha256 = Convert.ToHexString(SHA256.HashData(asset)),
        PublisherKey = Convert.ToBase64String(publisher.ExportSubjectPublicKeyInfo()),
    };
    package.Signature = Convert.ToBase64String(
        publisher.SignData(
            package.SigningPayload(),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        )
    );
    package.Verify(asset);
    asset[0] ^= 1;
    ExpectInvalid(() => package.Verify(asset));
    asset[0] ^= 1;
    package.Version = "2";
    ExpectInvalid(() => package.Verify(asset));
}

Console.WriteLine(
    "PASS: theme QR roundtrip, invalid themes, code preservation, server origin validation, package tamper detection."
);

var unavailable = new UnavailableAccountStorage();
var temporaryAccounts = new SessionFallbackAccountRepository(unavailable);
await temporaryAccounts.LoadAsync();
var unreadableAccounts = new SessionFallbackAccountRepository(
    new UnavailableAccountStorage { ReadFails = true }
);
Check(
    (await unreadableAccounts.LoadAsync()).Count == 0 && unreadableAccounts.SessionOnly,
    "Отказ чтения хранилища должен разрешать новый временный вход."
);
var temporary = new SavedAccount { UserName = "Fixture", AccessToken = "synthetic-token" };
await temporaryAccounts.SaveAsync([temporary]);
Check(
    temporaryAccounts.SessionOnly,
    "Отказ защищённого хранилища должен включать временный сеанс."
);
await temporaryAccounts.SaveAsync([temporary]);
Check(
    unavailable.Saves == 1,
    "Временный сеанс не должен повторно обращаться к недоступному хранилищу."
);
Check(
    (await temporaryAccounts.LoadAsync()).Single().AccessToken == temporary.AccessToken,
    "Временный сеанс потерял токен в памяти."
);
Check(
    (await new SessionFallbackAccountRepository(unavailable).LoadAsync()).Count == 0,
    "Временные токены не должны переживать новый экземпляр приложения."
);
Console.WriteLine(
    "PASS: secure-storage failure keeps a clearly marked session in memory without persistent tokens."
);

static void Check(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

static void ExpectInvalid(Action action)
{
    try
    {
        action();
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
    {
        return;
    }
    throw new InvalidOperationException("Недопустимые данные были приняты.");
}

sealed class UnavailableAccountStorage : IAccountRepository
{
    public bool ReadFails { get; init; }
    public int Saves { get; private set; }

    public Task<List<SavedAccount>> LoadAsync()
    {
        if (ReadFails)
            throw new AccountStorageException(
                "Fixture read unavailable",
                new InvalidOperationException()
            );
        return Task.FromResult(new List<SavedAccount>());
    }

    public Task<string> GetOrCreateDeviceIdAsync() => Task.FromResult(Guid.NewGuid().ToString());

    public Task SaveAsync(IEnumerable<SavedAccount> accounts)
    {
        Saves++;
        throw new AccountStorageException(
            "Fixture storage unavailable",
            new InvalidOperationException()
        );
    }
}
