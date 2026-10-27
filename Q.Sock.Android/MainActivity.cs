using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia;
using Avalonia.Android;

namespace Q.Sock.Android;

[Activity(
    Label = "Q.Sock",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.AppCompat.DayNight.NoActionBar",
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
)]
public sealed class MainActivity : AvaloniaMainActivity
{
    internal static MainActivity? Active { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Active = this;
        base.OnCreate(savedInstanceState);
        ApplyOrientation(new AndroidDisplaySettings().AllowRotation);
    }

    internal void ApplyOrientation(bool allowRotation) =>
        RequestedOrientation = allowRotation
            ? ScreenOrientation.FullUser
            : ScreenOrientation.Portrait;

    protected override void OnDestroy()
    {
        if (Active == this)
            Active = null;
        base.OnDestroy();
    }
}
