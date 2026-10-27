namespace Q.Sock.Core;

public sealed record CodeToken(string Text, string Kind);

public static class CodeSyntax
{
    private static readonly HashSet<string> Keywords = new(
        (
            "abstract and as ascending async await base bool boolean break byte case catch char class const continue "
            + "decimal def default delegate delete descending do double else elif enum event except export extends false finally fixed "
            + "float for foreach from function global if implements import in instanceof int interface internal into is let lock long "
            + "namespace native new none not null object of operator or orderby out override package params pass private protected public "
            + "raise readonly record ref return sbyte sealed select set short sizeof stackalloc static string struct super switch synchronized "
            + "this throw throws true try typeof uint ulong unchecked union unsafe ushort using var virtual void volatile when where while with yield "
            + "add all alter asc begin between by cascade check column constraint create database desc distinct drop end exists foreign group having "
            + "index inner insert join key left limit on outer primary references right table top truncate unique update values view"
        ).Split(' ', StringSplitOptions.RemoveEmptyEntries),
        StringComparer.OrdinalIgnoreCase
    );

    public static IEnumerable<CodeToken> Tokenize(string source, string? language)
    {
        var normalized = language?.Trim().Split(' ')[0].ToLowerInvariant();
        if (
            normalized
            is not (
                "cs"
                or "csharp"
                or "js"
                or "javascript"
                or "ts"
                or "typescript"
                or "py"
                or "python"
                or "php"
                or "sql"
                or "json"
                or "java"
                or "cpp"
                or "c"
            )
        )
        {
            yield return new(source, "plain");
            yield break;
        }
        var index = 0;
        while (index < source.Length)
        {
            var start = index;
            var character = source[index];
            var kind = "plain";
            if (character is '\'' or '"' or '`')
            {
                var quote = character;
                index++;
                while (index < source.Length)
                {
                    var next = source[index++];
                    if (next == '\\' && index < source.Length)
                        index++;
                    else if (next == quote)
                        break;
                }
                kind = "string";
            }
            else if (
                source.AsSpan(index).StartsWith("//")
                || source.AsSpan(index).StartsWith("--") && normalized == "sql"
                || character == '#' && normalized is "py" or "python" or "php"
            )
            {
                var end = source.IndexOf('\n', index);
                index = end < 0 ? source.Length : end;
                kind = "comment";
            }
            else if (source.AsSpan(index).StartsWith("/*"))
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? source.Length : end + 2;
                kind = "comment";
            }
            else if (char.IsDigit(character))
            {
                while (
                    index < source.Length
                    && (char.IsLetterOrDigit(source[index]) || source[index] is '.' or '_')
                )
                    index++;
                kind = "number";
            }
            else if (char.IsLetter(character) || character == '_')
            {
                while (
                    index < source.Length
                    && (char.IsLetterOrDigit(source[index]) || source[index] == '_')
                )
                    index++;
                if (Keywords.Contains(source[start..index]))
                    kind = "keyword";
            }
            else
                index++;
            yield return new(source[start..index], kind);
        }
    }
}
