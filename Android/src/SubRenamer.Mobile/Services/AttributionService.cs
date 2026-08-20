using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed record AttributionResult(
    IReadOnlyList<AttributionCandidate> Candidates,
    AttributionCandidate? AutoSelected);

public sealed class AttributionService
{
    public AttributionResult Rank(SubtitleSource source, IReadOnlyList<VideoTarget> targets)
    {
        var sourceArchiveTokens = FilenameHeuristics.Tokens(source.DisplayName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceCommon = FilenameHeuristics.CommonTokens(source.Entries.Select(x => x.DisplayName));
        var sourceEpisodes = FilenameHeuristics.EpisodeNumbers(source.Entries.Select(x => x.DisplayName));

        var candidates = new List<AttributionCandidate>();

        foreach (var target in targets)
        {
            var videoNames = target.Videos.Select(StorageAccessService.GetDisplayNameFast).ToArray();
            var folderTokens = FilenameHeuristics.Tokens(Path.GetFileName(target.RelativePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var videoCommon = FilenameHeuristics.CommonTokens(videoNames);
            var targetEpisodes = FilenameHeuristics.EpisodeNumbers(videoNames);

            var titleScore = FilenameHeuristics.Jaccard(sourceArchiveTokens, folderTokens);
            var commonScore = FilenameHeuristics.Jaccard(sourceCommon, videoCommon);
            var episodeScore = Overlap(sourceEpisodes, targetEpisodes);

            var score = titleScore * 0.55 + commonScore * 0.30 + episodeScore * 0.15;

            var evidence = new List<string>();
            if (titleScore > 0)
                evidence.Add($"来源名↔目录名 {titleScore:P0}");
            if (commonScore > 0)
                evidence.Add($"包内名↔视频名 {commonScore:P0}");
            if (episodeScore > 0)
                evidence.Add($"集数重合 {episodeScore:P0}（仅辅助）");

            candidates.Add(new AttributionCandidate(target, score, evidence));
        }

        var ranked = candidates
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AttributionCandidate? auto = null;
        if (ranked.Length > 0)
        {
            var first = ranked[0];
            var margin = ranked.Length == 1 ? 1 : first.Score - ranked[1].Score;
            if (first.Score >= 0.42 && margin >= 0.10)
                auto = first;
        }

        return new AttributionResult(ranked, auto);
    }

    private static double Overlap(IReadOnlySet<int> a, IReadOnlySet<int> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return 0;
        var intersection = a.Count(x => b.Contains(x));
        return (double)intersection / Math.Max(a.Count, b.Count);
    }
}
