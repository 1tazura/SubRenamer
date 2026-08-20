using System.Text.Json;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed record ArchiveIndexCacheRecord(
    string Identity,
    ulong Size,
    long ModifiedUtcTicks,
    SubtitleEntryRef[] Entries)
{
    public bool Matches(ulong size, long modifiedUtcTicks)
        => Size == size && ModifiedUtcTicks == modifiedUtcTicks;
}

/// <summary>
/// Persists the result of a completed archive inspection.
///
/// A cache hit is only accepted when the storage identity, byte size and last
/// modified timestamp all match. Providers that do not expose both metadata
/// fields simply miss the cache and are fully inspected again, preserving the
/// eager-validation semantics.
/// </summary>
public sealed class ArchiveIndexCacheStore
{
    private readonly string _path;

    public ArchiveIndexCacheStore(string? path = null)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            _path = path;
            return;
        }

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = Path.GetTempPath();

        _path = Path.Combine(baseDir, "SubRenamerMobile", "archive-index-cache.json");
    }

    public async Task<IReadOnlyDictionary<string, ArchiveIndexCacheRecord>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return new Dictionary<string, ArchiveIndexCacheRecord>(StringComparer.Ordinal);

        try
        {
            await using var stream = File.OpenRead(_path);
            var records = await JsonSerializer.DeserializeAsync<ArchiveIndexCacheRecord[]>(
                              stream,
                              cancellationToken: cancellationToken)
                          ?? [];

            var output = new Dictionary<string, ArchiveIndexCacheRecord>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (!string.IsNullOrWhiteSpace(record.Identity))
                    output[record.Identity] = record;
            }
            return output;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A corrupt/incompatible cache is never allowed to block scanning.
            return new Dictionary<string, ArchiveIndexCacheRecord>(StringComparer.Ordinal);
        }
    }

    public async Task SaveAsync(
        IEnumerable<ArchiveIndexCacheRecord> records,
        CancellationToken cancellationToken = default)
    {
        var tempPath = _path + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var snapshot = records
                .GroupBy(x => x.Identity, StringComparer.Ordinal)
                .Select(x => x.Last())
                .OrderBy(x => x.Identity, StringComparer.Ordinal)
                .ToArray();

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    new JsonSerializerOptions { WriteIndented = false },
                    cancellationToken);
            }

            File.Move(tempPath, _path, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Cache persistence is an optimization only. A failure must never
            // turn an otherwise successful full scan into a user-visible error.
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { File.Delete(_path); } catch { }
        try { File.Delete(_path + ".tmp"); } catch { }
        return Task.CompletedTask;
    }
}
