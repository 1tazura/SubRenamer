using System.Security.Cryptography;
using Avalonia.Platform.Storage;

namespace SubRenamer.Mobile.Services;

public sealed record UndoResult(
    int Deleted,
    int Missing,
    int Changed,
    IReadOnlyList<string> Errors);

public sealed class UndoService(SettingsStore settingsStore)
{
    public async Task<UndoResult> UndoLastAsync(
        IStorageFolder downloadRoot,
        UndoBatchRecord batch,
        CancellationToken cancellationToken = default)
    {
        var target = await StorageAccessService.ResolveRelativeFolderAsync(
            downloadRoot, batch.TargetRelativePath, cancellationToken);

        if (target is null)
        {
            return new UndoResult(
                0,
                0,
                0,
                [$"找不到上次处理的目标目录：{batch.TargetRelativePath}"]);
        }

        var files = await StorageAccessService.SnapshotChildFilesAsync(target, cancellationToken);
        var deleted = 0;
        var missing = 0;
        var changed = 0;
        var errors = new List<string>();

        foreach (var record in batch.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!files.TryGetValue(record.DestinationName, out var file))
            {
                missing++;
                continue;
            }

            try
            {
                await using var stream = await file.OpenReadAsync();
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));

                // Never delete a file that has been edited or replaced since this
                // app created it. This makes persisted undo safe across restarts.
                if (!string.Equals(hash, record.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    changed++;
                    continue;
                }

                await file.DeleteAsync();
                deleted++;
            }
            catch (Exception ex)
            {
                errors.Add($"{record.DestinationName}: {ex.Message}");
            }
        }

        // A successful undo attempt consumes the journal. Files that were changed
        // are deliberately kept and are reported rather than retried later.
        if (errors.Count == 0)
            await settingsStore.ClearUndoBatchAsync(cancellationToken);

        return new UndoResult(deleted, missing, changed, errors);
    }
}
