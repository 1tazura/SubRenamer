using Avalonia.Platform.Storage;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed record AppliedFileRecord(string DestinationName, string Sha256);

public sealed record ApplyResult(
    int Applied,
    int Skipped,
    IReadOnlyList<string> Errors,
    IReadOnlyList<AppliedFileRecord> CreatedFiles);

public sealed class ApplyService(ArchiveService archiveService)
{
    public async Task<ApplyResult> ApplyAsync(
        MatchPlan plan,
        CancellationToken cancellationToken = default)
    {
        var applied = 0;
        var skipped = 0;
        var errors = new List<string>();
        var createdFiles = new List<AppliedFileRecord>();

        // Recheck the directory once at apply time so a file created after preview
        // is still protected, without issuing one full SAF directory query per item.
        var existingNames = await StorageAccessService.SnapshotChildFileNamesAsync(
            plan.Target.Folder, cancellationToken);

        var looseByName = plan.Source.Kind == SubtitleSourceKind.LooseGroup
            ? plan.Source.LooseFiles.ToDictionary(StorageAccessService.GetDisplayNameFast, StringComparer.Ordinal)
            : null;

        ArchiveService.ArchiveSession? archiveSession = null;
        try
        {
            if (plan.Source.Kind == SubtitleSourceKind.Archive)
            {
                if (plan.Source.ArchiveFile is null)
                    throw new InvalidOperationException("Archive source has no archive file.");

                archiveSession = await archiveService.OpenSessionAsync(plan.Source.ArchiveFile, cancellationToken);
            }

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
                    created = await plan.Target.Folder.CreateFileAsync(item.DestinationName);
                    if (created is null)
                        throw new IOException($"Could not create subtitle file: {item.DestinationName}");

                    CopyFingerprint fingerprint;
                    await using (var destination = await created.OpenWriteAsync())
                    {
                        if (plan.Source.Kind == SubtitleSourceKind.Archive)
                        {
                            if (archiveSession is null)
                                throw new InvalidOperationException("Archive session is unavailable.");

                            fingerprint = await archiveSession.CopyEntryToAsync(
                                item.SourceKey, destination, cancellationToken);
                        }
                        else
                        {
                            if (looseByName is null || !looseByName.TryGetValue(item.SourceKey, out var sourceFile))
                                throw new FileNotFoundException($"Loose subtitle source not found: {item.SourceKey}");

                            await using var source = await sourceFile.OpenReadAsync();
                            fingerprint = await StreamCopyService.CopyWithSha256Async(
                                source, destination, cancellationToken);
                        }

                        await destination.FlushAsync(cancellationToken);
                    }

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
            if (archiveSession is not null)
                await archiveSession.DisposeAsync();
        }

        return new ApplyResult(applied, skipped, errors, createdFiles);
    }
}
