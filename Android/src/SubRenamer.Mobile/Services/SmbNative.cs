using System.Runtime.InteropServices;

namespace SubRenamer.Mobile.Services;

internal static class SmbNative
{
    private const string Library = "subrenamersmb";
    [StructLayout(LayoutKind.Sequential)]
    internal struct Stat
    {
        public ulong Inode, Birth, BirthNanoseconds, Size;
        public uint Type, Attributes;
        // Some Samba/filesystem combinations synthesize directory creation
        // time from mutable atime/ctime. Directory identity must not use it.
        public readonly string Identity => Type == 1 ? $"directory:{Inode:x}" : $"{Inode:x}:{Birth:x}:{BirthNanoseconds:x}";
        public readonly bool IsLink => Type == 2 || (Attributes & 0x400) != 0;
    }
    [DllImport(Library)] internal static extern IntPtr sr_new();
    [DllImport(Library)] internal static extern void sr_destroy(IntPtr context);
    [DllImport(Library)] internal static extern int sr_connect(IntPtr context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string host, [MarshalAs(UnmanagedType.LPUTF8Str)] string share,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string user, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);
    [DllImport(Library)] internal static extern int sr_volume(IntPtr context, out uint serial);
    [DllImport(Library)] internal static extern int sr_guid(IntPtr context, [Out] byte[] guid);
    [DllImport(Library)] internal static extern int sr_stat_path(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out Stat stat);
    [DllImport(Library)] internal static extern IntPtr sr_list(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Library)] internal static extern IntPtr sr_next(IntPtr context, IntPtr directory, out Stat stat);
    [DllImport(Library)] internal static extern void sr_list_close(IntPtr context, IntPtr directory);
    [DllImport(Library)] internal static extern int sr_open(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode, out IntPtr handle);
    [DllImport(Library)] internal static extern int sr_stat_handle(IntPtr context, IntPtr handle, out Stat stat);
    [DllImport(Library)] internal static extern int sr_read(IntPtr context, IntPtr handle, [Out] byte[] buffer, uint count, ulong offset);
    [DllImport(Library)] internal static extern int sr_write(IntPtr context, IntPtr handle, byte[] buffer, uint count, ulong offset);
    [DllImport(Library)] internal static extern int sr_flush(IntPtr context, IntPtr handle);
    [DllImport(Library)] internal static extern int sr_disposition(IntPtr context, IntPtr handle, int deletePending);
    [DllImport(Library)] internal static extern int sr_close(IntPtr context, IntPtr handle);
    [DllImport(Library)] internal static extern void sr_break_connection(IntPtr context);
}

public sealed class SmbException(string stage, int code) : IOException($"SMB {stage} 失败（错误码 {code}）")
{
    public int Code { get; } = code;
}
