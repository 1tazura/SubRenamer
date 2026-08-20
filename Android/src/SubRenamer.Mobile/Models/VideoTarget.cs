using Avalonia.Platform.Storage;

namespace SubRenamer.Mobile.Models;

public sealed record VideoTarget(
    IStorageFolder Folder,
    string RelativePath,
    IReadOnlyList<IStorageFile> Videos)
{
    public string DisplayName => RelativePath;
    public int VideoCount => Videos.Count;
}
