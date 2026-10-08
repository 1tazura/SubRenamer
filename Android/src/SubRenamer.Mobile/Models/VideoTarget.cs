using IStorageFile = SubRenamer.Mobile.Services.IContentFile;
using IStorageFolder = SubRenamer.Mobile.Services.IContentFolder;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile.Models;

public sealed record VideoTarget(
    IStorageFolder Folder,
    string RelativePath,
    IReadOnlyList<IStorageFile> Videos,
    StorageRootIdentity? RootIdentity = null)
{
    public string DisplayName => RelativePath;
    public int VideoCount => Videos.Count;
}
