using System.Diagnostics;
using Avalonia.Platform.Storage;

namespace SubRenamer.Mobile.Services;

public sealed record UndoResult(
    int Deleted,
    int Missing,
    int Changed,
    IReadOnlyList<string> Errors,
    UndoPerformance Performance);

public sealed class UndoService(SettingsStore settingsStore)
{
    // Undo is dominated by DocumentsProvider open/delete latency, not SHA work.
    // Eight independent verify-then-delete chains keep the provider busy while
    // remaining bounded; each individual file still preserves the strict
    // open -> full SHA-256 -> close -> delete ordering.
    private const int MaxUndoConcurrency = 8;

    public async Task<UndoResult> UndoLastAsync(
        IStorageFolder downloadRoot,
        UndoBatchRecord batch,
        CancellationToken cancellationToken = default,
        IStorageFolder? resolvedTarget = null)
    {
        var totalWatch = Stopwatch.StartNew();
        var concurrency = Math.Min(MaxUndoConcurrency, Math.Max(1, batch.Files.Count));

        // Same-session undo can reuse the exact target folder handle kept by the
        // current plan. Persisted undo after app restart has no live handle and
        // safely falls back to resolving the stored relative path from Download.
        var resolveWatch = Stopwatch.StartNew();
        var target = resolvedTarget;
        if (target is null)
        {
            target = await StorageAccessService.ResolveRelativeFolderAsync(
                downloadRoot, batch.TargetRelativePath, cancellationToken);
        }
        resolveWatch.Stop();

        if (target is null)
        {
            totalWatch.Stop();
            return new UndoResult(
                0,
                0,
                0,
                [$"找不到上次处理的目标目录：{batch.TargetRelativePath}"],
                new UndoPerformance(
                    resolveWatch.Elapsed,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    totalWatch.Elapsed,
                    0,
                    concurrency));
        }

        // One SAF enumeration for the whole batch. Individual files are then
        // addressed from this snapshot rather than searched one by one.
        var snapshotWatch = Stopwatch.StartNew();
        var files = await StorageAccessService.SnapshotChildFilesAsync(target, cancellationToken);
        snapshotWatch.Stop();

        using var gate = new SemaphoreSlim(concurrency);
        var tasks = batch.Files.Select(async record =>
        {
            if (!files.TryGetValue(record.DestinationName, out var file))
                return UndoFileResult.Missing(record.DestinationName);

            await gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var openElapsed = TimeSpan.Zero;
                var hashElapsed = TimeSpan.Zero;
                var deleteElapsed = TimeSpan.Zero;
                long bytesHashed = 0;

                try
                {
                    CopyFingerprint fingerprint;

                    // Close the read stream immediately after hashing, before
                    // DeleteAsync. This keeps the verify->delete race window no
                    // wider than the old serial implementation and avoids asking
                    // DocumentsProvider to delete a document with our read handle
                    // still open.
                    var openWatch = Stopwatch.StartNew();
                    var stream = await file.OpenReadAsync();
                    openWatch.Stop();
                    openElapsed = openWatch.Elapsed;

                    await using (stream)
                    {
                        var hashWatch = Stopwatch.StartNew();
                        fingerprint = await StreamCopyService.ComputeSha256Async(
                            stream, cancellationToken);
                        hashWatch.Stop();
                        hashElapsed = hashWatch.Elapsed;
                    }

                    bytesHashed = fingerprint.Length;

                    // Never delete a file that has been edited or replaced since
                    // this app created it. Persisted undo therefore remains safe
                    // across restarts; metadata is never substituted for SHA-256.
                    if (!string.Equals(
                            fingerprint.Sha256,
                            record.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return UndoFileResult.Changed(
                            record.DestinationName,
                            openElapsed,
                            hashElapsed,
                            bytesHashed);
                    }

                    var deleteWatch = Stopwatch.StartNew();
                    await file.DeleteAsync();
                    deleteWatch.Stop();
                    deleteElapsed = deleteWatch.Elapsed;

                    return UndoFileResult.Deleted(
                        record.DestinationName,
                        openElapsed,
                        hashElapsed,
                        deleteElapsed,
                        bytesHashed);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return UndoFileResult.Failed(
                        record.DestinationName,
                        ex.Message,
                        openElapsed,
                        hashElapsed,
                        deleteElapsed,
                        bytesHashed);
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        var deleted = results.Count(x => x.State == UndoFileState.Deleted);
        var missing = results.Count(x => x.State == UndoFileState.Missing);
        var changed = results.Count(x => x.State == UndoFileState.Changed);
        var errors = results
            .Where(x => x.State == UndoFileState.Error)
            .Select(x => $"{x.Name}: {x.Error}")
            .ToArray();

        var openElapsed = results.Aggregate(TimeSpan.Zero, (sum, x) => sum + x.OpenElapsed);
        var hashElapsed = results.Aggregate(TimeSpan.Zero, (sum, x) => sum + x.HashElapsed);
        var deleteElapsed = results.Aggregate(TimeSpan.Zero, (sum, x) => sum + x.DeleteElapsed);
        var bytesHashed = results.Sum(x => x.BytesHashed);

        // A successful undo attempt consumes the journal. Files that were changed
        // are deliberately kept and are reported rather than retried later.
        var journalWatch = Stopwatch.StartNew();
        if (errors.Length == 0)
            await settingsStore.ClearUndoBatchAsync(cancellationToken);
        journalWatch.Stop();

        totalWatch.Stop();
        return new UndoResult(
            deleted,
            missing,
            changed,
            errors,
            new UndoPerformance(
                resolveWatch.Elapsed,
                snapshotWatch.Elapsed,
                openElapsed,
                hashElapsed,
                deleteElapsed,
                journalWatch.Elapsed,
                totalWatch.Elapsed,
                bytesHashed,
                concurrency));
    }

    private enum UndoFileState
    {
        Deleted,
        Missing,
        Changed,
        Error,
    }

    private sealed record UndoFileResult(
        string Name,
        UndoFileState State,
        string? Error,
        TimeSpan OpenElapsed,
        TimeSpan HashElapsed,
        TimeSpan DeleteElapsed,
        long BytesHashed)
    {
        public static UndoFileResult Deleted(
            string name,
            TimeSpan open,
            TimeSpan hash,
            TimeSpan delete,
            long bytes)
            => new(name, UndoFileState.Deleted, null, open, hash, delete, bytes);

        public static UndoFileResult Missing(string name)
            => new(name, UndoFileState.Missing, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0);

        public static UndoFileResult Changed(
            string name,
            TimeSpan open,
            TimeSpan hash,
            long bytes)
            => new(name, UndoFileState.Changed, null, open, hash, TimeSpan.Zero, bytes);

        public static UndoFileResult Failed(
            string name,
            string error,
            TimeSpan open,
            TimeSpan hash,
            TimeSpan delete,
            long bytes)
            => new(name, UndoFileState.Error, error, open, hash, delete, bytes);
    }
}
