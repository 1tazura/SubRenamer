using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace SubRenamer.Mobile.Android;

[Activity(
    Label = "SubRenamer",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
