using System.Diagnostics;
using Avalonia.Platform.Storage;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed record ArchiveScanFailure(string ArchiveName, string Error)
{
    public static ArchiveScanFailure FromException(string archiveName, Exception exception)
    {
        var message = exception.Message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (message.Length > 180)
            message = message[..177] + "...";

        if (string.IsNullOrWhiteSpace(message))
            message = "(no message)";

        return new ArchiveScanFailure(
            archiveName,
            $"{exception.GetType().Name}: {message}");
    }

    public override string ToString() => $"{ArchiveName}: {Error}";
}

public sealed record ArchiveScanCount(
    int Total,
    int CacheHits,
    int Reindexed,
    IReadOnlyList<ArchiveScanFailure> Failures)
{
    public int Failed => Failures.Count;

    public override string ToString()
    {
        var failureText = Failed == 0
            ? ""
            : $"，索引失败 {Failed} 包 [{string.Join(" | ", Failures.Take(3))}" +
              (Failed > 3 ? $" | 另有 {Failed - 3} 包" : "") +
              "]";

        // MainView appends the final "包" after this formatted value, so keep
        // the successful re-index count last.
        return $"共 {Total} 包，缓存命中 {CacheHits} 包{failureText}，成功重索引 {Reindexed}";
    }
}

public sealed record SubtitleSourceScanResult(
    IReadOnlyList<SubtitleSource> Sources,
    TimeSpan RootEnumerationElapsed,
    TimeSpan ArchiveIndexElapsed,
    TimeSpan FinalizeElapsed,
    ArchiveScanCount ArchiveCount,
    int LooseSubtitleCount,
    int ArchiveCacheHits,
    int ArchiveValidated);

public sealed class ScanService(ArchiveService archiveService)
{
    private const int FolderScanConcurrency = 4;
    private const int ArchiveScanConcurrency = 2;
    private readonly ArchiveIndexCacheStore _archiveCache = new();

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".ts", ".m2ts", ".webm"
    };

    public static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ass", ".ssa", ".srt", ".vtt", ".sub", ".sup"
    };

    public Task<IReadOnlyList<VideoTarget>> FindVideoTargetsAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => FindVideoTargetsCoreAsync(downloadRoot, cancellationToken),
            cancellationToken);

    private async Task<IReadOnlyList<VideoTarget>> FindVideoTargetsCoreAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken)
    {
        var torrentRoot = await StorageAccessService.FindChildFolderAsync(downloadRoot, "Torrent", cancellationToken);
        if (torrentRoot is null)
            return [];

        var targets = new List<VideoTarget>();
        var pending = new Queue<FolderWork>();
        pending.Enqueue(new FolderWork(torrentRoot, "Torrent", 0));

        // SAF folder enumeration is mostly provider IPC/storage latency. A small,
        // bounded amount of concurrency hides that latency without flooding the
        // DocumentsProvider or opening an unbounded number of cursors.
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = new List<FolderWork>(FolderScanConcurrency);
            while (batch.Count < FolderScanConcurrency && pending.Count > 0)
                batch.Add(pending.Dequeue());

            var snapshots = await Task.WhenAll(batch.Select(x => InspectFolderAsync(x, cancellationToken)));
            foreach (var snapshot in snapshots)
            {
                if (snapshot.Videos.Count > 0)
                {
                    targets.Add(new VideoTarget(
                        snapshot.Work.Folder,
                        snapshot.Work.RelativePath,
                        snapshot.Videos.Select(x => x.File).ToArray()));
                }

                if (snapshot.Work.Depth >= 8)
                    continue;

                foreach (var child in snapshot.Children)
                {
                    pending.Enqueue(new FolderWork(
                        child.Folder,
                        $"{snapshot.Work.RelativePath}/{child.Name}",
                        snapshot.Work.Depth + 1));
                }
            }
        }

        return targets.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<FolderSnapshot> InspectFolderAsync(
        FolderWork work,
        CancellationToken cancellationToken)
    {
        var videos = new List<NamedFile>();
        var children = new List<NamedFolder>();

        await foreach (var item in work.Folder.GetItemsAsync().WithCancellation(cancellationToken))
        {
            switch (item)
            {
                case IStorageFile file:
                {
                    var name = StorageAccessService.GetDisplayNameFast(file);
                    if (VideoExtensions.Contains(Path.GetExtension(name)))
                        videos.Add(new NamedFile(file, name));
                    break;
                }
                case IStorageFolder child:
                    children.Add(new NamedFolder(child, StorageAccessService.GetDisplayNameFast(child)));
                    break;
            }
        }

        return new FolderSnapshot(
            work,
            videos.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            children);
    }

    public async Task<IReadOnlyList<SubtitleSource>> FindSubtitleSourcesAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken = default)
        => (await FindSubtitleSourcesWithMetricsAsync(downloadRoot, cancellationToken)).Sources;

    public Task<SubtitleSourceScanResult> FindSubtitleSourcesWithMetricsAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => FindSubtitleSourcesWithMetricsCoreAsync(downloadRoot, cancellationToken),
            cancellationToken);

    private async Task<SubtitleSourceScanResult> FindSubtitleSourcesWithMetricsCoreAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken)
    {
        var archives = new List<NamedFile>();
        var loose = new List<NamedFile>();

        var rootWatch = Stopwatch.StartNew();
        await foreach (var item in downloadRoot.GetItemsAsync().WithCancellation(cancellationToken))
        {
            if (item is not IStorageFile file)
                continue;

            // On Android ExternalStorageProvider this avoids an extra metadata
            // query for every single item in a large Download directory.
            var name = StorageAccessService.GetDisplayNameFast(file);
            var ext = Path.GetExtension(name);
            if (ArchiveService.ArchiveExtensions.Contains(ext))
                archives.Add(new NamedFile(file, name));
            else if (SubtitleExtensions.Contains(ext))
                loose.Add(new NamedFile(file, name));
        }
        rootWatch.Stop();

        var sources = new List<SubtitleSource>();
        var orderedArchives = archives
            .OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var archiveWatch = Stopwatch.StartNew();
        var cache = await _archiveCache.LoadAsync(cancellationToken);
        var refreshedCache = new ArchiveIndexCacheRecord?[orderedArchives.Length];
        var failures = new ArchiveScanFailure?[orderedArchives.Length];
        var cacheHits = 0;
        var validated = 0;

        if (orderedArchives.Length > 0)
        {
            var indexed = new SubtitleSource?[orderedArchives.Length];
            using var gate = new SemaphoreSlim(ArchiveScanConcurrency);

            var tasks = orderedArchives.Select(async (archive, index) =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var signature = await TryGetArchiveSignatureAsync(archive.File, cancellationToken);
                    if (signature is not null &&
                        cache.TryGetValue(signature.Identity, out var cached) &&
                        cached.Matches(signature.Size, signature.ModifiedUtcTicks))
                    {
                        refreshedCache[index] = cached;
                        Interlocked.Increment(ref cacheHits);

                        // Negative cache entries are equally important: this archive
                        // was fully opened before and confirmed to contain no supported
                        // subtitle files. Do not surface it as a subtitle source.
                        if (cached.Entries.Length == 0)
                            return;

                        indexed[index] = new SubtitleSource(
                            SubtitleSourceKind.Archive,
                            archive.Name,
                            archive.File,
                            [],
                            cached.Entries);
                        return;
                    }

                    // A new/changed archive, a metadata-less provider, or any cache
                    // miss follows the original eager path and is fully inspected.
                    var entries = await archiveService.ListSubtitleEntriesAsync(archive.File, cancellationToken);
                    Interlocked.Increment(ref validated);

                    if (signature is not null)
                    {
                        refreshedCache[index] = new ArchiveIndexCacheRecord(
                            signature.Identity,
                            signature.Size,
                            signature.ModifiedUtcTicks,
                            entries.ToArray());
                    }

                    if (entries.Count == 0)
                        return;

                    indexed[index] = new SubtitleSource(
                        SubtitleSourceKind.Archive,
                        archive.Name,
                        archive.File,
                        [],
                        entries);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Do not keep/reuse a stale cache entry after a failed attempt to
                    // validate a file whose signature no longer matched. Record the
                    // failure so an archive can no longer disappear from scan counts.
                    failures[index] = ArchiveScanFailure.FromException(archive.Name, ex);
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);
            sources.AddRange(indexed.OfType<SubtitleSource>());
        }

        // Save one coherent snapshot after the parallel work. Removed archives are
        // naturally pruned, and failures/metadata-less files are revalidated later.
        await _archiveCache.SaveAsync(refreshedCache.OfType<ArchiveIndexCacheRecord>(), cancellationToken);
        archiveWatch.Stop();

        var finalizeWatch = Stopwatch.StartNew();
        foreach (var group in loose.GroupBy(x => FilenameHeuristics.LooseClusterKey(x.Name)))
        {
            var files = group.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var entries = files.Select(x => new SubtitleEntryRef(
                x.Name,
                x.Name,
                Path.GetExtension(x.Name)))
                .ToArray();

            sources.Add(new SubtitleSource(
                SubtitleSourceKind.LooseGroup,
                files.Length == 1 ? files[0].Name : $"裸字幕组 · {files.Length} 个",
                null,
                files.Select(x => x.File).ToArray(),
                entries));
        }
        finalizeWatch.Stop();

        var archiveFailures = failures.OfType<ArchiveScanFailure>().ToArray();

        return new SubtitleSourceScanResult(
            sources.ToArray(),
            rootWatch.Elapsed,
            archiveWatch.Elapsed,
            finalizeWatch.Elapsed,
            new ArchiveScanCount(orderedArchives.Length, cacheHits, validated, archiveFailures),
            loose.Count,
            cacheHits,
            validated);
    }

    private static async Task<ArchiveSignature?> TryGetArchiveSignatureAsync(
        IStorageFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            var properties = await file.GetBasicPropertiesAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (properties.Size is not { } size || properties.DateModified is not { } modified)
                return null;

            return new ArchiveSignature(
                file.Path.ToString(),
                size,
                modified.UtcDateTime.Ticks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Metadata is an optimization only. Missing/unreliable metadata must
            // cause a full archive inspection rather than weakening correctness.
            return null;
        }
    }

    private sealed record ArchiveSignature(string Identity, ulong Size, long ModifiedUtcTicks);
    private sealed record NamedFile(IStorageFile File, string Name);
    private sealed record NamedFolder(IStorageFolder Folder, string Name);
    private sealed record FolderWork(IStorageFolder Folder, string RelativePath, int Depth);
    private sealed record FolderSnapshot(
        FolderWork Work,
        IReadOnlyList<NamedFile> Videos,
        IReadOnlyList<NamedFolder> Children);
}
