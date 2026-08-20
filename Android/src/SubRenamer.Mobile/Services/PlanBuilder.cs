using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed class PlanBuilder(SubRenamerCoreBridge bridge)
{
    public async Task<MatchPlan> BuildAsync(
        SubtitleSource source,
        VideoTarget target,
        CoreMatchSettings? matchSettings = null,
        CancellationToken cancellationToken = default)
    {
        var videoNames = target.Videos.Select(x => x.Name).ToArray();
        var subtitleNames = source.Entries.Select(x => x.DisplayName).ToArray();

        var rows = await bridge.MatchAsync(videoNames, subtitleNames, matchSettings, cancellationToken);
        var diagnostics = new List<string>();

        var matched = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Video) && !string.IsNullOrWhiteSpace(x.Subtitle))
            .ToArray();

        var sourceByDisplayName = source.Entries
            .GroupBy(x => x.DisplayName, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);

        // SAF directory enumeration is an IPC/query on Android. Snapshot once rather
        // than running a full folder query for every planned subtitle destination.
        var existingNames = await StorageAccessService.SnapshotChildFileNamesAsync(
            target.Folder, cancellationToken);

        var items = new List<MatchPlanItem>();

        foreach (var videoGroup in matched.GroupBy(x => x.Video, StringComparer.Ordinal))
        {
            var videoBase = Path.GetFileNameWithoutExtension(videoGroup.Key);
            var rowsForVideo = videoGroup.ToArray();
            var sameExtensionCounts = rowsForVideo
                .GroupBy(x => Path.GetExtension(x.Subtitle), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

            var usedDestinationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rowsForVideo)
            {
                if (!sourceByDisplayName.TryGetValue(row.Subtitle, out var sourceEntries) || sourceEntries.Length != 1)
                {
                    items.Add(new MatchPlanItem(
                        row.Key, row.Video, row.Subtitle, row.Subtitle, "",
                        PlanItemStatus.AmbiguousSource,
                        "压缩包内存在同名字幕，无法唯一定位源条目。"));
                    continue;
                }

                var entry = sourceEntries[0];
                var ext = entry.Extension;
                var destination = videoBase + ext;

                if (sameExtensionCounts.GetValueOrDefault(ext) > 1)
                {
                    var lang = FilenameHeuristics.LanguageTag(row.Subtitle);
                    if (string.IsNullOrWhiteSpace(lang))
                    {
                        items.Add(new MatchPlanItem(
                            row.Key, row.Video, entry.Key, entry.DisplayName, "",
                            PlanItemStatus.AmbiguousSource,
                            "同一视频匹配到多个相同扩展名字幕，但无法安全提取唯一语言后缀。"));
                        continue;
                    }
                    destination = $"{videoBase}.{lang}{ext}";
                }

                if (!usedDestinationNames.Add(destination))
                {
                    items.Add(new MatchPlanItem(
                        row.Key, row.Video, entry.Key, entry.DisplayName, destination,
                        PlanItemStatus.AmbiguousSource,
                        "多个字幕将写入同一目标文件名。"));
                    continue;
                }

                var exists = existingNames.Contains(destination);
                items.Add(new MatchPlanItem(
                    row.Key,
                    row.Video,
                    entry.Key,
                    entry.DisplayName,
                    destination,
                    exists ? PlanItemStatus.ExistingDestination : PlanItemStatus.Ready,
                    exists ? "目标字幕已存在；v1 不覆盖。" : null));
            }
        }

        var matchedSubs = matched.Select(x => x.Subtitle).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in source.Entries.Where(x => !matchedSubs.Contains(x.DisplayName)))
        {
            items.Add(new MatchPlanItem(
                "",
                "",
                entry.Key,
                entry.DisplayName,
                "",
                PlanItemStatus.Unmatched,
                "SubRenamer.Core 未将此字幕映射到视频。"));
        }

        return new MatchPlan(source, target, items, diagnostics);
    }
}
