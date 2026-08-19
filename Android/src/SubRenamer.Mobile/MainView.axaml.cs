using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SubRenamer.Mobile.Models;
using SubRenamer.Mobile.Presentation;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile;

public partial class MainView : UserControl
{
    private readonly SettingsStore _settings = new();
    private readonly ArchiveService _archives = new();
    private readonly StorageAccessService _storage;
    private readonly ScanService _scanner;
    private readonly AttributionService _attribution = new();
    private readonly PlanBuilder _planner = new(new SubRenamerCoreBridge());
    private readonly ApplyService _apply;

    private readonly ObservableCollection<SourceCard> _cards = [];
    private IReadOnlyList<VideoTarget> _targets = [];
    private Avalonia.Platform.Storage.IStorageFolder? _downloadRoot;
    private MatchPlan? _currentPlan;
    private bool _suppressCandidateChanged;

    public MainView()
    {
        InitializeComponent();
        _storage = new StorageAccessService(_settings);
        _scanner = new ScanService(_archives);
        _apply = new ApplyService(_archives);
        SourceList.ItemsSource = _cards;

        AttachedToVisualTree += async (_, _) =>
        {
            if (_downloadRoot is null)
                await InitializeAsync();
        };
    }

    private async Task InitializeAsync()
    {
        try
        {
            StatusText.Text = "正在检查已保存的 Download 授权…";
            _downloadRoot = await _storage.RestoreDownloadAsync(this);

            if (_downloadRoot is null)
            {
                RootText.Text = "尚未授权 Download";
                StatusText.Text = "首次使用请点“选择 Download”；以后授权会被保存。";
                ScanButton.IsEnabled = false;
                return;
            }

            // Do not read IStorageFolder.Name here. Android's SAF returns a tree URI
            // for the picked Download directory, and querying metadata on that raw
            // tree URI is rejected by some DocumentsProvider implementations.
            RootText.Text = "已授权：Download";
            StatusText.Text = "已恢复 Download 授权。点“扫描字幕与视频”开始。";
            ScanButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            RootText.Text = "尚未授权 Download";
            ScanButton.IsEnabled = false;
            StatusText.Text = $"恢复授权失败：{ex.Message}。请重新选择 Download。";
        }
    }

    private async Task ScanAsync()
    {
        var root = _downloadRoot;
        if (root is null)
        {
            StatusText.Text = "请先选择或恢复 Download 授权。";
            return;
        }

        SetBusy(true);
        _cards.Clear();
        _currentPlan = null;
        PreviewText.Text = "";
        ApplyButton.IsEnabled = false;

        try
        {
            StatusText.Text = "后台扫描 Torrent 视频目录与 Download 根目录字幕…";

            var result = await Task.Run(async () =>
            {
                var targets = await _scanner.FindVideoTargetsAsync(root).ConfigureAwait(false);
                var sources = await _scanner.FindSubtitleSourcesAsync(root).ConfigureAwait(false);
                return (Targets: targets, Sources: sources);
            });

            _targets = result.Targets;

            foreach (var source in result.Sources)
            {
                var ranked = _attribution.Rank(source, _targets);
                _cards.Add(new SourceCard
                {
                    Source = source,
                    Candidates = ranked.Candidates,
                    SelectedCandidate = ranked.AutoSelected,
                    State = ranked.AutoSelected is null ? "待确认归属" : "已自动归属",
                });
            }

            StatusText.Text =
                $"发现 {_targets.Count} 个视频目标目录，{result.Sources.Count} 个字幕来源。";

            if (_cards.Count > 0)
                SourceList.SelectedIndex = 0;
            else
                PreviewText.Text = "Download 根目录没有发现含字幕的 zip/7z/rar 或裸字幕文件。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"扫描失败：{ex.Message}";
            PreviewText.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task BuildPreviewAsync()
    {
        if (SourceList.SelectedItem is not SourceCard card)
            return;

        if (card.SelectedCandidate is null)
        {
            _currentPlan = null;
            PreviewText.Text = "无法可靠自动归属。请从上方列表点选正确的 Torrent 目录。";
            ApplyButton.IsEnabled = false;
            return;
        }

        SetBusy(true);
        try
        {
            card.State = "匹配中";
            StatusText.Text = $"调用原 SubRenamer.Core：{card.Source.DisplayName}";

            var source = card.Source;
            var target = card.SelectedCandidate.Target;
            _currentPlan = await Task.Run(() => _planner.BuildAsync(source, target));

            var lines = new List<string>
            {
                $"来源：{card.Source.DisplayName}",
                $"目标：{card.SelectedCandidate.Target.RelativePath}",
                $"可应用：{_currentPlan.ReadyCount}",
                $"冲突：{_currentPlan.ConflictCount}",
                "",
            };

            foreach (var item in _currentPlan.Items)
            {
                var icon = item.Status switch
                {
                    PlanItemStatus.Ready => "✓",
                    PlanItemStatus.ExistingDestination => "≠",
                    PlanItemStatus.Unmatched => "·",
                    _ => "!",
                };

                lines.Add(item.Status == PlanItemStatus.Ready
                    ? $"{icon} {item.SourceDisplayName}  →  {item.DestinationName}"
                    : $"{icon} {item.SourceDisplayName}  [{item.Status}] {item.Message}");
            }

            if (_currentPlan.Diagnostics.Count > 0)
            {
                lines.Add("");
                lines.AddRange(_currentPlan.Diagnostics.Select(x => "提示：" + x));
            }

            PreviewText.Text = string.Join(Environment.NewLine, lines);
            ApplyButton.Content = $"应用 {_currentPlan.ReadyCount} 项";
            ApplyButton.IsEnabled = _currentPlan.ReadyCount > 0;
            card.State = $"{_currentPlan.ReadyCount} 可应用";
            StatusText.Text = "预览已生成；视频文件不会被修改。";
        }
        catch (Exception ex)
        {
            _currentPlan = null;
            ApplyButton.IsEnabled = false;
            card.State = "匹配失败";
            PreviewText.Text = ex.ToString();
            StatusText.Text = "SubRenamer.Core 匹配失败；错误信息已显示在预览区。";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SourceList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SourceList.SelectedItem is not SourceCard card)
            return;

        _suppressCandidateChanged = true;
        CandidateCombo.ItemsSource = card.Candidates;
        CandidateCombo.SelectedItem = card.SelectedCandidate;
        _suppressCandidateChanged = false;

        await BuildPreviewAsync();
    }

    private async void CandidateCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressCandidateChanged)
            return;

        if (SourceList.SelectedItem is not SourceCard card)
            return;

        card.SelectedCandidate = CandidateCombo.SelectedItem as AttributionCandidate;
        card.State = card.SelectedCandidate is null ? "待确认归属" : "手动归属";
        await BuildPreviewAsync();
    }

    private async void Preview_Click(object? sender, RoutedEventArgs e) => await BuildPreviewAsync();

    private async void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPlan is null || _currentPlan.ReadyCount == 0)
            return;

        var plan = _currentPlan;
        SetBusy(true);
        ApplyButton.IsEnabled = false;

        try
        {
            StatusText.Text = "后台写入字幕；视频保持原位…";
            var result = await Task.Run(() => _apply.ApplyAsync(plan));

            var extra = result.Errors.Count == 0
                ? ""
                : Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Select(x => "失败：" + x));

            StatusText.Text = $"完成：{result.Applied} 成功，{result.Skipped} 跳过，{result.Errors.Count} 失败。";
            PreviewText.Text += Environment.NewLine + Environment.NewLine +
                                $"应用结果：{result.Applied} 成功 / {result.Skipped} 跳过 / {result.Errors.Count} 失败" +
                                extra;

            if (SourceList.SelectedItem is SourceCard card)
                card.State = $"已写入 {result.Applied}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"应用失败：{ex.Message}";
            PreviewText.Text += Environment.NewLine + ex;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Scan_Click(object? sender, RoutedEventArgs e) => await ScanAsync();

    private async void RestoreRoot_Click(object? sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            StatusText.Text = "恢复 Download 授权…";
            _downloadRoot = await _storage.RestoreDownloadAsync(this);
            if (_downloadRoot is null)
            {
                RootText.Text = "尚未授权 Download";
                StatusText.Text = "没有可用的已保存授权，请点“选择 Download”。";
                return;
            }

            RootText.Text = "已授权：Download";
            StatusText.Text = "Download 授权已恢复。点“扫描字幕与视频”开始。";
        }
        catch (Exception ex)
        {
            _downloadRoot = null;
            RootText.Text = "尚未授权 Download";
            StatusText.Text = $"恢复授权失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ChangeRoot_Click(object? sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            _downloadRoot = await _storage.PickDownloadAsync(this);
            if (_downloadRoot is null)
            {
                StatusText.Text = "未更改 Download 授权。";
                return;
            }

            RootText.Text = "已授权：Download";
            StatusText.Text = "授权已保存。点“扫描字幕与视频”开始；不会自动扫描。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"选择目录失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        RestoreRootButton.IsEnabled = !busy;
        ChangeRootButton.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy && _downloadRoot is not null;
        PreviewButton.IsEnabled = !busy;
        CandidateCombo.IsEnabled = !busy;
        SourceList.IsEnabled = !busy;
    }
}
