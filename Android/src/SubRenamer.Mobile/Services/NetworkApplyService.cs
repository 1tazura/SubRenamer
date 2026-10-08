using System.Diagnostics;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

internal sealed class NetworkApplyService(ArchiveService archives)
{
    public async Task<ApplyResult> ApplyAsync(MatchPlan plan, SmbStorageFolder folder, CancellationToken token)
    {
        if (plan.Target.RootIdentity != folder.RootIdentity)
            throw new IOException("预览目标与当前网络根身份不匹配。");
        var total = Stopwatch.StartNew();
        var snapshot = Stopwatch.StartNew();
        var reserved = await StorageAccessService.SnapshotChildFileNamesAsync(folder, token);
        snapshot.Stop();
        var created = new List<AppliedFileRecord>();
        var errors = new List<string>();
        var skipped = 0;
        var applied = 0;
        long bytes = 0;
        var sources = plan.Source.LooseFiles.ToDictionary(StorageAccessService.GetDisplayNameFast, StringComparer.Ordinal);
        ArchiveService.ArchiveSession? session = null;
        try
        {
            if (plan.Source.Kind == SubtitleSourceKind.Archive)
                session = await archives.OpenSessionAsync(plan.Source.ArchiveFile ?? throw new IOException("缺少字幕源压缩包。"), token);
            foreach (var item in plan.Items)
            {
                if (token.IsCancellationRequested) { errors.Add("处理已取消，已完成输出仍保留撤销记录。"); break; }
                if (item.Status != PlanItemStatus.Ready || !reserved.Add(item.DestinationName)) { skipped++; continue; }
                SmbHandleStream? destination = null;
                CopyFingerprint? fingerprint = null;
                string? fileIdentity = null;
                try
                {
                    // Compound FILE_CREATE + delete-pending, share_access=0: a
                    // preview race fails rather than overwriting/renaming a file.
                    destination = folder.CreateSubtitleExclusive(item.DestinationName);
                    if (session is not null)
                        fingerprint = await session.CopyEntryToAsync(item.SourceKey, destination, token);
                    else
                    {
                        if (!sources.TryGetValue(item.SourceKey, out var source)) throw new IOException("找不到裸字幕源。");
                        await using var input = await source.OpenReadAsync();
                        fingerprint = await StreamCopyService.CopyWithSha256Async(input, destination, token);
                    }
                    token.ThrowIfCancellationRequested();
                    destination.Flush();
                    // Capture after writing: some servers synthesize file birth
                    // timestamps from ctime. Never journal pre-transfer metadata.
                    fileIdentity = destination.FileIdentity;
                    destination.Commit();
                    created.Add(new AppliedFileRecord(item.DestinationName, fingerprint.Sha256, fileIdentity));
                    bytes += fingerprint.Length;
                    applied++;
                }
                catch (SmbException ex) when (ex.Code == -17) { skipped++; }
                catch (Exception ex)
                {
                    // A commit reply/close can be lost after the server retained
                    // the complete output. Preserve its hash for restart Undo.
                    if (destination?.CommitAttempted == true && fingerprint is not null)
                        created.Add(new AppliedFileRecord(item.DestinationName, fingerprint.Sha256, fileIdentity));
                    errors.Add($"{item.DestinationName}: {ex.Message}");
                    // Return a partial journal even on cancellation/disconnect.
                    // Do not throw away previously completed output records.
                    if (ex is OperationCanceledException || ex is SmbException { Code: -107 }) break;
                }
                finally
                {
                    if (destination is not null)
                    {
                        try { await destination.DisposeAsync(); }
                        catch (Exception ex) { errors.Add($"{item.DestinationName}: 关闭/回滚失败：{ex.Message}"); }
                    }
                }
            }
        }
        finally
        {
            if (session is not null)
            {
                try { await session.DisposeAsync(); }
                catch (Exception ex) { errors.Add("关闭字幕源失败：" + ex.Message); }
            }
        }
        total.Stop();
        return new ApplyResult(applied, skipped, errors, created,
            new ApplyPerformance(snapshot.Elapsed, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                TimeSpan.Zero, 1, TimeSpan.Zero, total.Elapsed, TimeSpan.Zero, total.Elapsed, bytes));
    }
}
