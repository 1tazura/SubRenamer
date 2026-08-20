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
            TimeSpan.FromMilliseconds(15),
            4,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(60),
            TimeSpan.FromMilliseconds(70),
            TimeSpan.FromMilliseconds(280),
            123456);

        var text = metrics.ToString();

        Assert.That(text, Does.Contain("目标目录复查 10 ms"));
        Assert.That(text, Does.Contain("源准备 20 ms"));
        Assert.That(text, Does.Contain("目标创建累计 30 ms"));
        Assert.That(text, Does.Contain("目标打开累计 40 ms"));
        Assert.That(text, Does.Contain("等待目标就绪 15 ms（并发 4）"));
        Assert.That(text, Does.Contain("源打开 50 ms"));
        Assert.That(text, Does.Contain("传输/解压/SHA 60 ms"));
        Assert.That(text, Does.Contain("目标关闭 70 ms"));
        Assert.That(text, Does.Contain("Apply 总计 280 ms"));
        Assert.That(text, Does.Contain("123456 B"));
    }

    [Test]
    public void Destination_preparation_metrics_are_explicitly_cumulative()
    {
        var metrics = new ApplyPerformance(
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(6000),
            TimeSpan.FromMilliseconds(1600),
            TimeSpan.FromMilliseconds(900),
            4,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(2500),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(4000),
            22000000);

        var text = metrics.ToString();

        Assert.That(text, Does.Contain("目标创建累计 6000 ms"));
        Assert.That(text, Does.Contain("目标打开累计 1600 ms"));
        Assert.That(text, Does.Contain("等待目标就绪 900 ms（并发 4）"));
        Assert.That(text, Does.Contain("Apply 总计 4000 ms"));
    }
}
