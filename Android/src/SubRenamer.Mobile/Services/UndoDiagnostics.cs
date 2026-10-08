namespace SubRenamer.Mobile.Services;

public sealed record UndoPerformance(
    TimeSpan TargetResolveElapsed,
    TimeSpan DirectorySnapshotElapsed,
    TimeSpan FileOpenElapsed,
    TimeSpan HashElapsed,
    TimeSpan DeleteElapsed,
    TimeSpan JournalElapsed,
    TimeSpan TotalElapsed,
    long BytesHashed,
    int Concurrency)
{
    public override string ToString()
        => $"撤销性能：目标定位 {TargetResolveElapsed.TotalMilliseconds:F0} ms；" +
           $"目录快照 {DirectorySnapshotElapsed.TotalMilliseconds:F0} ms；" +
           $"文件打开累计 {FileOpenElapsed.TotalMilliseconds:F0} ms；" +
           $"SHA 校验累计 {HashElapsed.TotalMilliseconds:F0} ms；" +
           $"删除累计 {DeleteElapsed.TotalMilliseconds:F0} ms；" +
           $"哈希 {BytesHashed} B；日志清理 {JournalElapsed.TotalMilliseconds:F0} ms；" +
           $"并发 {Concurrency}；Undo 总计 {TotalElapsed.TotalMilliseconds:F0} ms";
}
