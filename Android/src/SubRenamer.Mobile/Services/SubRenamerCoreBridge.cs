using SubRenamer.Core;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Services;

public sealed class SubRenamerCoreBridge
{
    public Task<IReadOnlyList<CoreMatchRow>> MatchAsync(
        IReadOnlyList<string> videoNames,
        IReadOnlyList<string> subtitleNames,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var input = videoNames
            .Select(name => new MatchItem("", name, ""))
            .Concat(subtitleNames.Select(name => new MatchItem("", "", name)))
            .ToArray();

        var result = Matcher.Execute(input)
            .Select(x => new CoreMatchRow(x.Key, x.Video, x.Subtitle))
            .ToArray();

        return Task.FromResult<IReadOnlyList<CoreMatchRow>>(result);
    }
}
