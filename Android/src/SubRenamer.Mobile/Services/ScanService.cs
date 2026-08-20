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
    IReadOnlyList<ArchiveScanFailure> StableRejections,
    int StableRejectionCacheHits,
    IReadOnlyList<ArchiveScanFailure> Failures)
{
    public int StableRejected => StableRejections.Count;
    public int Failed => Failures.Count;
    public int Accounted => CacheHits + Reindexed + StableRejected + Failed;

    public override string ToString()
    {
        var rejectionText = StableRejected == 0
            ? ""
            : $"，稳定排除 {StableRejected} 包（缓存 {StableRejectionCacheHits}） [{FormatIssues(StableRejections)}]";

        var failureText = Failed == 0
            ? ""
            : $"，索引失败 {Failed} 包 [{FormatIssues(Failures)}]";

        // MainView appends the final "包" after this formatted value, so keep
        // the successful re-index count last.
        return $"共 {Total} 包，缓存命中 {CacheHits} 包{rejectionText}{failureText}，成功重索引 {Reindexed}";
    }

    private static string FormatIssues(IReadOnlyList<ArchiveScanFailure> issues)
        => string.Join(" | ", issues.Take(3)) +
           (issues.Count > 3 ? $" | 另有 {issues.Count - 3} 包" : "");
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

public sealed record FullScanResult(
    IReadOnlyList<VideoTarget> Targets,
    TimeSpan VideoTraversalElapsed,
    SubtitleSourceScanResult SubtitleScan);

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

    /// <summary>
    /// Main Android scan path. Download root is enumerated exactly once to find
    /// Torrent plus direct subtitle/archive sources, then Torrent traversal and
    /// archive indexing run on independent workers.
    /// </summary>
    public Task<FullScanResult> ScanAllWithMetricsAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => ScanAllWithMetricsCoreAsync(downloadRoot, cancellationToken),
            cancellationToken);

    private async Task<FullScanResult> ScanAllWithMetricsCoreAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken)
    {
        var rootWatch = Stopwatch.StartNew();
        var root = await SnapshotDownloadRootAsync(downloadRoot, cancellationToken);
        rootWatch.Stop();

        var videoTask = Task.Run(async () =>
        {
            var watch = Stopwatch.StartNew();
            var targets = await FindVideoTargetsFromTorrentRootCoreAsync(root.TorrentRoot, cancellationToken);
            watch.Stop();
            return (Targets: targets, Elapsed: watch.Elapsed);
        }, cancellationToken);

        var sourceTask = Task.Run(
            () => BuildSubtitleSourcesWithMetricsCoreAsync(
                root.Archives,
                root.LooseSubtitles,
                rootWatch.Elapsed,
                cancellationToken),
            cancellationToken);

        await Task.WhenAll(videoTask, sourceTask).ConfigureAwait(false);

        return new FullScanResult(
            videoTask.Result.Targets,
            videoTask.Result.Elapsed,
            sourceTask.Result);
    }

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
        var root = await SnapshotDownloadRootAsync(downloadRoot, cancellationToken);
        return await FindVideoTargetsFromTorrentRootCoreAsync(root.TorrentRoot, cancellationToken);
    }

    private async Task<IReadOnlyList<VideoTarget>> FindVideoTargetsFromTorrentRootCoreAsync(
        IStorageFolder? torrentRoot,
        CancellationToken cancellationToken)
    {
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
        var rootWatch = Stopwatch.StartNew();
        var root = await SnapshotDownloadRootAsync(downloadRoot, cancellationToken);
        rootWatch.Stop();

        return await BuildSubtitleSourcesWithMetricsCoreAsync(
            root.Archives,
            root.LooseSubtitles,
            rootWatch.Elapsed,
            cancellationToken);
    }

    private async Task<SubtitleSourceScanResult> BuildSubtitleSourcesWithMetricsCoreAsync(
        IReadOnlyList<NamedFile> archives,
        IReadOnlyList<NamedFile> loose,
        TimeSpan rootEnumerationElapsed,
        CancellationToken cancellationToken)
    {
        var sources = new List<SubtitleSource>();
        var orderedArchives = archives
            .OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var archiveWatch = Stopwatch.StartNew();
        var cache = await _archiveCache.LoadAsync(cancellationToken);
        var refreshedCache = new ArchiveIndexCacheRecord?[orderedArchives.Length];
        var stableRejections = new ArchiveScanFailure?[orderedArchives.Length];
        var failures = new ArchiveScanFailure?[orderedArchives.Length];
        var cacheHits = 0;
        var stableRejectionCacheHits = 0;
        var validated = 0;

        if (orderedArchives.Length > 0)
        {
            var indexed = new SubtitleSource?[orderedArchives.Length];
            using var gate = new SemaphoreSlim(ArchiveScanConcurrency);

            var tasks = orderedArchives.Select(async (archive, index) =>
            {
                await gate.WaitAsync(cancellationToken);
                ArchiveSignature? signature = null;
                try
                {
                    signature = await TryGetArchiveSignatureAsync(archive.File, cancellationToken);
                    if (signature is not null &&
                        cache.TryGetValue(signature.Identity, out var cached) &&
                        cached.Matches(signature.Size, signature.ModifiedUtcTicks))
                    {
                        refreshedCache[index] = cached;

                        if (cached.IsStableRejection)
                        {
                            Interlocked.Increment(ref stableRejectionCacheHits);
                            stableRejections[index] = new ArchiveScanFailure(
                                archive.Name,
                                cached.StableRejectionError!);
                            return;
                        }

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
                    var issue = ArchiveScanFailure.FromException(archive.Name, ex);

                    // Only cache a deliberately narrow deterministic format rejection.
                    // Transient I/O/provider/permission failures are never remembered.
                    if (signature is not null && ArchiveRejectionPolicy.IsStable(ex))
                    {
                        stableRejections[index] = issue;
                        refreshedCache[index] = new ArchiveIndexCacheRecord(
                            signature.Identity,
                            signature.Size,
                            signature.ModifiedUtcTicks,
                            [],
                            issue.Error);
                    }
                    else
                    {
                        failures[index] = issue;
                    }
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
        // naturally pruned. Stable deterministic rejections are remembered only
        // when the full identity/size/mtime signature is available.
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

        var archiveStableRejections = stableRejections.OfType<ArchiveScanFailure>().ToArray();
        var archiveFailures = failures.OfType<ArchiveScanFailure>().ToArray();

        return new SubtitleSourceScanResult(
            sources.ToArray(),
            rootEnumerationElapsed,
            archiveWatch.Elapsed,
            finalizeWatch.Elapsed,
            new ArchiveScanCount(
                orderedArchives.Length,
                cacheHits,
                validated,
                archiveStableRejections,
                stableRejectionCacheHits,
                archiveFailures),
            loose.Count,
            cacheHits,
            validated);
    }

    private static async Task<DownloadRootSnapshot> SnapshotDownloadRootAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken)
    {
        IStorageFolder? torrentRoot = null;
        var archives = new List<NamedFile>();
        var loose = new List<NamedFile>();

        await foreach (var item in downloadRoot.GetItemsAsync().WithCancellation(cancellationToken))
        {
            switch (item)
            {
                case IStorageFolder folder:
                    if (torrentRoot is null &&
                        string.Equals(
                            StorageAccessService.GetDisplayNameFast(folder),
                            "Torrent",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        torrentRoot = folder;
                    }
                    break;

                case IStorageFile file:
                {
                    // On Android ExternalStorageProvider this avoids an extra metadata
                    // query for every single item in a large Download directory.
                    var name = StorageAccessService.GetDisplayNameFast(file);
                    var ext = Path.GetExtension(name);
                    if (ArchiveService.ArchiveExtensions.Contains(ext))
                        archives.Add(new NamedFile(file, name));
                    else if (SubtitleExtensions.Contains(ext))
                        loose.Add(new NamedFile(file, name));
                    break;
                }
            }
        }

        return new DownloadRootSnapshot(torrentRoot, archives, loose);
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
    private sealed record DownloadRootSnapshot(
        IStorageFolder? TorrentRoot,
        IReadOnlyList<NamedFile> Archives,
        IReadOnlyList<NamedFile> LooseSubtitles);
    private sealed record NamedFile(IStorageFile File, string Name);
    private sealed record NamedFolder(IStorageFolder Folder, string Name);
    private sealed record FolderWork(IStorageFolder Folder, string RelativePath, int Depth);
    private sealed record FolderSnapshot(
        FolderWork Work,
        IReadOnlyList<NamedFile> Videos,
        IReadOnlyList<NamedFolder> Children);
}
