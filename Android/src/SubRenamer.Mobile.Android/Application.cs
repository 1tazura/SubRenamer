using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using SubRenamer.Mobile;

[assembly: UsesPermission(global::Android.Manifest.Permission.Internet)]

namespace SubRenamer.Mobile.Android;

[Application]
public class AndroidApplication : AvaloniaAndroidApplication<App>
{
    protected AndroidApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder);
}
