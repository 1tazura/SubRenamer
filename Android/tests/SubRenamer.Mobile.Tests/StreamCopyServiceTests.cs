using System.Text;
using NUnit.Framework;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile.Tests;

[TestFixture]
public sealed class StreamCopyServiceTests
{
    [Test]
    public async Task Copy_fingerprint_matches_written_bytes()
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        await using var destination = new MemoryStream();

        var result = await StreamCopyService.CopyWithSha256Async(source, destination);

        Assert.That(result.Length, Is.EqualTo(3));
        Assert.That(result.Sha256, Is.EqualTo(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
        Assert.That(Encoding.UTF8.GetString(destination.ToArray()), Is.EqualTo("abc"));
    }
}
