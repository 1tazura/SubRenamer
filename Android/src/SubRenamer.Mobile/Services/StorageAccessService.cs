using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SubRenamer.Mobile.Services;

public sealed class StorageAccessService(SettingsStore settingsStore)
{
    private const string ExternalStorageDocumentsAuthority = "com.android.externalstorage.documents";

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
                await settingsStore.SaveDownloadBookmarkAsync(bookmark, cancellationToken);
        }

        return folder;
    }

    /// <summary>
    /// Avalonia Android's GetItemsAsync cursor already contains the document id,
    /// but IStorageItem.Name performs another ContentResolver query per item.
    /// ExternalStorageProvider document ids contain the path/name, so derive the
    /// display name from IStorageItem.Path when possible and fall back to Name on
    /// other providers/platforms.
    /// </summary>
    public static string GetDisplayNameFast(IStorageItem item)
    {
        var parsed = TryGetExternalStorageDocumentName(item.Path);
        return string.IsNullOrEmpty(parsed) ? item.Name : parsed;
    }

    public static string? TryGetExternalStorageDocumentName(Uri uri)
    {
        if (!uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, ExternalStorageDocumentsAuthority, StringComparison.OrdinalIgnoreCase))
            return null;

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        catch
        {
            return null;
        }

        const string marker = "/document/";
        var markerIndex = decoded.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return null;

        var documentId = decoded[(markerIndex + marker.Length)..].TrimEnd('/');
        if (documentId.Length == 0)
            return null;

        var slash = documentId.LastIndexOf('/');
        if (slash >= 0 && slash < documentId.Length - 1)
            return documentId[(slash + 1)..];

        // Tree/document roots such as primary:Download have no slash after the
        // volume separator. This branch is mostly useful for folder lookups.
        var colon = documentId.LastIndexOf(':');
        if (colon >= 0 && colon < documentId.Length - 1)
            return documentId[(colon + 1)..];

        return null;
    }

    public static async Task<IStorageFolder?> FindChildFolderAsync(
        IStorageFolder parent,
        string name,
        CancellationToken cancellationToken = default)
    {
        await foreach (var item in parent.GetItemsAsync().WithCancellation(cancellationToken))
        {
            if (item is IStorageFolder folder &&
                string.Equals(GetDisplayNameFast(folder), name, StringComparison.OrdinalIgnoreCase))
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
                string.Equals(GetDisplayNameFast(file), name, StringComparison.Ordinal))
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
                names.Add(GetDisplayNameFast(file));
        }
        return names;
    }

    public static async Task<Dictionary<string, IStorageFile>> SnapshotChildFilesAsync(
        IStorageFolder parent,
        CancellationToken cancellationToken = default)
    {
        var files = new Dictionary<string, IStorageFile>(StringComparer.OrdinalIgnoreCase);
        await foreach (var item in parent.GetItemsAsync().WithCancellation(cancellationToken))
        {
            if (item is IStorageFile file)
                files.TryAdd(GetDisplayNameFast(file), file);
        }
        return files;
    }

    public static async Task<IStorageFolder?> ResolveRelativeFolderAsync(
        IStorageFolder downloadRoot,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        IStorageFolder current = downloadRoot;
        var segments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            var next = await FindChildFolderAsync(current, segment, cancellationToken);
            if (next is null)
                return null;
            current = next;
        }

        return current;
    }
}
