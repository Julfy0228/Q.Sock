using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Q.Sock.Core;

public enum AvatarKind
{
    User,
    Chat,
    Space,
}

public static class AvatarColors
{
    private static readonly string[] Colors =
    [
        "#943D45",
        "#993E35",
        "#914A2C",
        "#865629",
        "#796027",
        "#6A6729",
        "#556C30",
        "#3E713B",
        "#2D734B",
        "#25745C",
        "#237368",
        "#246F74",
        "#286979",
        "#2B637F",
        "#305C88",
        "#385492",
        "#414B96",
        "#4C4494",
        "#593E8D",
        "#683C84",
        "#773B79",
        "#853A6B",
        "#903C5B",
        "#963E50",
        "#70483E",
        "#68563D",
        "#4D6246",
        "#3C6260",
        "#405873",
        "#514B76",
        "#68466A",
        "#7B435C",
    ];

    public static IReadOnlyList<string> Palette { get; } = Array.AsReadOnly(Colors);

    public static string For(Guid id, AvatarKind kind) => Colors[Index(id, kind)];

    public static int Index(Guid id, AvatarKind kind)
    {
        Span<byte> identity = stackalloc byte[16];
        id.TryWriteBytes(identity, bigEndian: true, out _);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(identity, digest);
        var offset = kind switch
        {
            AvatarKind.User => 0,
            AvatarKind.Chat => 11,
            AvatarKind.Space => 23,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var mixed = BinaryPrimitives.ReadUInt32BigEndian(digest);
        return (int)((mixed + (ulong)offset) % (ulong)Colors.Length);
    }
}
