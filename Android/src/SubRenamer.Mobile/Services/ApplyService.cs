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

        var looseByName = plan.Source.Kind == SubtitleSourceKind.LooseGroup
            ? plan.Source.LooseFiles.ToDictionary(StorageAccessService.GetDisplayNameFast, StringComparer.Ordinal)
            : null;

        ArchiveService.ArchiveSession? archiveSession = null;
        var sourcePreparationWatch = Stopwatch.StartNew();
        try
        {
            if (plan.Source.Kind == SubtitleSourceKind.Archive)
            {
                if (plan.Source.ArchiveFile is null)
                    throw new InvalidOperationException("Archive source has no archive file.");

                archiveSession = await archiveService.OpenSessionAsync(plan.Source.ArchiveFile, cancellationToken);
            }
            sourcePreparationWatch.Stop();

            foreach (var item in plan.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.Status != PlanItemStatus.Ready)
                {
                    skipped++;
                    continue;
                }

                if (existingNames.Contains(item.DestinationName))
                {
                    skipped++;
                    continue;
                }

                IStorageFile? created = null;
                try
                {
                    var createWatch = Stopwatch.StartNew();
                    created = await plan.Target.Folder.CreateFileAsync(item.DestinationName);
                    createWatch.Stop();
                    destinationCreateElapsed += createWatch.Elapsed;

                    if (created is null)
                        throw new IOException($"Could not create subtitle file: {item.DestinationName}");

                    var openWatch = Stopwatch.StartNew();
                    var destination = await created.OpenWriteAsync();
                    openWatch.Stop();
                    destinationOpenElapsed += openWatch.Elapsed;

                    CopyFingerprint fingerprint;
                    try
                    {
                        if (plan.Source.Kind == SubtitleSourceKind.Archive)
                        {
                            if (archiveSession is null)
                                throw new InvalidOperationException("Archive session is unavailable.");

                            var transferWatch = Stopwatch.StartNew();
                            fingerprint = await archiveSession.CopyEntryToAsync(
                                item.SourceKey, destination, cancellationToken);
                            transferWatch.Stop();
                            transferElapsed += transferWatch.Elapsed;
                        }
                        else
                        {
                            if (looseByName is null || !looseByName.TryGetValue(item.SourceKey, out var sourceFile))
                                throw new FileNotFoundException($"Loose subtitle source not found: {item.SourceKey}");

                            var sourceOpenWatch = Stopwatch.StartNew();
                            var source = await sourceFile.OpenReadAsync();
                            sourceOpenWatch.Stop();
                            sourceOpenElapsed += sourceOpenWatch.Elapsed;

                            await using (source)
                            {
                                var transferWatch = Stopwatch.StartNew();
                                fingerprint = await StreamCopyService.CopyWithSha256Async(
                                    source, destination, cancellationToken);
                                transferWatch.Stop();
                                transferElapsed += transferWatch.Elapsed;
                            }
                        }
                    }
                    finally
                    {
                        // Closing the SAF output stream is the commit boundary. An
                        // explicit FlushAsync immediately before DisposeAsync only
                        // duplicated provider work, so disposal is timed directly.
                        var closeWatch = Stopwatch.StartNew();
                        await destination.DisposeAsync();
                        closeWatch.Stop();
                        destinationCloseElapsed += closeWatch.Elapsed;
                    }

                    bytesWritten += fingerprint.Length;
                    existingNames.Add(item.DestinationName);
                    createdFiles.Add(new AppliedFileRecord(item.DestinationName, fingerprint.Sha256));
                    applied++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{item.SourceDisplayName} → {item.DestinationName}: {ex.Message}");
                    if (created is not null)
                    {
                        try { await created.DeleteAsync(); } catch { }
                    }
                }
            }
        }
        finally
        {
            if (sourcePreparationWatch.IsRunning)
                sourcePreparationWatch.Stop();

            if (archiveSession is not null)
                await archiveSession.DisposeAsync();
        }

        totalWatch.Stop();
        var performance = new ApplyPerformance(
            snapshotWatch.Elapsed,
            sourcePreparationWatch.Elapsed,
            destinationCreateElapsed,
            destinationOpenElapsed,
            sourceOpenElapsed,
            transferElapsed,
            destinationCloseElapsed,
            totalWatch.Elapsed,
            bytesWritten);

        return new ApplyResult(applied, skipped, errors, createdFiles, performance);
    }
}
