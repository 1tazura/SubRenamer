using System.Diagnostics;
using Avalonia.Interactivity;
using SubRenamer.Mobile.Presentation;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile;

public partial class MainView
{
    private async void ScanOptimized_Click(object? sender, RoutedEventArgs e)
        => await ScanOptimizedAsync();

    private async Task ScanOptimizedAsync()
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
            StatusText.Text = "单次枚举 Download 后，并行扫描 Torrent 子树与字幕来源…";
            var totalWatch = Stopwatch.StartNew();
            var result = await _scanner.ScanAllWithMetricsAsync(root);

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
                $"扫描性能：Download 根枚举 {result.SubtitleScan.RootEnumerationElapsed.TotalMilliseconds:F0} ms；" +
                $"Torrent 子树遍历 {result.VideoTraversalElapsed.TotalMilliseconds:F0} ms；" +
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

    private async void ApplyInstrumented_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPlan is null || _currentPlan.ReadyCount == 0)
            return;

        var plan = _currentPlan;
        SetBusy(true);
        ApplyButton.IsEnabled = false;
        var userWatch = Stopwatch.StartNew();

        try
        {
            StatusText.Text = "后台写入字幕并记录各处理阶段耗时；视频保持原位…";
            var result = await Task.Run(() => _apply.ApplyAsync(plan));

            var extra = result.Errors.Count == 0
                ? ""
                : Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Select(x => "失败：" + x));

            var undoWarning = "";
            var undoElapsed = TimeSpan.Zero;
            if (result.CreatedFiles.Count > 0)
            {
                var undoWatch = Stopwatch.StartNew();
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
                finally
                {
                    undoWatch.Stop();
                    undoElapsed = undoWatch.Elapsed;
                }
            }

            userWatch.Stop();
            var performanceText =
                result.Performance +
                $"；Undo 记录 {undoElapsed.TotalMilliseconds:F0} ms；点击到完成 {userWatch.Elapsed.TotalMilliseconds:F0} ms。";

            StatusText.Text =
                $"完成：{result.Applied} 成功，{result.Skipped} 跳过，{result.Errors.Count} 失败；" +
                $"点击到完成 {userWatch.Elapsed.TotalMilliseconds:F0} ms。";
            PreviewText.Text += Environment.NewLine + Environment.NewLine +
                                $"应用结果：{result.Applied} 成功 / {result.Skipped} 跳过 / {result.Errors.Count} 失败" +
                                extra + undoWarning +
                                Environment.NewLine + performanceText;

            if (SourceList.SelectedItem is SourceCard card)
                card.State = $"已写入 {result.Applied}";
        }
        catch (Exception ex)
        {
            userWatch.Stop();
            StatusText.Text = $"应用失败：{ex.Message}";
            PreviewText.Text += Environment.NewLine + ex;
        }
        finally
        {
            SetBusy(false);
            UpdateUndoButton();
        }
    }

    private async void UndoInstrumented_Click(object? sender, RoutedEventArgs e)
    {
        var root = _downloadRoot;
        var batch = _undoBatch;
        if (root is null || batch is null || batch.Files.Count == 0)
            return;

        UndoResult? result = null;
        var rebuildPreview = false;
        var userWatch = Stopwatch.StartNew();
        SetBusy(true);
        ApplyButton.IsEnabled = false;

        try
        {
            StatusText.Text = $"撤销上次处理：并行校验并删除 {batch.Files.Count} 个由本应用创建的字幕…";
            result = await Task.Run(() => _undo.UndoLastAsync(root, batch));

            await RefreshUndoBatchAsync();
            rebuildPreview = result.Deleted > 0 &&
                             _currentPlan?.Target.RelativePath == batch.TargetRelativePath;
        }
        catch (Exception ex)
        {
            userWatch.Stop();
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

        userWatch.Stop();
        var errorText = result.Errors.Count == 0
            ? ""
            : $"，{result.Errors.Count} 个错误";
        StatusText.Text =
            $"撤销完成：{result.Deleted} 已删除，{result.Missing} 已不存在，{result.Changed} 已修改而保留{errorText}；" +
            $"点击到完成 {userWatch.Elapsed.TotalMilliseconds:F0} ms。";

        PreviewText.Text += Environment.NewLine + Environment.NewLine +
                            result.Performance +
                            $"；点击到完成 {userWatch.Elapsed.TotalMilliseconds:F0} ms。";

        if (result.Errors.Count > 0)
        {
            PreviewText.Text += Environment.NewLine +
                                string.Join(Environment.NewLine, result.Errors.Select(x => "撤销失败：" + x));
        }
    }
}
