using Avalonia.Platform.Storage;
using IStorageItem = SubRenamer.Mobile.Services.IContentItem;
using IStorageFile = SubRenamer.Mobile.Services.IContentFile;
using IStorageFolder = SubRenamer.Mobile.Services.IContentFolder;

namespace SubRenamer.Mobile.Services;

public abstract class SmbStorageItem : IStorageItem
{
    internal readonly SmbConnection Connection;
    internal readonly string SharePath;
    internal readonly SmbNative.Stat Metadata;
    internal SmbStorageItem(SmbConnection connection, string path, SmbNative.Stat metadata)
    { Connection = connection; SharePath = path; Metadata = metadata; }
    public string Name => SharePath.Length == 0 ? Connection.Location.Share : SharePath.Split('/').Last();
    public Uri Path => Connection.ItemUri(SharePath, Metadata.Identity);
    public bool CanBookmark => false;
    public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>(null);
    public Task<StorageItemProperties> GetBasicPropertiesAsync() => Task.FromResult(new StorageItemProperties(Metadata.Size, null, null));
    public Task<IStorageFolder?> GetParentAsync() => Task.FromResult<IStorageFolder?>(null);
    public virtual Task DeleteAsync() => throw new NotSupportedException("网络删除仅允许同句柄 SHA 校验撤销。");
    public Task<IStorageItem?> MoveAsync(IStorageFolder destination) => throw new NotSupportedException("网络文件不允许移动或重命名。");
    public void Dispose() { } // metadata object; the session owns the connection
}

public sealed class SmbStorageFolder : SmbStorageItem, IStorageFolder, IIdentifiedStorageFolder
{
    internal SmbStorageFolder(SmbConnection connection, string path, SmbNative.Stat metadata) : base(connection, path, metadata) { }
    public StorageRootIdentity RootIdentity => Connection.RootIdentity;
    public async IAsyncEnumerable<IStorageItem> GetItemsAsync()
    {
        var items = await Task.Run(() => Connection.List(SharePath, Metadata.Identity));
        foreach (var item in items) yield return item;
    }
    public async Task<IStorageFolder?> GetFolderAsync(string name)
        => (await StorageAccessService.FindChildFolderAsync(this, name));
    public async Task<IStorageFile?> GetFileAsync(string name)
        => (await StorageAccessService.FindChildFileAsync(this, name));
    public Task<IStorageFile?> CreateFileAsync(string name) => throw new NotSupportedException("SMB 必须使用排他字幕创建接口。");
    public Task<IStorageFolder?> CreateFolderAsync(string name) => throw new NotSupportedException("不在媒体目录创建组织子目录。");
    public SmbHandleStream CreateSubtitleExclusive(string name)
    {
        SmbLocation.ValidateName(name);
        if (!ScanService.SubtitleExtensions.Contains(System.IO.Path.GetExtension(name)))
            throw new NotSupportedException("网络输出只允许字幕扩展名。");
        var path = SharePath.Length == 0 ? name : SharePath + "/" + name;
        return Connection.Open(path, SharePath, Metadata.Identity, 1);
    }
}

public sealed class SmbStorageFile : SmbStorageItem
    , IStorageFile
{
    private readonly string _folderPath, _folderIdentity;
    internal SmbStorageFile(SmbConnection connection, string path, SmbNative.Stat metadata, string folderPath, string folderIdentity)
        : base(connection, path, metadata) { _folderPath = folderPath; _folderIdentity = folderIdentity; }
    public Task<Stream> OpenReadAsync()
    {
        if (!ScanService.SubtitleExtensions.Contains(System.IO.Path.GetExtension(Name)))
            throw new NotSupportedException("网络视频仅读取名称及元数据，不打开片源字节。");
        return Task.FromResult<Stream>(Connection.Open(SharePath, _folderPath, _folderIdentity, 0));
    }
    public Task<Stream> OpenWriteAsync() => throw new NotSupportedException("不允许打开既有网络文件写入。");
    public async Task<(bool Deleted, CopyFingerprint Fingerprint)> DeleteIfUnchangedAsync(string sha256, string? fileIdentity, CancellationToken token)
    {
        if (!ScanService.SubtitleExtensions.Contains(System.IO.Path.GetExtension(Name)))
            throw new NotSupportedException("撤销不允许删除非字幕文件。");
        // Read + DELETE, share_access=0: hash and delete the same locked object,
        // never close then unlink by path. Other writers/replacers cannot race us.
        await using var stream = Connection.Open(SharePath, _folderPath, _folderIdentity, 2);
        var fingerprint = await StreamCopyService.ComputeSha256Async(stream, token);
        if ((fileIdentity is not null && fileIdentity != stream.FileIdentity) ||
            !string.Equals(fingerprint.Sha256, sha256, StringComparison.OrdinalIgnoreCase)) return (false, fingerprint);
        token.ThrowIfCancellationRequested();
        stream.MarkForDeletion();
        await stream.DisposeAsync();
        return (true, fingerprint);
    }
}

public sealed class SmbHandleStream : Stream
{
    private readonly SmbConnection _connection;
    private IntPtr _handle;
    private readonly int _mode;
    private long _position;
    public bool CommitAttempted { get; private set; }
    public string FileIdentity => _connection.Locked(context =>
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        _connection.Check(SmbNative.sr_stat_handle(context, _handle, out var stat), "文件身份");
        return stat.Identity;
    });
    internal SmbHandleStream(SmbConnection connection, IntPtr handle, int mode)
    { _connection = connection; _handle = handle; _mode = mode; }
    public override bool CanRead => _handle != IntPtr.Zero;
    public override bool CanWrite => _handle != IntPtr.Zero && _mode == 1;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        var temporary = new byte[Math.Min(count, 64 * 1024)];
        var read = _connection.Locked(context => SmbNative.sr_read(context, _handle, temporary, (uint)temporary.Length, (ulong)_position));
        _connection.Check(read, "读取字幕");
        Buffer.BlockCopy(temporary, 0, buffer, offset, read); _position += read; return read;
    }
    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        if (!CanWrite) throw new NotSupportedException();
        while (count > 0)
        {
            var temporary = new byte[Math.Min(count, 64 * 1024)];
            Buffer.BlockCopy(buffer, offset, temporary, 0, temporary.Length);
            var written = _connection.Locked(context => SmbNative.sr_write(context, _handle, temporary, (uint)temporary.Length, (ulong)_position));
            _connection.Check(written, "写入字幕");
            if (written == 0) throw new IOException("SMB 返回零字节写入。");
            offset += written; count -= written; _position += written;
        }
    }
    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        _connection.Locked(context => { _connection.Check(SmbNative.sr_flush(context, _handle), "刷新字幕"); return 0; });
    }
    public void Commit()
    {
        if (_mode != 1) throw new InvalidOperationException();
        Flush();
        // A lost reply after clearing delete-on-close is ambiguous. The caller
        // retains a SHA journal entry even on a commit/close error, never blindly
        // deletes a new file by path. Undo verifies it after reconnect/restart.
        CommitAttempted = true;
        _connection.Locked(context => { _connection.Check(SmbNative.sr_disposition(context, _handle, 0), "提交字幕"); return 0; });
        Dispose();
    }
    internal void MarkForDeletion() => _connection.Locked(context =>
    { _connection.Check(SmbNative.sr_disposition(context, _handle, 1), "同句柄删除字幕"); return 0; });
    protected override void Dispose(bool disposing)
    {
        if (_handle != IntPtr.Zero)
        {
            var handle = _handle; _handle = IntPtr.Zero;
            _connection.Locked(context => { _connection.Check(SmbNative.sr_close(context, handle), "关闭字幕"); return 0; });
        }
        base.Dispose(disposing);
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
