using Avalonia.Platform.Storage;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed class ScanService(ArchiveService archiveService)
{
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
        await DiscoverTargetsRecursiveAsync(torrentRoot, "Torrent", targets, 0, cancellationToken);

        return targets.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task DiscoverTargetsRecursiveAsync(
        IStorageFolder folder,
        string relativePath,
        List<VideoTarget> output,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > 8)
            return;

        var videos = new List<IStorageFile>();
        var children = new List<IStorageFolder>();

        await foreach (var item in folder.GetItemsAsync().WithCancellation(cancellationToken))
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

        if (videos.Count > 0)
        {
            output.Add(new VideoTarget(
                folder,
                relativePath,
                videos.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        foreach (var child in children)
        {
            await DiscoverTargetsRecursiveAsync(
                child,
                $"{relativePath}/{child.Name}",
                output,
                depth + 1,
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SubtitleSource>> FindSubtitleSourcesAsync(
        IStorageFolder downloadRoot,
        CancellationToken cancellationToken = default)
    {
        var archives = new List<IStorageFile>();
        var loose = new List<IStorageFile>();

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

        var sources = new List<SubtitleSource>();

        foreach (var archive in archives.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var entries = await archiveService.ListSubtitleEntriesAsync(archive, cancellationToken);
                if (entries.Count == 0)
                    continue;

                sources.Add(new SubtitleSource(
                    SubtitleSourceKind.Archive,
                    archive.Name,
                    archive,
                    [],
                    entries));
            }
            catch
            {
            }
        }

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

        return sources.ToArray();
    }
}
