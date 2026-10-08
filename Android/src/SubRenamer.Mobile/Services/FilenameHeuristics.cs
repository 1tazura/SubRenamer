using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SubRenamer.Mobile.Services;

public static partial class FilenameHeuristics
{
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "1080p", "1080", "720p", "720", "2160p", "2160", "4k",
        "webrip", "webdl", "web", "bluray", "bdrip", "bd", "hdtv",
        "x264", "h264", "avc", "x265", "h265", "hevc", "av1",
        "aac", "flac", "opus", "ddp", "ac3", "hdr", "sdr", "10bit",
        "chs", "cht", "sc", "tc", "zh", "zho", "eng", "en", "jpn", "ja",
        "简", "繁", "简中", "繁中", "中字", "字幕"
    };

    [GeneratedRegex(@"(?i)\bS\d{1,3}E\d{1,4}\b")]
    private static partial Regex SeasonEpisodeRegex();

    [GeneratedRegex(@"(?i)\bS(?:eason)?[ ._-]?\d{1,3}\b")]
    private static partial Regex SeasonRegex();

    [GeneratedRegex(@"(?i)(?:^|[\s._\-\[\(\{])(?:EP?|第)?0*(\d{1,4})(?:集|话|話)?(?=$|[\s._\-\]\)\}])")]
    private static partial Regex LooseEpisodeRegex();

    [GeneratedRegex(@"[\p{L}\p{N}\p{IsCJKUnifiedIdeographs}]+")]
    private static partial Regex TokenRegex();

    // Subtitle groups very commonly write language markers as [CHS], [CHT],
    // (CHS), etc. Brackets must therefore count as token boundaries just like
    // dots, spaces, underscores and hyphens.
    [GeneratedRegex(@"(?i)(?:^|[._\-\s\[\(\{])(zh-hans|zh-hant|chs|cht|sc|tc|zho|eng|en|jpn|ja)(?=$|[._\-\s\]\)\}])")]
    private static partial Regex LanguageRegex();

    public static IReadOnlyList<string> Tokens(string? text, bool stripEpisodes = true)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var s = Path.GetFileNameWithoutExtension(text).Normalize(NormalizationForm.FormKC);
        if (stripEpisodes)
        {
            s = SeasonEpisodeRegex().Replace(s, " ");
            s = LooseEpisodeRegex().Replace(s, " ");
        }

        var list = new List<string>();
        foreach (Match m in TokenRegex().Matches(s))
        {
            var token = m.Value.Trim().ToLowerInvariant();
            if (token.Length < 2 || NoiseTokens.Contains(token))
                continue;
            if (token.All(char.IsDigit))
                continue;
            list.Add(token);
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlySet<string> CommonTokens(IEnumerable<string> names, double requiredFraction = 0.6)
    {
        var tokenLists = names.Select(x => Tokens(x).ToHashSet(StringComparer.OrdinalIgnoreCase)).ToArray();
        if (tokenLists.Length == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in tokenLists)
        {
            foreach (var token in set)
                counts[token] = counts.GetValueOrDefault(token) + 1;
        }

        var required = Math.Max(1, (int)Math.Ceiling(tokenLists.Length * requiredFraction));
        return counts.Where(x => x.Value >= required)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<int> EpisodeNumbers(IEnumerable<string> names)
    {
        var output = new HashSet<int>();
        foreach (var name in names)
        {
            var stem = Path.GetFileNameWithoutExtension(name);

            foreach (Match m in SeasonEpisodeRegex().Matches(stem))
            {
                var e = Regex.Match(m.Value, @"(?i)E(\d{1,4})");
                if (e.Success && int.TryParse(e.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    output.Add(n);
            }

            foreach (Match m in LooseEpisodeRegex().Matches(stem))
            {
                if (int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                    && n is > 0 and < 500)
                    output.Add(n);
            }
        }
        return output;
    }

    public static string LooseClusterKey(string fileName)
    {
        var tokens = Tokens(fileName);
        return tokens.Count == 0
            ? Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant()
            : string.Join("|", tokens.Order(StringComparer.OrdinalIgnoreCase));
    }

    public static string? LanguageTag(string subtitleName)
    {
        var stem = Path.GetFileNameWithoutExtension(subtitleName);
        var matches = LanguageRegex().Matches(stem);
        if (matches.Count == 0)
            return null;

        var tag = matches[^1].Groups[1].Value.ToLowerInvariant();
        return tag switch
        {
            "chs" or "sc" or "zh-hans" => "chs",
            "cht" or "tc" or "zh-hant" => "cht",
            "zho" or "zh" => "zh",
            "eng" or "en" => "en",
            "jpn" or "ja" => "ja",
            _ => tag,
        };
    }

    public static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return 0;

        var intersection = a.Count(x => b.Contains(x));
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }
}
