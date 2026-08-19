using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace SubRenamer.Diagnostics.AvaloniaProbe.Android;

[Activity(
    Label = "SubRenamer Avalonia probe",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
