using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Q.Sock.Android;

[Application]
public sealed class AndroidApplication(nint handle, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<MobileApplication>(handle, transfer)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
