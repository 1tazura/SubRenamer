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
}
