using Android.Content;
using Q.Sock.UI;

namespace Q.Sock.Android;

public sealed class AndroidDisplaySettings : IDisplaySettings
{
    private readonly ISharedPreferences preferences =
        global::Android.App.Application.Context.GetSharedPreferences(
            "display",
            FileCreationMode.Private
        )!;

    public bool AllowRotation =>
        preferences.GetBoolean(
            "allow-rotation",
            global::Android.App.Application.Context.Resources?.Configuration?.SmallestScreenWidthDp
                >= 600
        );

    public Task SetAllowRotation(bool value)
    {
        preferences.Edit()!.PutBoolean("allow-rotation", value)!.Apply();
        MainActivity.Active?.ApplyOrientation(value);
        return Task.CompletedTask;
    }
}
