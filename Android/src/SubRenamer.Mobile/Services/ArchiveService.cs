using Avalonia.Platform.Storage;
using SharpCompress.Archives;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed class ArchiveService
{
    public static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar"
    };

    public async Task<IReadOnlyList<SubtitleEntryRef>> ListSubtitleEntriesAsync(
        IStorageFile archiveFile,
        CancellationToken cancellationToken = default)
    {
        await using var session = await OpenSessionAsync(archiveFile, cancellationToken);

        return session.Entries
            .Where(x => !x.IsDirectory)
            .Select(x => new { Key = x.Key, x.Size })
            .Where(x => !string.IsNullOrEmpty(x.Key))
            .Select(x => new { Key = x.Key!, x.Size })
            .Where(x => ScanService.SubtitleExtensions.Contains(Path.GetExtension(x.Key)))
            .Select(x => new SubtitleEntryRef(
                x.Key,
                Path.GetFileName(x.Key),
                Path.GetExtension(x.Key),
                x.Size))
            .ToArray();
    }

    public async Task<ArchiveSession> OpenSessionAsync(
        IStorageFile archiveFile,
        CancellationToken cancellationToken = default)
    {
        var source = await archiveFile.OpenReadAsync();

        if (source.CanSeek)
        {
            try
            {
                return new ArchiveSession(source, ArchiveFactory.OpenArchive(source), null);
            }
            catch
            {
                await source.DisposeAsync();
                throw;
            }
        }

        var extension = Path.GetExtension(archiveFile.Name);
        var temp = Path.Combine(Path.GetTempPath(), $"subrenamer-{Guid.NewGuid():N}{extension}");

        try
        {
            await using (source)
            await using (var target = File.Create(temp))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            var seekable = File.OpenRead(temp);
            try
            {
                return new ArchiveSession(seekable, ArchiveFactory.OpenArchive(seekable), temp);
            }
            catch
            {
                await seekable.DisposeAsync();
                TryDelete(temp);
                throw;
            }
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public sealed class ArchiveSession : IAsyncDisposable
    {
        private readonly Stream _input;
        private readonly IArchive _archive;
        private readonly string? _tempPath;
        private readonly Dictionary<string, IArchiveEntry> _entriesByKey;

        internal ArchiveSession(Stream input, IArchive archive, string? tempPath)
        {
            _input = input;
            _archive = archive;
            _tempPath = tempPath;
            _entriesByKey = new Dictionary<string, IArchiveEntry>(StringComparer.Ordinal);

            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory && !string.IsNullOrEmpty(entry.Key))
                    _entriesByKey.TryAdd(entry.Key!, entry);
            }
        }

        public IEnumerable<IArchiveEntry> Entries => _archive.Entries;

        public async Task CopyEntryToAsync(
            string entryKey,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            if (!_entriesByKey.TryGetValue(entryKey, out var entry))
                throw new FileNotFoundException($"Archive entry was not found: {entryKey}");

            using var source = entry.OpenEntryStream();
            await source.CopyToAsync(destination, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _archive.Dispose();
            await _input.DisposeAsync();
            if (_tempPath is not null)
                TryDelete(_tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
