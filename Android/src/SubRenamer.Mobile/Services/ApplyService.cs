using System.Diagnostics;
using Avalonia.Platform.Storage;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed record AppliedFileRecord(string DestinationName, string Sha256);

public sealed record ApplyResult(
    int Applied,
    int Skipped,
    IReadOnlyList<string> Errors,
    IReadOnlyList<AppliedFileRecord> CreatedFiles,
    ApplyPerformance Performance);

public sealed class ApplyService(ArchiveService archiveService)
{
    // Real-device v0.1.20 measurements showed CreateFileAsync + OpenWriteAsync
    // accounting for roughly three quarters of 50-item apply time. Keep only a
    // small look-ahead window so provider latency can overlap with sequential
    // archive extraction without flooding DocumentsProvider or pre-creating the
    // whole batch at once.
    private const int DestinationPreparationConcurrency = 4;

    public async Task<ApplyResult> ApplyAsync(
        MatchPlan plan,
        CancellationToken cancellationToken = default)
    {
        var totalWatch = Stopwatch.StartNew();
        var applied = 0;
        var skipped = 0;
        var errors = new List<string>();
        var createdFiles = new List<AppliedFileRecord>();

        var destinationCreateElapsed = TimeSpan.Zero;
        var destinationOpenElapsed = TimeSpan.Zero;
        var destinationReadyWaitElapsed = TimeSpan.Zero;
        var sourceOpenElapsed = TimeSpan.Zero;
        var transferElapsed = TimeSpan.Zero;
        var destinationCloseElapsed = TimeSpan.Zero;
        long bytesWritten = 0;

        // Recheck the directory once at apply time so a file created after preview
        // is still protected, without issuing one full SAF directory query per item.
        var snapshotWatch = Stopwatch.StartNew();
        var existingNames = await StorageAccessService.SnapshotChildFileNamesAsync(
            plan.Target.Folder, cancellationToken);
        snapshotWatch.Stop();

        // Reserve names before asynchronous preparation starts. This both preserves
        // no-overwrite behavior and prevents two Ready rows with the same destination
        // from racing each other through CreateFileAsync.
        var readyItems = new List<MatchPlanItem>();
        var reservedNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items)
        {
            if (item.Status != PlanItemStatus.Ready)
            {
                skipped++;
                continue;
            }

            if (!reservedNames.Add(item.DestinationName))
            {
                skipped++;
                continue;
            }

            readyItems.Add(item);
        }

        var looseByName = plan.Source.Kind == SubtitleSourceKind.LooseGroup
            ? plan.Source.LooseFiles.ToDictionary(StorageAccessService.GetDisplayNameFast, StringComparer.Ordinal)
            : null;

        ArchiveService.ArchiveSession? archiveSession = null;
        var sourcePreparationWatch = Stopwatch.StartNew();
        var pendingPreparations = new Queue<Task<PreparedDestination>>();
        var nextPreparationIndex = 0;

        void StartPreparations()
        {
            while (pendingPreparations.Count < DestinationPreparationConcurrency &&
                   nextPreparationIndex < readyItems.Count)
            {
                var item = readyItems[nextPreparationIndex++];
                pendingPreparations.Enqueue(Task.Run(
                    () => PrepareDestinationAsync(plan.Target.Folder, item, cancellationToken),
                    cancellationToken));
            }
        }

        try
        {
            if (plan.Source.Kind == SubtitleSourceKind.Archive)
            {
                if (plan.Source.ArchiveFile is null)
                    throw new InvalidOperationException("Archive source has no archive file.");

                archiveSession = await archiveService.OpenSessionAsync(plan.Source.ArchiveFile, cancellationToken);
            }
            sourcePreparationWatch.Stop();

            StartPreparations();

            while (pendingPreparations.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var waitWatch = Stopwatch.StartNew();
                var prepared = await pendingPreparations.Dequeue();
                waitWatch.Stop();
                destinationReadyWaitElapsed += waitWatch.Elapsed;
                destinationCreateElapsed += prepared.CreateElapsed;
                destinationOpenElapsed += prepared.OpenElapsed;

                // Refill before copying the current subtitle so the next destination
                // can be created/opened while archive extraction + hashing + writing
                // remains strictly sequential.
                StartPreparations();

                if (!prepared.IsReady)
                {
                    errors.Add($"{prepared.Item.SourceDisplayName} → {prepared.Item.DestinationName}: {prepared.Error}");
                    continue;
                }

                var created = prepared.File!;
                Stream? destination = prepared.Destination!;
                CopyFingerprint? fingerprint = null;
                Exception? itemFailure = null;

                try
                {
                    if (plan.Source.Kind == SubtitleSourceKind.Archive)
                    {
                        if (archiveSession is null)
                            throw new InvalidOperationException("Archive session is unavailable.");

                        var transferWatch = Stopwatch.StartNew();
                        try
                        {
                            fingerprint = await archiveSession.CopyEntryToAsync(
                                prepared.Item.SourceKey, destination, cancellationToken);
                        }
                        finally
                        {
                            transferWatch.Stop();
                            transferElapsed += transferWatch.Elapsed;
                        }
                    }
                    else
                    {
                        if (looseByName is null || !looseByName.TryGetValue(prepared.Item.SourceKey, out var sourceFile))
                            throw new FileNotFoundException($"Loose subtitle source not found: {prepared.Item.SourceKey}");

                        var sourceOpenWatch = Stopwatch.StartNew();
                        var source = await sourceFile.OpenReadAsync();
                        sourceOpenWatch.Stop();
                        sourceOpenElapsed += sourceOpenWatch.Elapsed;

                        await using (source)
                        {
                            var transferWatch = Stopwatch.StartNew();
                            try
                            {
                                fingerprint = await StreamCopyService.CopyWithSha256Async(
                                    source, destination, cancellationToken);
                            }
                            finally
                            {
                                transferWatch.Stop();
                                transferElapsed += transferWatch.Elapsed;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    itemFailure = ex;
                }

                // Closing the SAF output stream is the commit boundary. Always close
                // before deciding whether the item succeeded, and include provider
                // close failures in the item result.
                var closeWatch = Stopwatch.StartNew();
                try
                {
                    await destination.DisposeAsync();
                    destination = null;
                }
                catch (Exception ex)
                {
                    itemFailure ??= ex;
                }
                finally
                {
                    closeWatch.Stop();
                    destinationCloseElapsed += closeWatch.Elapsed;
                }

                if (itemFailure is not null || fingerprint is null)
                {
                    if (destination is not null)
                    {
                        try { await destination.DisposeAsync(); } catch { }
                    }
                    try { await created.DeleteAsync(); } catch { }

                    if (itemFailure is OperationCanceledException && cancellationToken.IsCancellationRequested)
                        throw itemFailure;

                    errors.Add(
                        $"{prepared.Item.SourceDisplayName} → {prepared.Item.DestinationName}: " +
                        (itemFailure?.Message ?? "Copy did not produce a fingerprint."));
                    continue;
                }

                bytesWritten += fingerprint.Length;
                createdFiles.Add(new AppliedFileRecord(prepared.Item.DestinationName, fingerprint.Sha256));
                applied++;
            }
        }
        finally
        {
            if (sourcePreparationWatch.IsRunning)
                sourcePreparationWatch.Stop();

            // On cancellation or an unexpected outer failure, only the small
            // look-ahead window can contain prepared-but-unwritten files. Drain it
            // and remove every such file best-effort before returning/throwing.
            await CleanupPendingPreparationsAsync(pendingPreparations);

            if (archiveSession is not null)
                await archiveSession.DisposeAsync();
        }

        totalWatch.Stop();
        var performance = new ApplyPerformance(
            snapshotWatch.Elapsed,
            sourcePreparationWatch.Elapsed,
            destinationCreateElapsed,
            destinationOpenElapsed,
            destinationReadyWaitElapsed,
            DestinationPreparationConcurrency,
            sourceOpenElapsed,
            transferElapsed,
            destinationCloseElapsed,
            totalWatch.Elapsed,
            bytesWritten);

        return new ApplyResult(applied, skipped, errors, createdFiles, performance);
    }

    private static async Task<PreparedDestination> PrepareDestinationAsync(
        IStorageFolder targetFolder,
        MatchPlanItem item,
        CancellationToken cancellationToken)
    {
        IStorageFile? created = null;
        Stream? destination = null;
        var createElapsed = TimeSpan.Zero;
        var openElapsed = TimeSpan.Zero;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var createWatch = Stopwatch.StartNew();
            created = await targetFolder.CreateFileAsync(item.DestinationName);
            createWatch.Stop();
            createElapsed = createWatch.Elapsed;

            cancellationToken.ThrowIfCancellationRequested();
            if (created is null)
                throw new IOException($"Could not create subtitle file: {item.DestinationName}");

            // DocumentsProvider may resolve a create-name collision by returning a
            // renamed file. Never silently accept that: it would violate the exact
            // destination preview even though it would not overwrite the old file.
            var actualName = StorageAccessService.GetDisplayNameFast(created);
            if (!string.Equals(actualName, item.DestinationName, StringComparison.Ordinal))
                throw new IOException($"Destination name changed during creation: {actualName}");

            var openWatch = Stopwatch.StartNew();
            destination = await created.OpenWriteAsync();
            openWatch.Stop();
            openElapsed = openWatch.Elapsed;

            cancellationToken.ThrowIfCancellationRequested();
            return new PreparedDestination(item, created, destination, createElapsed, openElapsed, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (destination is not null)
            {
                try { await destination.DisposeAsync(); } catch { }
            }
            if (created is not null)
            {
                try { await created.DeleteAsync(); } catch { }
            }
            throw;
        }
        catch (Exception ex)
        {
            if (destination is not null)
            {
                try { await destination.DisposeAsync(); } catch { }
            }
            if (created is not null)
            {
                try { await created.DeleteAsync(); } catch { }
            }

            return new PreparedDestination(item, null, null, createElapsed, openElapsed, ex.Message);
        }
    }

    private static async Task CleanupPendingPreparationsAsync(
        Queue<Task<PreparedDestination>> pendingPreparations)
    {
        while (pendingPreparations.Count > 0)
        {
            try
            {
                var prepared = await pendingPreparations.Dequeue();
                if (!prepared.IsReady)
                    continue;

                try { await prepared.Destination!.DisposeAsync(); } catch { }
                try { await prepared.File!.DeleteAsync(); } catch { }
            }
            catch
            {
                // Preparation already performs best-effort cleanup on its own.
            }
        }
    }

    private sealed record PreparedDestination(
        MatchPlanItem Item,
        IStorageFile? File,
        Stream? Destination,
        TimeSpan CreateElapsed,
        TimeSpan OpenElapsed,
        string? Error)
    {
        public bool IsReady => File is not null && Destination is not null && Error is null;
    }
}
