namespace SubRenamer.Mobile.Services;

/// <summary>
/// Identifies archive-open failures that are deterministic for unchanged bytes
/// and can therefore be remembered by the archive index cache.
///
/// Keep this deliberately narrow. Permission, I/O, cancellation and other
/// potentially transient failures must be retried on the next scan.
/// </summary>
public static class ArchiveRejectionPolicy
{
    private const string UnsupportedStreamPrefix = "Cannot determine compressed stream type";

    public static bool IsStable(Exception exception)
        => IsStable(exception.GetType().Name, exception.Message);

    public static bool IsStable(string exceptionTypeName, string message)
        => string.Equals(exceptionTypeName, "ArchiveOperationException", StringComparison.Ordinal) &&
           message.StartsWith(UnsupportedStreamPrefix, StringComparison.OrdinalIgnoreCase);
}
