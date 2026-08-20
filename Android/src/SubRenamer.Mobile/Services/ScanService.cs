using System.Diagnostics;
using Avalonia.Platform.Storage;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed record SubtitleSourceScanResult(
    IReadOnlyList<SubtitleSource> Sources,
    TimeSpan RootEnumerationElapsed,
    TimeSpan ArchiveIndexElapsed,
    TimeSpan FinalizeElapsed,
    int ArchiveCount,
    int LooseSubtitleCount);

public sealed class ScanService(ArchiveService archiveService)
{
    private const int FolderScanConcurrency = 4;
    private const int ArchiveScanConcurrency = 2;

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".ts", ".m2ts", ".webm"
    };

    public static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ass", ".ssa", ".srt", ".vtt", ".sub", ".sup"
    };

    public async Task<IReadOnlyList<VideoTarget>> FindVideoTargetsAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken = default)
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
                        snapshot.Videos));
                }

                if (snapshot.Work.Depth >= 8)
                    continue;

                foreach (var child in snapshot.Children)
                {
                    pending.Enqueue(new FolderWork(
                        child,
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
        var videos = new List<IStorageFile>();
        var children = new List<IStorageFolder>();

        await foreach (var item in work.Folder.GetItemsAsync().WithCancellation(cancellationToken))
        {
            switch (item)
            {
                case IStorageFile file when VideoExtensions.Contains(Path.GetExtension(file.Name)):
                    videos.Add(file);
                    break;
                case IStorageFolder child:
                    children.Add(child);
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

    public async Task<SubtitleSourceScanResult> FindSubtitleSourcesWithMetricsAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken = default)
    {
        var archives = new List<IStorageFile>();
        var loose = new List<IStorageFile>();

        var rootWatch = Stopwatch.StartNew();
        await foreach (var item in downloadRoot.GetItemsAsync().WithCancellation(cancellationToken))
        {
            if (item is not IStorageFile file)
                continue;

            var ext = Path.GetExtension(file.Name);
            if (ArchiveService.ArchiveExtensions.Contains(ext))
                archives.Add(file);
            else if (SubtitleExtensions.Contains(ext))
                loose.Add(file);
        }
        rootWatch.Stop();

        var sources = new List<SubtitleSource>();
        var orderedArchives = archives
            .OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var archiveWatch = Stopwatch.StartNew();
        if (orderedArchives.Length > 0)
        {
            var indexed = new SubtitleSource?[orderedArchives.Length];
            using var gate = new SemaphoreSlim(ArchiveScanConcurrency);

            var tasks = orderedArchives.Select(async (archive, index) =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var entries = await archiveService.ListSubtitleEntriesAsync(archive, cancellationToken);
                    if (entries.Count == 0)
                        return;

                    indexed[index] = new SubtitleSource(
                        SubtitleSourceKind.Archive,
                        archive.Name,
                        archive,
                        [],
                        entries);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);
            sources.AddRange(indexed.OfType<SubtitleSource>());
        }
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
                files,
                entries));
        }
        finalizeWatch.Stop();

        return new SubtitleSourceScanResult(
            sources.ToArray(),
            rootWatch.Elapsed,
            archiveWatch.Elapsed,
            finalizeWatch.Elapsed,
            orderedArchives.Length,
            loose.Count);
    }

    private sealed record FolderWork(IStorageFolder Folder, string RelativePath, int Depth);
    private sealed record FolderSnapshot(
        FolderWork Work,
        IReadOnlyList<IStorageFile> Videos,
        IReadOnlyList<IStorageFolder> Children);
}
