using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly UndoService _undo;

    private readonly ObservableCollection<SourceCard> _cards = [];
    private IReadOnlyList<VideoTarget> _targets = [];
    private Avalonia.Platform.Storage.IStorageFolder? _downloadRoot;
    private MatchPlan? _currentPlan;
    private UndoBatchRecord? _undoBatch;
    private bool _suppressCandidateChanged;
    private bool _suppressMatchModeChanged = true;
    private bool _busy;
    private string _lastScanPerformanceText = "";

    private CoreMatchMode _matchMode = CoreMatchMode.Diff;
    private string _manualVideoPattern = "";
    private string _manualSubtitlePattern = "";
    private string _videoRegex = "";
    private string _subtitleRegex = "";

    public MainView()
    {
        InitializeComponent();
        _storage = new StorageAccessService(_settings);
        _scanner = new ScanService(_archives);
        _apply = new ApplyService(_archives);
        _undo = new UndoService(_settings);
        SourceList.ItemsSource = _cards;

        _suppressMatchModeChanged = false;
        UpdateMatchModeUi();

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
            ApplyLoadedMatchSettings(await _settings.LoadAsync());
            _downloadRoot = await _storage.RestoreDownloadAsync(this);

            if (_downloadRoot is null)
            {
                RootText.Text = "尚未授权 Download";
                StatusText.Text = "首次使用请点“选择 Download”；以后授权会被保存。";
                ScanButton.IsEnabled = false;
                UpdateUndoButton();
                return;
            }

            RootText.Text = "已授权：Download";
            StatusText.Text = "已恢复 Download 授权。点“扫描字幕与视频”开始。";
            ScanButton.IsEnabled = true;
            await RefreshUndoBatchAsync();
        }
        catch (Exception ex)
        {
            RootText.Text = "尚未授权 Download";
            ScanButton.IsEnabled = false;
            StatusText.Text = $"恢复授权失败：{ex.Message}。请重新选择 Download。";
            UpdateUndoButton();
        }
    }

    private void ApplyLoadedMatchSettings(AppSettings settings)
    {
        _matchMode = Enum.IsDefined(settings.MatchMode) ? settings.MatchMode : CoreMatchMode.Diff;
        _manualVideoPattern = settings.ManualVideoPattern ?? "";
        _manualSubtitlePattern = settings.ManualSubtitlePattern ?? "";
        _videoRegex = settings.VideoRegex ?? "";
        _subtitleRegex = settings.SubtitleRegex ?? "";

        _suppressMatchModeChanged = true;
        MatchModeCombo.SelectedIndex = (int)_matchMode;
        _suppressMatchModeChanged = false;
        UpdateMatchModeUi();
    }

    private void CaptureActiveRulesFromUi()
    {
        switch (_matchMode)
        {
            case CoreMatchMode.Manual:
                _manualVideoPattern = VideoRuleTextBox.Text ?? "";
                _manualSubtitlePattern = SubtitleRuleTextBox.Text ?? "";
                break;
            case CoreMatchMode.Regex:
                _videoRegex = VideoRuleTextBox.Text ?? "";
                _subtitleRegex = SubtitleRuleTextBox.Text ?? "";
                break;
        }
    }

    private void UpdateMatchModeUi()
    {
        switch (_matchMode)
        {
            case CoreMatchMode.Diff:
                MatchRulePanel.IsVisible = false;
                MatchRuleHint.Text = "自动模式使用原 SubRenamer.Core 的 diff → extract → mapping。";
                break;
            case CoreMatchMode.Manual:
                MatchRulePanel.IsVisible = true;
                VideoRuleLabel.Text = "视频手动规则";
                SubtitleRuleLabel.Text = "字幕手动规则";
                VideoRuleTextBox.Text = _manualVideoPattern;
                SubtitleRuleTextBox.Text = _manualSubtitlePattern;
                MatchRuleHint.Text = "与桌面版手动规则一致：$$ 表示集数/匹配键，* 表示任意文本。例如：Show - $$ *.mkv";
                break;
            case CoreMatchMode.Regex:
                MatchRulePanel.IsVisible = true;
                VideoRuleLabel.Text = "视频正则";
                SubtitleRuleLabel.Text = "字幕正则";
                VideoRuleTextBox.Text = _videoRegex;
                SubtitleRuleTextBox.Text = _subtitleRegex;
                MatchRuleHint.Text = "SubRenamer.Core 使用捕获组 1 作为匹配键。示例：S02E(\\d+)。";
                break;
        }
    }

    private CoreMatchSettings GetActiveMatchSettings()
    {
        CaptureActiveRulesFromUi();
        return _matchMode switch
        {
            CoreMatchMode.Diff => new CoreMatchSettings(CoreMatchMode.Diff),
            CoreMatchMode.Manual => new CoreMatchSettings(
                CoreMatchMode.Manual, _manualVideoPattern, _manualSubtitlePattern),
            CoreMatchMode.Regex => new CoreMatchSettings(
                CoreMatchMode.Regex, _videoRegex, _subtitleRegex),
            _ => new CoreMatchSettings(CoreMatchMode.Diff),
        };
    }

    private async Task PersistMatchSettingsAsync()
    {
        CaptureActiveRulesFromUi();
        try
        {
            await _settings.SaveMatchSettingsAsync(
                _matchMode,
                _manualVideoPattern,
                _manualSubtitlePattern,
                _videoRegex,
                _subtitleRegex);
        }
        catch
        {
            // Matching must remain usable even if app-private preference storage
            // temporarily fails. The active in-memory rules are still used.
        }
    }

    private static string MatchModeText(CoreMatchMode mode) => mode switch
    {
        CoreMatchMode.Diff => "自动 (Diff)",
        CoreMatchMode.Manual => "手动规则",
        CoreMatchMode.Regex => "正则表达式",
        _ => mode.ToString(),
    };

    private async Task RefreshUndoBatchAsync()
    {
        var settings = await _settings.LoadAsync();
        _undoBatch = settings.LastUndoBatch is { Files.Count: > 0 }
            ? settings.LastUndoBatch
            : null;
        UpdateUndoButton();
    }

    private void UpdateUndoButton()
    {
        var count = _undoBatch?.Files.Count ?? 0;
        UndoButton.Content = count > 0 ? $"撤销上次处理 ({count} 项)" : "撤销上次处理";
        UndoButton.IsEnabled = !_busy && _downloadRoot is not null && count > 0;
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
        _lastScanPerformanceText = "";
        PreviewText.Text = "";
        ApplyButton.Content = "确认处理";
        ApplyButton.IsEnabled = false;

        try
        {
            StatusText.Text = "后台并行扫描 Torrent 视频目录与 Download 字幕来源…";
            var totalWatch = Stopwatch.StartNew();

            var result = await Task.Run(async () =>
            {
                var videoWatch = Stopwatch.StartNew();
                var targetsTask = _scanner.FindVideoTargetsAsync(root);
                var measuredTargetsTask = targetsTask.ContinueWith(task =>
                {
                    videoWatch.Stop();
                    return task.GetAwaiter().GetResult();
                }, TaskScheduler.Default);

                var sourceTask = _scanner.FindSubtitleSourcesWithMetricsAsync(root);

                await Task.WhenAll(measuredTargetsTask, sourceTask).ConfigureAwait(false);
                return (
                    Targets: measuredTargetsTask.Result,
                    VideoElapsed: videoWatch.Elapsed,
                    SubtitleScan: sourceTask.Result);
            });

            _targets = result.Targets;

            var attributionWatch = Stopwatch.StartNew();
            foreach (var source in result.SubtitleScan.Sources)
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
            attributionWatch.Stop();
            totalWatch.Stop();

            _lastScanPerformanceText =
                $"扫描性能：Torrent 遍历 {result.VideoElapsed.TotalMilliseconds:F0} ms；" +
                $"Download 根枚举 {result.SubtitleScan.RootEnumerationElapsed.TotalMilliseconds:F0} ms；" +
                $"压缩包索引 {result.SubtitleScan.ArchiveIndexElapsed.TotalMilliseconds:F0} ms " +
                $"({result.SubtitleScan.ArchiveCount} 包)；" +
                $"来源整理 {result.SubtitleScan.FinalizeElapsed.TotalMilliseconds:F0} ms；" +
                $"作品归属 {attributionWatch.Elapsed.TotalMilliseconds:F0} ms；" +
                $"总计 {totalWatch.Elapsed.TotalMilliseconds:F0} ms。";

            StatusText.Text =
                $"发现 {_targets.Count} 个视频目标目录，{result.SubtitleScan.Sources.Count} 个字幕来源。{_lastScanPerformanceText}";

            if (_cards.Count > 0)
                SourceList.SelectedIndex = 0;
            else
                PreviewText.Text = "Download 根目录没有发现含字幕的 zip/7z/rar 或裸字幕文件。" +
                                   Environment.NewLine + Environment.NewLine + _lastScanPerformanceText;
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
            PreviewText.Text = "无法可靠自动归属。请从上方列表点选正确的 Torrent 目录。" +
                               (string.IsNullOrWhiteSpace(_lastScanPerformanceText)
                                   ? ""
                                   : Environment.NewLine + Environment.NewLine + _lastScanPerformanceText);
            ApplyButton.Content = "确认处理";
            ApplyButton.IsEnabled = false;
            return;
        }

        var matchSettings = GetActiveMatchSettings();
        await PersistMatchSettingsAsync();

        SetBusy(true);
        try
        {
            card.State = "匹配中";
            StatusText.Text = $"调用原 SubRenamer.Core · {MatchModeText(matchSettings.Mode)}：{card.Source.DisplayName}";

            var source = card.Source;
            var target = card.SelectedCandidate.Target;
            _currentPlan = await Task.Run(() => _planner.BuildAsync(source, target, matchSettings));

            var lines = new List<string>
            {
                $"来源：{card.Source.DisplayName}",
                $"目标：{card.SelectedCandidate.Target.RelativePath}",
                $"匹配模式：{MatchModeText(matchSettings.Mode)}",
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

            if (!string.IsNullOrWhiteSpace(_lastScanPerformanceText))
            {
                lines.Add("");
                lines.Add(_lastScanPerformanceText);
            }

            if (_currentPlan.Diagnostics.Count > 0)
            {
                lines.Add("");
                lines.AddRange(_currentPlan.Diagnostics.Select(x => "提示：" + x));
            }

            PreviewText.Text = string.Join(Environment.NewLine, lines);
            ApplyButton.Content = $"确认处理 {_currentPlan.ReadyCount} 项";
            ApplyButton.IsEnabled = _currentPlan.ReadyCount > 0;
            card.State = $"{_currentPlan.ReadyCount} 可应用";
            StatusText.Text = $"{MatchModeText(matchSettings.Mode)} 预览已生成；确认后只写入字幕，视频文件不会被修改。";
        }
        catch (Exception ex)
        {
            _currentPlan = null;
            ApplyButton.Content = "确认处理";
            ApplyButton.IsEnabled = false;
            card.State = "匹配失败";
            PreviewText.Text = ex.ToString();
            StatusText.Text = "SubRenamer.Core 匹配失败；请检查当前匹配模式/规则。";
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

    private async void MatchModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressMatchModeChanged)
            return;

        CaptureActiveRulesFromUi();
        _matchMode = MatchModeCombo.SelectedIndex switch
        {
            1 => CoreMatchMode.Manual,
            2 => CoreMatchMode.Regex,
            _ => CoreMatchMode.Diff,
        };
        UpdateMatchModeUi();
        await PersistMatchSettingsAsync();

        _currentPlan = null;
        ApplyButton.Content = "确认处理";
        ApplyButton.IsEnabled = false;
        PreviewText.Text = $"已切换为 {MatchModeText(_matchMode)}。填写需要的规则后点“重新生成预览”。" +
                           (string.IsNullOrWhiteSpace(_lastScanPerformanceText)
                               ? ""
                               : Environment.NewLine + Environment.NewLine + _lastScanPerformanceText);
        StatusText.Text = $"集数匹配模式：{MatchModeText(_matchMode)}。";
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

            var undoWarning = "";
            if (result.CreatedFiles.Count > 0)
            {
                try
                {
                    var batch = new UndoBatchRecord(
                        plan.Target.RelativePath,
                        DateTimeOffset.UtcNow,
                        result.CreatedFiles
                            .Select(x => new UndoFileRecord(x.DestinationName, x.Sha256))
                            .ToArray());

                    await _settings.SaveUndoBatchAsync(batch);
                    _undoBatch = batch;
                }
                catch (Exception ex)
                {
                    _undoBatch = null;
                    undoWarning = Environment.NewLine + $"警告：撤销记录保存失败：{ex.Message}";
                }
            }

            StatusText.Text = $"完成：{result.Applied} 成功，{result.Skipped} 跳过，{result.Errors.Count} 失败。";
            PreviewText.Text += Environment.NewLine + Environment.NewLine +
                                $"应用结果：{result.Applied} 成功 / {result.Skipped} 跳过 / {result.Errors.Count} 失败" +
                                extra + undoWarning;

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
            UpdateUndoButton();
        }
    }

    private async void Undo_Click(object? sender, RoutedEventArgs e)
    {
        var root = _downloadRoot;
        var batch = _undoBatch;
        if (root is null || batch is null || batch.Files.Count == 0)
            return;

        UndoResult? result = null;
        var rebuildPreview = false;
        SetBusy(true);
        ApplyButton.IsEnabled = false;

        try
        {
            StatusText.Text = $"撤销上次处理：检查并删除 {batch.Files.Count} 个由本应用创建的字幕…";
            result = await Task.Run(() => _undo.UndoLastAsync(root, batch));

            await RefreshUndoBatchAsync();
            rebuildPreview = result.Deleted > 0 &&
                             _currentPlan?.Target.RelativePath == batch.TargetRelativePath;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"撤销失败：{ex.Message}";
            PreviewText.Text += Environment.NewLine + ex;
        }
        finally
        {
            SetBusy(false);
            UpdateUndoButton();
        }

        if (result is null)
            return;

        if (rebuildPreview)
            await BuildPreviewAsync();

        var errorText = result.Errors.Count == 0
            ? ""
            : $"，{result.Errors.Count} 个错误";
        StatusText.Text =
            $"撤销完成：{result.Deleted} 已删除，{result.Missing} 已不存在，{result.Changed} 已修改而保留{errorText}。";

        if (result.Errors.Count > 0)
        {
            PreviewText.Text += Environment.NewLine + Environment.NewLine +
                                string.Join(Environment.NewLine, result.Errors.Select(x => "撤销失败：" + x));
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
            await RefreshUndoBatchAsync();
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
            UpdateUndoButton();
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

            _undoBatch = null;
            _lastScanPerformanceText = "";
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
            UpdateUndoButton();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RestoreRootButton.IsEnabled = !busy;
        ChangeRootButton.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy && _downloadRoot is not null;
        PreviewButton.IsEnabled = !busy;
        CandidateCombo.IsEnabled = !busy;
        SourceList.IsEnabled = !busy;
        MatchModeCombo.IsEnabled = !busy;
        VideoRuleTextBox.IsEnabled = !busy;
        SubtitleRuleTextBox.IsEnabled = !busy;
        UpdateUndoButton();
    }
}
