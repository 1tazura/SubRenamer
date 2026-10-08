using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SubRenamer.Mobile;

/// <summary>
/// Keeps the process alive if MainView construction fails so a startup error can
/// be shown on-device instead of presenting as an unexplained Android crash.
/// </summary>
public sealed class StartupView : UserControl
{
    private readonly TextBlock _status;
    private bool _loaded;

    public StartupView()
    {
        _status = new TextBlock
        {
            Text = "SubRenamer 正在启动…",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = _status,
        };

        AttachedToVisualTree += async (_, _) => await LoadMainViewAsync();
    }

    private async Task LoadMainViewAsync()
    {
        if (_loaded)
            return;

        _loaded = true;
        await Task.Yield();

        try
        {
            Content = new MainView();
        }
        catch (Exception ex)
        {
            _status.Text =
                "SubRenamer 主界面初始化失败。\n\n" +
                ex +
                "\n\n请把这一屏截图发给 ChatGPT；无需抓 logcat。";
        }
    }
}
