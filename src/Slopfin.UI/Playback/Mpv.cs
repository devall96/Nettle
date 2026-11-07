using System.Runtime.InteropServices;

namespace Slopfin.UI.Playback;

/// <summary>
/// Minimal binding to libmpv's client API (mpv/client.h, API 2.x). Only what Slopfin uses.
/// The library is "libmpv-2.dll" on Windows and "libmpv.so.2" on Linux; platform heads can
/// point <see cref="LibraryHandle"/> at a specific copy before first use.
/// </summary>
internal static class Mpv
{
    private const string Lib = "libmpv";

    /// <summary>Set by a platform head that loaded libmpv itself (e.g. the Linux AppImage's bundled copy).</summary>
    public static IntPtr LibraryHandle { get; set; }

    static Mpv()
    {
        NativeLibrary.SetDllImportResolver(typeof(Mpv).Assembly, (name, asm, path) =>
        {
            if (name != Lib) return IntPtr.Zero;
            if (LibraryHandle != IntPtr.Zero) return LibraryHandle;
            foreach (var candidate in new[] { "libmpv-2.dll", "mpv-2.dll", "libmpv.so.2", "libmpv.2.dylib", "libmpv.so" })
                if (NativeLibrary.TryLoad(candidate, asm, path, out var h)) return LibraryHandle = h;
            return IntPtr.Zero;
        });
    }

    /// <summary>Forces the static constructor (resolver registration) to run.</summary>
    public static void EnsureResolver() { }

    // ---- formats / events ----
    public const int FormatNone = 0, FormatString = 1, FormatFlag = 3, FormatInt64 = 4, FormatDouble = 5;
    public const int EventShutdown = 1, EventLogMessage = 2, EventStartFile = 6, EventEndFile = 7, EventFileLoaded = 8,
        EventVideoReconfig = 17, EventAudioReconfig = 18, EventPlaybackRestart = 21, EventPropertyChange = 22;
    public const int EndReasonEof = 0, EndReasonStop = 2, EndReasonQuit = 3, EndReasonError = 4;

    [DllImport(Lib)] public static extern ulong mpv_client_api_version();
    [DllImport(Lib)] public static extern IntPtr mpv_create();
    [DllImport(Lib)] public static extern int mpv_initialize(IntPtr ctx);
    [DllImport(Lib)] public static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport(Lib)] public static extern IntPtr mpv_error_string(int error);
    [DllImport(Lib)] public static extern void mpv_free(IntPtr data);
    [DllImport(Lib)] public static extern int mpv_set_option_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [DllImport(Lib)] public static extern int mpv_set_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [DllImport(Lib)] public static extern IntPtr mpv_get_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] public static extern int mpv_get_property(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out double data);
    [DllImport(Lib, EntryPoint = "mpv_get_property")] public static extern int mpv_get_property_long(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out long data);
    [DllImport(Lib)] public static extern int mpv_set_property(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref long data);
    [DllImport(Lib)] public static extern int mpv_command(IntPtr ctx, IntPtr[] args);
    [DllImport(Lib)] public static extern int mpv_observe_property(IntPtr ctx, ulong replyUserdata, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);
    [DllImport(Lib)] public static extern int mpv_request_log_messages(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string minLevel);
    [DllImport(Lib)] public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
    [DllImport(Lib)] public static extern void mpv_wakeup(IntPtr ctx);

    public static string ErrorString(int code) => Marshal.PtrToStringUTF8(mpv_error_string(code)) ?? code.ToString();

    /// <summary>Runs an mpv command given as separate UTF-8 arguments (no quoting/escaping issues).</summary>
    public static int Command(IntPtr ctx, params string[] args)
    {
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            return mpv_command(ctx, ptrs);
        }
        finally
        {
            for (int i = 0; i < args.Length; i++) if (ptrs[i] != IntPtr.Zero) Marshal.FreeCoTaskMem(ptrs[i]);
        }
    }

    public static string? GetString(IntPtr ctx, string name)
    {
        var p = mpv_get_property_string(ctx, name);
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(p); }
        finally { mpv_free(p); }
    }
}
