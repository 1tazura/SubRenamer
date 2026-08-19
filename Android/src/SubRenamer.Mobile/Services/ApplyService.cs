using Avalonia.Platform.Storage;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed record ApplyResult(int Applied, int Skipped, IReadOnlyList<string> Errors);

public sealed class ApplyService(ArchiveService archiveService)
{
    public async Task<ApplyResult> ApplyAsync(
        MatchPlan plan,
        CancellationToken cancellationToken = default)
    {
        var applied = 0;
        var skipped = 0;
        var errors = new List<string>();

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

                var existing = await StorageAccessService.FindChildFileAsync(
                    plan.Target.Folder, item.DestinationName, cancellationToken);
                if (existing is not null)
                {
                    skipped++;
                    continue;
                }

                IStorageFile? created = null;
                try
                {
                    created = await plan.Target.Folder.CreateFileAsync(item.DestinationName);
                    await using var destination = await created.OpenWriteAsync();

                    if (plan.Source.Kind == SubtitleSourceKind.Archive)
                    {
                        if (archiveSession is null)
                            throw new InvalidOperationException("Archive session is unavailable.");

                        await archiveSession.CopyEntryToAsync(item.SourceKey, destination, cancellationToken);
                    }
                    else
                    {
                        var sourceFile = plan.Source.LooseFiles
                            .FirstOrDefault(x => string.Equals(x.Name, item.SourceKey, StringComparison.Ordinal));
                        if (sourceFile is null)
                            throw new FileNotFoundException($"Loose subtitle source not found: {item.SourceKey}");

                        await using var source = await sourceFile.OpenReadAsync();
                        await source.CopyToAsync(destination, cancellationToken);
                    }

                    await destination.FlushAsync(cancellationToken);
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

        return new ApplyResult(applied, skipped, errors);
    }
}
