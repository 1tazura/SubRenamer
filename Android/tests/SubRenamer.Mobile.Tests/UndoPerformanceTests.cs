using NUnit.Framework;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile.Tests;

[TestFixture]
public sealed class UndoPerformanceTests
{
    [Test]
    public void Undo_performance_text_contains_parallel_io_phases()
    {
        var metrics = new UndoPerformance(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(60),
            TimeSpan.FromMilliseconds(100),
            123456,
            4);

        var text = metrics.ToString();

        Assert.That(text, Does.Contain("目标定位 10 ms"));
        Assert.That(text, Does.Contain("目录快照 20 ms"));
        Assert.That(text, Does.Contain("文件打开累计 30 ms"));
        Assert.That(text, Does.Contain("SHA 校验累计 40 ms"));
        Assert.That(text, Does.Contain("删除累计 50 ms"));
        Assert.That(text, Does.Contain("日志清理 60 ms"));
        Assert.That(text, Does.Contain("123456 B"));
        Assert.That(text, Does.Contain("并发 4"));
        Assert.That(text, Does.Contain("Undo 总计 100 ms"));
    }
}
