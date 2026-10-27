namespace Q.Sock.UI;

public static class AppIcons
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["plus"] = "Add",
        ["message-circle"] = "ChatBubble",
        ["message-circle-plus"] = "AddComment",
        ["paperclip"] = "AttachFile",
        ["ellipsis"] = "MoreHoriz",
        ["arrow-left"] = "ArrowBack",
        ["arrow-right"] = "ArrowForward",
        ["arrow-up"] = "ArrowUpward",
        ["arrow-down"] = "ArrowDownward",
    };

    public static MaterialIcon Create(string name, double size = 20) =>
        new(
            ResolveMaterial(name)
                ?? throw new ArgumentException("Неизвестная иконка.", nameof(name)),
            size
        );

    public static bool Exists(string name) => ResolveMaterial(name) is not null;

    private static string? ResolveMaterial(string name)
    {
        if (name.StartsWith("material/", StringComparison.Ordinal))
            return MaterialIcon.ResolveName(name[9..]);
        return MaterialIcon.ResolveName(Aliases.GetValueOrDefault(name, name));
    }
}
