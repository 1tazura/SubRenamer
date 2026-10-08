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

    [Test]
    public async Task Bridge_passes_manual_rules_as_core_regex_options()
    {
        var bridge = new SubRenamerCoreBridge();
        var result = await bridge.MatchAsync(
            ["Show - 01 [1080p].mkv", "Show - 02 [1080p].mkv"],
            ["[Sub] Show 01 [CHS].ass", "[Sub] Show 02 [CHS].ass"],
            new CoreMatchSettings(
                CoreMatchMode.Manual,
                "Show - $$ *.mkv",
                "[Sub] Show $$ *.ass"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Any(x => x.Key == "1" && x.Video.Contains("01") && x.Subtitle.Contains("01")), Is.True);
            Assert.That(result.Any(x => x.Key == "2" && x.Video.Contains("02") && x.Subtitle.Contains("02")), Is.True);
        });
    }

    [Test]
    public async Task Bridge_passes_regex_rules_to_core()
    {
        var bridge = new SubRenamerCoreBridge();
        var result = await bridge.MatchAsync(
            ["show.S02E01.mkv", "show.S02E02.mkv"],
            ["[Sub] show.01.ass", "[Sub] show.02.ass"],
            new CoreMatchSettings(
                CoreMatchMode.Regex,
                @"S02E(\d+)",
                @"show\.(\d+)"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Any(x => x.Key == "1" && x.Video.Contains("E01") && x.Subtitle.Contains("01")), Is.True);
            Assert.That(result.Any(x => x.Key == "2" && x.Video.Contains("E02") && x.Subtitle.Contains("02")), Is.True);
        });
    }

    [Test]
    public void Manual_rule_requires_key_marker()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SubRenamerCoreBridge.ManualPatternToRegex("Show - *.mkv", "视频"));

        Assert.That(ex!.Message, Does.Contain("$$"));
    }
}
