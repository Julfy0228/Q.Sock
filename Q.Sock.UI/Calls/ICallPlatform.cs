using Avalonia.Controls;
using Q.Sock.Core;
using Q.Sock.Shared.Models;

namespace Q.Sock.UI.Calls;

public interface ICallPlatform
{
    ICallSession Create(MessengerClient client, Action<string> notice);
}

public interface ICallSession : IAsyncDisposable
{
    Control View { get; }
    event Action<CallMediaState>? Changed;
    void ApplyTheme(MessengerTheme theme);
    Task Join(CallInfo call);
    Task ToggleMicrophone();
    Task ToggleCamera();
    Task ToggleScreen();
}

public sealed class CallMediaState
{
    public bool Connected { get; set; }
    public bool Microphone { get; set; }
    public bool Camera { get; set; }
    public bool Sharing { get; set; }
    public int Participants { get; set; }
}
