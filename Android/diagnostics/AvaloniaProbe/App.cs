using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;

namespace SubRenamer.Diagnostics.AvaloniaProbe;

public sealed class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
            activityLifetime.MainViewFactory = CreateView;
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = CreateView();

        base.OnFrameworkInitializationCompleted();
    }

    private static Control CreateView() => new Border
    {
        Padding = new Thickness(24),
        Child = new TextBlock
        {
            Text = "AVALONIA PROBE OK\n\nNo XAML, no SubRenamer.Core, no storage code.\nIf you can read this screen, Avalonia 12 bootstrap itself works on this device.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };
}
