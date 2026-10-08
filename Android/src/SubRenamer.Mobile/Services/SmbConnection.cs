using System.Runtime.InteropServices;
using IStorageItem = SubRenamer.Mobile.Services.IContentItem;

namespace SubRenamer.Mobile.Services;

public interface IIdentifiedStorageFolder
{
    StorageRootIdentity RootIdentity { get; }
}

public sealed record SmbLocation(string Host, string Share, string Directory)
{
    public string Url => $"smb://{Host}/{Uri.EscapeDataString(Share)}" +
                         (Directory.Length == 0 ? "" : "/" + string.Join('/', Directory.Split('/').Select(Uri.EscapeDataString)));

    public static SmbLocation Parse(string value)
    {
        value = value.Trim().Replace('\\', '/');
        if (value.StartsWith("//", StringComparison.Ordinal)) value = "smb:" + value;
        if (!value.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)) value = "smb://" + value;
        // Reject traversal before Uri normalizes it away.
        var authorityEnd = value.IndexOf('/', 6);
        var rawPath = authorityEnd < 0 ? "" : value[(authorityEnd + 1)..];
        var segments = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        foreach (var segment in segments) ValidateName(segment);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "smb" ||
            uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.IsDefaultPort || segments.Length == 0)
            throw new ArgumentException("请输入 smb://主机/共享/可选子目录，不含账号、密码、端口或查询参数。");
        return new SmbLocation(uri.IdnHost.ToLowerInvariant(), segments[0], string.Join('/', segments.Skip(1)));
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Any(c => c is '/' or '\\' or ':' or '\0' || char.IsControl(c)))
            throw new ArgumentException("无效的共享内文件/目录名称。");
    }
}

public sealed class SmbConnection : IDisposable
{
    private IntPtr _context;
    private readonly object _lock = new();
    public SmbLocation Location { get; }
    public StorageRootIdentity RootIdentity { get; private set; } = null!;
    public SmbStorageFolder Root { get; private set; } = null!;

    private SmbConnection(SmbLocation location) => Location = location;

    public static Task<SmbConnection> ConnectAsync(string url, string user = "", string? password = null,
        StorageRootIdentity? expectedIdentity = null, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = new SmbConnection(SmbLocation.Parse(url));
            try
            {
                connection._context = SmbNative.sr_new();
                if (connection._context == IntPtr.Zero) throw new IOException("无法初始化 SMB 客户端。");
                connection.Check(SmbNative.sr_connect(connection._context, connection.Location.Host,
                    connection.Location.Share, user, user.Length == 0 ? null : password ?? ""), "连接/认证");
                var guid = new byte[16];
                connection.Check(SmbNative.sr_guid(connection._context, guid), "服务器身份");
                var stat = connection.Stat(connection.Location.Directory);
                if (stat.Type != 1 || stat.IsLink || (stat.Inode == 0 && stat.Birth == 0))
                    throw new IOException("目标根不是具有可靠身份的普通目录。");
                connection.RootIdentity = new StorageRootIdentity("smb", connection.Location.Url,
                    Convert.ToHexString(guid), stat.Identity);
                if (expectedIdentity is not null && expectedIdentity != connection.RootIdentity)
                    throw new IOException("网络服务器/共享/目标根身份已变化；请明确重新选择，旧撤销记录不会迁移。");
                connection.Root = new SmbStorageFolder(connection, connection.Location.Directory, stat);
                cancellationToken.ThrowIfCancellationRequested();
                return connection;
            }
            catch { connection.Dispose(); throw; }
        }, cancellationToken);

    internal T Locked<T>(Func<IntPtr, T> action)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_context == IntPtr.Zero, this);
            return action(_context);
        }
    }
    internal void Check(int result, string stage)
    {
        if (result < 0) throw new SmbException(stage, result);
    }
    internal SmbNative.Stat Stat(string path) => Locked(context =>
    {
        Check(SmbNative.sr_stat_path(context, path, out var stat), "目录/文件元数据"); return stat;
    });
    internal void ValidateFolder(string path, string identity)
    {
        var root = Stat(Location.Directory);
        if (root.Identity != RootIdentity.RootFileIdentity || root.Type != 1 || root.IsLink)
            throw new IOException("网络根目录身份已变化；未执行写入/删除。");
        var folder = path == Location.Directory ? root : Stat(path);
        if (folder.Identity != identity || folder.Type != 1 || folder.IsLink)
            throw new IOException("网络目标目录身份已变化；未执行写入/删除。");
    }
    internal IStorageItem[] List(string path, string identity) => Locked(context =>
    {
        ValidateFolder(path, identity);
        var directory = SmbNative.sr_list(context, path);
        if (directory == IntPtr.Zero) throw new IOException("SMB 列目录失败；连接可能已断开。");
        var items = new List<IStorageItem>();
        try
        {
            while (true)
            {
                var namePointer = SmbNative.sr_next(context, directory, out var stat);
                if (namePointer == IntPtr.Zero) break;
                var name = Marshal.PtrToStringUTF8(namePointer)!;
                if (name is "." or ".." || stat.IsLink) continue;
                SmbLocation.ValidateName(name);
                var child = path.Length == 0 ? name : path + "/" + name;
                if (stat.Type == 1) items.Add(new SmbStorageFolder(this, child, stat));
                else if (stat.Type == 0) items.Add(new SmbStorageFile(this, child, stat, path, identity));
            }
        }
        finally { SmbNative.sr_list_close(context, directory); }
        return items.ToArray();
    });
    internal Uri ItemUri(string path, string identity)
    {
        var value = $"smb://{Location.Host}/{Uri.EscapeDataString(Location.Share)}" +
                    (path.Length == 0 ? "" : "/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString)));
        return new Uri(value + "#" + RootIdentity.ServerIdentity + ":" + RootIdentity.RootFileIdentity + ":" + identity);
    }
    internal SmbHandleStream Open(string path, string folderPath, string folderIdentity, int mode) => Locked(context =>
    {
        ValidateFolder(folderPath, folderIdentity);
        Check(SmbNative.sr_open(context, path, mode, out var handle), mode == 1 ? "排他创建" : "打开字幕");
        try
        {
            ValidateFolder(folderPath, folderIdentity);
            return new SmbHandleStream(this, handle, mode);
        }
        catch { SmbNative.sr_close(context, handle); throw; }
    });
    public void BreakConnectionForTest() => Locked(context => { SmbNative.sr_break_connection(context); return 0; });
    public void Dispose()
    {
        lock (_lock)
        {
            if (_context != IntPtr.Zero) SmbNative.sr_destroy(_context);
            _context = IntPtr.Zero;
        }
    }
}
