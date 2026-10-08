using Avalonia.Controls;
using Avalonia.Interactivity;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile;

public partial class MainView
{
    private SmbConnection? _network;
    private IContentFolder? _videoRoot;
    private StorageRootIdentity? _savedVideoRoot;

    private async Task RestoreNetworkVideoRootAsync(AppSettings settings)
    {
        _savedVideoRoot = settings.VideoRoot;
        if (_savedVideoRoot is null) return;
        NetworkUrlTextBox.Text = _savedVideoRoot.RootUri;
        try
        {
            _network = await SmbConnection.ConnectAsync(_savedVideoRoot.RootUri, expectedIdentity: _savedVideoRoot);
            _videoRoot = _network.Root;
            VideoRootText.Text = "视频目标：" + _savedVideoRoot.RootUri;
        }
        catch (Exception ex)
        {
            NetworkPanel.IsVisible = true;
            VideoRootText.Text = "网络根尚未恢复：" + ex.Message + "；请重新输入凭据并连接。";
        }
    }

    private void InvalidateStoragePlan()
    {
        _currentPlan = null;
        _targets = [];
        _cards.Clear();
        CandidateCombo.ItemsSource = null;
        ApplyButton.IsEnabled = false;
        ApplyButton.Content = "确认处理";
        PreviewText.Text = "存储根已变化，请重新扫描与预览。";
    }

    private void NetworkRoot_Click(object? sender, RoutedEventArgs e)
        => NetworkPanel.IsVisible = !NetworkPanel.IsVisible;

    private async void ConnectNetwork_Click(object? sender, RoutedEventArgs e)
    {
        SetBusy(true);
        SmbConnection? candidate = null;
        try
        {
            var location = SmbLocation.Parse(NetworkUrlTextBox.Text ?? "");
            var user = NetworkUserTextBox.Text ?? "";
            var password = NetworkPasswordTextBox.Text;
            NetworkPasswordTextBox.Text = "";
            StatusText.Text = "连接网络视频根并核验身份（仅读目录，不创建文件）…";
            candidate = await SmbConnection.ConnectAsync(location.Url, user, password,
                _savedVideoRoot?.RootUri == location.Url ? _savedVideoRoot : null);
            // Prove this root can be listed before adopting/persisting it.
            var folders = new List<string>();
            await foreach (var item in candidate.Root.GetItemsAsync())
                if (item is IContentFolder) folders.Add(item.Name);
            await _settings.SaveVideoRootAsync(candidate.RootIdentity);
            InvalidateStoragePlan();
            _network?.Dispose();
            _network = candidate;
            candidate = null;
            _videoRoot = _network.Root;
            _savedVideoRoot = _network.RootIdentity;
            NetworkUrlTextBox.Text = _savedVideoRoot.RootUri;
            VideoRootText.Text = "视频目标：" + _savedVideoRoot.RootUri;
            NetworkDirectoryCombo.ItemsSource = folders.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            await RefreshUndoBatchAsync();
            StatusText.Text = "网络视频根已选定。可选择下级目录，或直接扫描；连接成功不代表账号有写权限。";
        }
        catch (Exception ex) { StatusText.Text = "网络连接失败：" + ex.Message; }
        finally { candidate?.Dispose(); SetBusy(false); }
    }

    private void NetworkDirectory_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NetworkDirectoryCombo.SelectedItem is not string name || _savedVideoRoot is null) return;
        NetworkUrlTextBox.Text = _savedVideoRoot.RootUri.TrimEnd('/') + "/" + Uri.EscapeDataString(name);
        NetworkDirectoryCombo.SelectedItem = null;
        StatusText.Text = "已填入子目录地址，点“连接并选择此视频根目录”确认切换。";
    }

    private async void LocalVideo_Click(object? sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            await _settings.SaveVideoRootAsync(null);
            InvalidateStoragePlan();
            _network?.Dispose();
            _network = null;
            _videoRoot = null;
            _savedVideoRoot = null;
            VideoRootText.Text = "视频目标：Download/Torrent（本地）";
            NetworkDirectoryCombo.ItemsSource = null;
            await RefreshUndoBatchAsync();
            StatusText.Text = "已恢复本地 Torrent 工作流。请重新扫描。";
        }
        catch (Exception ex) { StatusText.Text = "切换失败：" + ex.Message; }
        finally { SetBusy(false); }
    }
}
