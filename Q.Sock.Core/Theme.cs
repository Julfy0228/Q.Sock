using System.Text.Json;
using System.Text.RegularExpressions;
using QRCoder;

namespace Q.Sock.Core;

public sealed class MessengerTheme
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Мессенджер";
    public Dictionary<string, string> Colors { get; set; } =
        new()
        {
            ["background"] = "#313338",
            ["panel"] = "#2B2D31",
            ["surface"] = "#383A40",
            ["text"] = "#F2F3F5",
            ["muted"] = "#949BA4",
            ["accent"] = "#007ACC",
        };

    public static MessengerTheme Parse(string json)
    {
        if (json.Length > 8192)
            throw new ArgumentException("Тема слишком большая.");
        var t =
            JsonSerializer.Deserialize<MessengerTheme>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? throw new ArgumentException("Тема пуста.");
        if (
            t.Version != 1
            || t.Name is null
            || t.Colors is null
            || t.Name.Length > 60
            || t.Colors.Count != 6
            || t.Colors.Keys.Any(x =>
                !new[] { "background", "panel", "surface", "text", "muted", "accent" }.Contains(x)
            )
            || t.Colors.Values.Any(x => x is null || !Regex.IsMatch(x, "^#[0-9a-fA-F]{6}$"))
        )
            throw new ArgumentException("Некорректный формат темы.");
        return t;
    }

    public string Export() =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public byte[] QrPng()
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(Export(), QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(6);
    }
}
