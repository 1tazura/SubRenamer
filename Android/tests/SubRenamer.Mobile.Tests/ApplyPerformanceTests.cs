using NUnit.Framework;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile.Tests;

[TestFixture]
public sealed class ApplyPerformanceTests
{
    [Test]
    public void Apply_performance_text_contains_all_major_io_phases()
    {
        var metrics = new ApplyPerformance(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(60),
            TimeSpan.FromMilliseconds(70),
            TimeSpan.FromMilliseconds(280),
            123456);

        var text = metrics.ToString();

        Assert.That(text, Does.Contain("目标目录复查 10 ms"));
        Assert.That(text, Does.Contain("源准备 20 ms"));
        Assert.That(text, Does.Contain("目标创建 30 ms"));
        Assert.That(text, Does.Contain("目标打开 40 ms"));
        Assert.That(text, Does.Contain("源打开 50 ms"));
        Assert.That(text, Does.Contain("传输/解压/SHA 60 ms"));
        Assert.That(text, Does.Contain("目标关闭 70 ms"));
        Assert.That(text, Does.Contain("Apply 总计 280 ms"));
        Assert.That(text, Does.Contain("123456 B"));
    }
}
