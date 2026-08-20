using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SubRenamer.Mobile.Services;

public sealed class StorageAccessService(SettingsStore settingsStore)
{
    public async Task<IStorageFolder?> RestoreDownloadAsync(
        Control owner,
        CancellationToken cancellationToken = default)
    {
        var topLevel = TopLevel.GetTopLevel(owner)
                       ?? throw new InvalidOperationException("Storage UI is not attached to a TopLevel.");
        var provider = topLevel.StorageProvider;
        var settings = await settingsStore.LoadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(settings.DownloadBookmark))
            return null;

        try
        {
            return await provider.OpenFolderBookmarkAsync(settings.DownloadBookmark);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IStorageFolder?> PickDownloadAsync(
        Control owner,
        CancellationToken cancellationToken = default)
    {
        var topLevel = TopLevel.GetTopLevel(owner)
                       ?? throw new InvalidOperationException("Storage UI is not attached to a TopLevel.");
        var provider = topLevel.StorageProvider;

        if (!provider.CanPickFolder)
            throw new NotSupportedException("This platform cannot pick folders.");

        var picked = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 /storage/emulated/0/Download",
            AllowMultiple = false,
        });

        var folder = picked.FirstOrDefault();
        if (folder is null)
            return null;

        if (folder.CanBookmark)
        {
            var bookmark = await folder.SaveBookmarkAsync();
            if (!string.IsNullOrWhiteSpace(bookmark))
                await settingsStore.SaveAsync(new AppSettings(bookmark), cancellationToken);
        }

        return folder;
    }

    public static async Task<IStorageFolder?> FindChildFolderAsync(
        IStorageFolder parent,
        string name,
        CancellationToken cancellationToken = default)
    {
        await foreach (var item in parent.GetItemsAsync().WithCancellation(cancellationToken))
        {
            if (item is IStorageFolder folder &&
                string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase))
                return folder;
        }
        return null;
    }

    public static async Task<IStorageFile?> FindChildFileAsync(
        IStorageFolder parent,
        string name,
        CancellationToken cancellationToken = default)
    {
        await foreach (var item in parent.GetItemsAsync().WithCancellation(cancellationToken))
        {
            if (item is IStorageFile file &&
                string.Equals(file.Name, name, StringComparison.Ordinal))
                return file;
        }
        return null;
    }

    public static async Task<HashSet<string>> SnapshotChildFileNamesAsync(
        IStorageFolder parent,
        CancellationToken cancellationToken = default)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var item in parent.GetItemsAsync().WithCancellation(cancellationToken))
        {
            if (item is IStorageFile file)
                names.Add(file.Name);
        }
        return names;
    }
}
