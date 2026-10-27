namespace Q.Sock.UI;

public interface IDisplaySettings
{
    bool AllowRotation { get; }
    Task SetAllowRotation(bool value);
}
