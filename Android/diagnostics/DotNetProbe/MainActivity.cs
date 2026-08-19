using Android.App;
using Android.OS;
using Android.Widget;

namespace SubRenamer.Diagnostics.DotNetProbe;

[Activity(Label = "SubRenamer .NET probe", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var text = new TextView(this)
        {
            Text = $"DOTNET PROBE OK\n\nAndroid {Build.VERSION.Release}\nSDK {(int)Build.VERSION.SdkInt}\nABI {Build.SupportedAbis?.FirstOrDefault() ?? "unknown"}\n\nIf you can read this screen, the packaged .NET 10 Android runtime starts correctly on this device.",
            TextSize = 18,
        };
        text.SetPadding(32, 32, 32, 32);
        SetContentView(text);
    }
}
