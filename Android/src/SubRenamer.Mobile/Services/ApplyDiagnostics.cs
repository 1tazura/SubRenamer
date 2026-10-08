namespace SubRenamer.Mobile.Services;

public sealed record ApplyPerformance(
    TimeSpan DirectorySnapshotElapsed,
    TimeSpan SourcePreparationElapsed,
    TimeSpan DestinationCreateElapsed,
    TimeSpan DestinationOpenElapsed,
    TimeSpan DestinationReadyWaitElapsed,
    int DestinationPreparationConcurrency,
    TimeSpan SourceOpenElapsed,
    TimeSpan TransferElapsed,
    TimeSpan DestinationCloseElapsed,
    TimeSpan TotalElapsed,
    long BytesWritten)
{
    public override string ToString()
        => $"处理性能：目标目录复查 {DirectorySnapshotElapsed.TotalMilliseconds:F0} ms；" +
           $"源准备 {SourcePreparationElapsed.TotalMilliseconds:F0} ms；" +
           $"目标创建累计 {DestinationCreateElapsed.TotalMilliseconds:F0} ms；" +
           $"目标打开累计 {DestinationOpenElapsed.TotalMilliseconds:F0} ms；" +
           $"等待目标就绪 {DestinationReadyWaitElapsed.TotalMilliseconds:F0} ms（并发 {DestinationPreparationConcurrency}）；" +
           $"源打开 {SourceOpenElapsed.TotalMilliseconds:F0} ms；" +
           $"传输/解压/SHA {TransferElapsed.TotalMilliseconds:F0} ms；" +
           $"目标关闭 {DestinationCloseElapsed.TotalMilliseconds:F0} ms；" +
           $"写入 {BytesWritten} B；Apply 总计 {TotalElapsed.TotalMilliseconds:F0} ms";
}
