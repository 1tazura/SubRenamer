using Avalonia.Platform.Storage;

namespace SubRenamer.Mobile.Models;

public enum SubtitleSourceKind
{
    Archive,
    LooseGroup,
}

public sealed record SubtitleEntryRef(
    string Key,
    string DisplayName,
    string Extension,
    long? Size = null);

public sealed record SubtitleSource(
    SubtitleSourceKind Kind,
    string DisplayName,
    IStorageFile? ArchiveFile,
    IReadOnlyList<IStorageFile> LooseFiles,
    IReadOnlyList<SubtitleEntryRef> Entries,
    bool IsIndexed = true,
    double DiscoveryAffinity = 0)
{
    public int SubtitleCount => Entries.Count;
}
