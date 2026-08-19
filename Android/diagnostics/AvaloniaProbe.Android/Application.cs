using Android.App;
using Android.Runtime;
using Avalonia.Android;
using SubRenamer.Diagnostics.AvaloniaProbe;

namespace SubRenamer.Diagnostics.AvaloniaProbe.Android;

[Application]
public sealed class AndroidApplication : AvaloniaAndroidApplication<App>
{
    protected AndroidApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
