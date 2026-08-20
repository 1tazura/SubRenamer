using System.Text.RegularExpressions;
using SubRenamer.Core;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public enum CoreMatchMode
{
    Diff,
    Manual,
    Regex,
}

public sealed record CoreMatchSettings(
    CoreMatchMode Mode = CoreMatchMode.Diff,
    string VideoRule = "",
    string SubtitleRule = "");

public sealed class SubRenamerCoreBridge
{
    public Task<IReadOnlyList<CoreMatchRow>> MatchAsync(
        IReadOnlyList<string> videoNames,
        IReadOnlyList<string> subtitleNames,
        CoreMatchSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var input = videoNames
            .Select(name => new MatchItem("", name, ""))
            .Concat(subtitleNames.Select(name => new MatchItem("", "", name)))
            .ToArray();

        var options = BuildMatcherOptions(settings ?? new CoreMatchSettings());
        var result = Matcher.Execute(input, options)
            .Select(x => new CoreMatchRow(x.Key, x.Video, x.Subtitle))
            .ToArray();

        return Task.FromResult<IReadOnlyList<CoreMatchRow>>(result);
    }

    public static MatcherOptions BuildMatcherOptions(CoreMatchSettings settings)
    {
        return settings.Mode switch
        {
            CoreMatchMode.Diff => new MatcherOptions(),
            CoreMatchMode.Manual => new MatcherOptions
            {
                VideoRegex = ManualPatternToRegex(settings.VideoRule, "视频"),
                SubtitleRegex = ManualPatternToRegex(settings.SubtitleRule, "字幕"),
            },
            CoreMatchMode.Regex => new MatcherOptions
            {
                VideoRegex = ValidateRegex(settings.VideoRule, "视频"),
                SubtitleRegex = ValidateRegex(settings.SubtitleRule, "字幕"),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(settings.Mode), settings.Mode, null),
        };
    }

    public static string ManualPatternToRegex(string pattern, string kind = "文件")
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new InvalidOperationException($"{kind}手动规则不能为空。");
        if (!pattern.Contains("$$", StringComparison.Ordinal))
            throw new InvalidOperationException($"{kind}手动规则必须包含 $$，它表示要提取的集数/匹配键。");

        // Keep the desktop editor semantics: $$ is capture group 1 and * is a
        // non-greedy wildcard. Everything else is treated literally.
        return Regex.Escape(pattern)
            .Replace(@"\$\$", @"(.+?)")
            .Replace(@"\*", @".*?");
    }

    private static string ValidateRegex(string pattern, string kind)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new InvalidOperationException($"{kind}正则不能为空。");

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"{kind}正则无效：{ex.Message}", ex);
        }

        if (!regex.GetGroupNumbers().Contains(1))
            throw new InvalidOperationException($"{kind}正则必须包含捕获组 1，例如 (\\d+)；SubRenamer.Core 使用第一个捕获组作为匹配键。");

        return pattern;
    }
}
