namespace Q.Sock.UI;

public sealed record Choice(string Value, string Label)
{
    public override string ToString() => Label;
}
