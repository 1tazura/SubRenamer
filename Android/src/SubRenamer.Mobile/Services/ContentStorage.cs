using Avalonia.Platform.Storage;

namespace SubRenamer.Mobile.Services;

// Avalonia 12 storage interfaces are intentionally not client-implementable.
// Keep this narrow app-owned boundary; SAF delegates to authorized handles,
// while network implementations never need to impersonate a DocumentsProvider.
public interface IContentItem : IDisposable
{
    string Name { get; }
    Uri Path { get; }
    bool CanBookmark { get; }
    Task<string?> SaveBookmarkAsync();
    Task<StorageItemProperties> GetBasicPropertiesAsync();
    Task<IContentFolder?> GetParentAsync();
    Task DeleteAsync();
    Task<IContentItem?> MoveAsync(IContentFolder destination);
}
public interface IContentFile : IContentItem
{
    Task<Stream> OpenReadAsync();
    Task<Stream> OpenWriteAsync();
}
public interface IContentFolder : IContentItem
{
    IAsyncEnumerable<IContentItem> GetItemsAsync();
    Task<IContentFile?> GetFileAsync(string name);
    Task<IContentFolder?> GetFolderAsync(string name);
    Task<IContentFile?> CreateFileAsync(string name);
    Task<IContentFolder?> CreateFolderAsync(string name);
}

internal abstract class SafContentItem(IStorageItem inner) : IContentItem
{
    protected readonly IStorageItem Inner = inner;
    public string Name => Inner.Name;
    public Uri Path => Inner.Path;
    public bool CanBookmark => Inner.CanBookmark;
    public Task<string?> SaveBookmarkAsync() => Inner.SaveBookmarkAsync();
    public Task<StorageItemProperties> GetBasicPropertiesAsync() => Inner.GetBasicPropertiesAsync();
    public async Task<IContentFolder?> GetParentAsync()
        => await Inner.GetParentAsync() is { } parent ? new SafContentFolder(parent) : null;
    public Task DeleteAsync() => Inner.DeleteAsync();
    public async Task<IContentItem?> MoveAsync(IContentFolder destination)
    {
        if (destination is not SafContentFolder folder) throw new NotSupportedException();
        var moved = await Inner.MoveAsync((IStorageFolder)folder.Inner);
        return moved is null ? null : Wrap(moved);
    }
    internal static IContentItem Wrap(IStorageItem item) => item switch
    {
        IStorageFolder folder => new SafContentFolder(folder),
        IStorageFile file => new SafContentFile(file),
        _ => throw new NotSupportedException(),
    };
    public void Dispose() => Inner.Dispose();
}
internal sealed class SafContentFolder(IStorageFolder inner) : SafContentItem(inner), IContentFolder
{
    public async IAsyncEnumerable<IContentItem> GetItemsAsync()
    { await foreach (var item in inner.GetItemsAsync()) yield return Wrap(item); }
    public async Task<IContentFile?> GetFileAsync(string name)
        => await inner.GetFileAsync(name) is { } file ? new SafContentFile(file) : null;
    public async Task<IContentFolder?> GetFolderAsync(string name)
        => await inner.GetFolderAsync(name) is { } folder ? new SafContentFolder(folder) : null;
    public async Task<IContentFile?> CreateFileAsync(string name)
        => await inner.CreateFileAsync(name) is { } file ? new SafContentFile(file) : null;
    public async Task<IContentFolder?> CreateFolderAsync(string name)
        => await inner.CreateFolderAsync(name) is { } folder ? new SafContentFolder(folder) : null;
}
internal sealed class SafContentFile(IStorageFile inner) : SafContentItem(inner), IContentFile
{
    public Task<Stream> OpenReadAsync() => inner.OpenReadAsync();
    public Task<Stream> OpenWriteAsync() => inner.OpenWriteAsync();
}
