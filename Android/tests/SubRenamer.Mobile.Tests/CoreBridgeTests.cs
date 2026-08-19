using NUnit.Framework;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile.Tests;

[TestFixture]
public sealed class CoreBridgeTests
{
    [Test]
    public async Task Bridge_uses_original_matcher_for_episode_mapping()
    {
        var bridge = new SubRenamerCoreBridge();
        var result = await bridge.MatchAsync(
            ["abc.S02E01.123.mkv", "abc.S02E02.abc.mkv", "abc.S02E03.ccc.mkv"],
            ["[SubGroup] def.S02E01.xyz.ass", "[SubGroup] def.S02E02.abc.ass", "[SubGroup] def.S02E04.kkk.ass"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Any(x => x.Key == "1" && x.Video.Contains("E01") && x.Subtitle.Contains("E01")), Is.True);
            Assert.That(result.Any(x => x.Key == "2" && x.Video.Contains("E02") && x.Subtitle.Contains("E02")), Is.True);
            Assert.That(result.Any(x => x.Key == "3" && x.Video.Contains("E03") && string.IsNullOrEmpty(x.Subtitle)), Is.True);
            Assert.That(result.Any(x => x.Key == "4" && string.IsNullOrEmpty(x.Video) && x.Subtitle.Contains("E04")), Is.True);
        });
    }
}
