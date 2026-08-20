using NUnit.Framework;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile.Tests;

[TestFixture]
public sealed class FilenameHeuristicsTests
{
    [Test]
    public void Episode_numbers_are_auxiliary_only_but_detectable()
    {
        var numbers = FilenameHeuristics.EpisodeNumbers([
            "[Group] Title - 01.ass",
            "[Group] Title - 02.ass",
            "[Group] Title - 03.ass"]);

        Assert.That(numbers, Is.EquivalentTo(new[] { 1, 2, 3 }));
    }

    [TestCase("[Sakurato] Mushoku Tensei S2 [00][CHS].ass", "chs")]
    [TestCase("[Sakurato] Mushoku Tensei S2 [00][CHT].ass", "cht")]
    [TestCase("[Sakurato] Mushoku Tensei S2 [01][CHS].Delay 1s (1000ms).ass", "chs")]
    [TestCase("Title.zh-Hant.ass", "cht")]
    [TestCase("Title (ENG).srt", "en")]
    public void Language_tags_accept_common_group_delimiters(string fileName, string expected)
    {
        Assert.That(FilenameHeuristics.LanguageTag(fileName), Is.EqualTo(expected));
    }
}
