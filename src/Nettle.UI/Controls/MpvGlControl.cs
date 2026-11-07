using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Nettle.UI.Playback;

namespace Nettle.UI.Controls;

/// <summary>
/// mpv's render API: mpv draws each video frame into this control's OpenGL framebuffer, and
/// Avalonia composites it like any other control. The player controls are then ordinary UI on
/// top, in the same window, so there's no separate overlay window to keep lined up (which is
/// what made the Linux player fragile under GNOME / XWayland). Used on Linux; Windows keeps
/// mpv's own D3D11 window because that's what carries HDR to the display.
/// </summary>
public sealed class MpvGlControl : OpenGlControlBase
{
    public static readonly StyledProperty<MpvPlayer?> PlayerProperty =
        AvaloniaProperty.Register<MpvGlControl, MpvPlayer?>(nameof(Player));

    public MpvPlayer? Player { get => GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }

    /// <summary>Raised (on the UI thread) if OpenGL or mpv's render context can't be set up.</summary>
    public event Action<string>? RenderFailed;

    private IntPtr _render;
    private MpvPlayer? _attached;
    private GlInterface? _gl;
    // Delegates handed to native code must stay alive as long as mpv may call them.
    private Mpv.GetProcAddressFn? _getProc;
    private Mpv.RenderUpdateFn? _onUpdate;
    private GCHandle _self;
    private int _framePending;
    private bool _initSeen;

    // One X11 connection for mpv's VA-API (hardware decoding) setup, opened once per process.
    private static IntPtr _x11Display;
    private static bool _x11Tried;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // No GPU OpenGL at all (software rendering): OnOpenGlInit never runs. Report it so the
        // host can fall back to mpv's own window.
        DispatcherTimer.RunOnce(() =>
        {
            if (!_initSeen && VisualRoot != null) RenderFailed?.Invoke("OpenGL isn't available to Nettle's window");
        }, TimeSpan.FromSeconds(3));
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        _initSeen = true;
        _gl = gl;
        var player = Player;
        if (player == null) return;
        try
        {
            player.Initialize(null, CreateRenderContext);
            if (_render == IntPtr.Zero) throw new InvalidOperationException("mpv's OpenGL renderer couldn't start");
            _attached = player;
            player.AttachRender();
            Nettle.Core.Services.Log.Info($"mpv render API: OpenGL {gl.ContextInfo.Version}");
        }
        catch (Exception ex)
        {
            Nettle.Core.Services.Log.Error("mpv render API failed", ex);
            Dispatcher.UIThread.Post(() => RenderFailed?.Invoke(ex.Message));
        }
    }

    private void CreateRenderContext(IntPtr mpv)
    {
        var gl = _gl!;
        _getProc = (_, namePtr) =>
        {
            var name = Marshal.PtrToStringAnsi(namePtr);
            return name == null ? IntPtr.Zero : gl.GetProcAddress(name);
        };
        _self = GCHandle.Alloc(this);
        _onUpdate = ctx =>
        {
            // Called on an mpv thread: ask Avalonia for a frame (coalesced).
            if (Interlocked.Exchange(ref _framePending, 1) == 1) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _framePending, 0);
                RequestNextFrameRendering();
            }, DispatcherPriority.Render);
        };

        var apiType = Marshal.StringToCoTaskMemUTF8("opengl");
        var init = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var parms = Marshal.AllocHGlobal(16 * 4);
        try
        {
            Marshal.WriteIntPtr(init, 0, Marshal.GetFunctionPointerForDelegate(_getProc));
            Marshal.WriteIntPtr(init, IntPtr.Size, IntPtr.Zero);
            int i = 0;
            void Param(int type, IntPtr data)
            {
                Marshal.WriteInt32(parms, i * 16, type);
                Marshal.WriteIntPtr(parms, i * 16 + 8, data);
                i++;
            }
            Param(Mpv.RenderParamApiType, apiType);
            Param(Mpv.RenderParamOpenGlInitParams, init);
            if (X11Display() is { } dpy && dpy != IntPtr.Zero) Param(Mpv.RenderParamX11Display, dpy);
            Param(Mpv.RenderParamInvalid, IntPtr.Zero);

            int r = Mpv.mpv_render_context_create(out _render, mpv, parms);
            if (r < 0) { _render = IntPtr.Zero; throw new InvalidOperationException("mpv_render_context_create: " + Mpv.ErrorString(r)); }
            Mpv.mpv_render_context_set_update_callback(_render, Marshal.GetFunctionPointerForDelegate(_onUpdate), IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeCoTaskMem(apiType);
            Marshal.FreeHGlobal(init);
            Marshal.FreeHGlobal(parms);
        }
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);

    private static IntPtr? X11Display()
    {
        if (!OperatingSystem.IsLinux()) return null;
        if (!_x11Tried)
        {
            _x11Tried = true;
            try { _x11Display = XOpenDisplay(IntPtr.Zero); } catch { _x11Display = IntPtr.Zero; }
        }
        return _x11Display;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        var ctx = _render;
        if (ctx == IntPtr.Zero) return;
        var scale = VisualRoot?.RenderScaling ?? 1;
        int w = Math.Max(1, (int)(Bounds.Width * scale)), h = Math.Max(1, (int)(Bounds.Height * scale));

        var fbo = Marshal.AllocHGlobal(16);
        var flip = Marshal.AllocHGlobal(4);
        var parms = Marshal.AllocHGlobal(16 * 3);
        try
        {
            Marshal.WriteInt32(fbo, 0, fb);
            Marshal.WriteInt32(fbo, 4, w);
            Marshal.WriteInt32(fbo, 8, h);
            Marshal.WriteInt32(fbo, 12, 0);
            // Avalonia's framebuffer uses OpenGL's bottom-up rows, so mpv flips its output.
            Marshal.WriteInt32(flip, 0, 1);
            Marshal.WriteInt32(parms, 0, Mpv.RenderParamOpenGlFbo); Marshal.WriteIntPtr(parms, 8, fbo);
            Marshal.WriteInt32(parms, 16, Mpv.RenderParamFlipY); Marshal.WriteIntPtr(parms, 24, flip);
            Marshal.WriteInt32(parms, 32, Mpv.RenderParamInvalid); Marshal.WriteIntPtr(parms, 40, IntPtr.Zero);
            Mpv.mpv_render_context_render(ctx, parms);
            Mpv.mpv_render_context_report_swap(ctx);
        }
        finally
        {
            Marshal.FreeHGlobal(fbo);
            Marshal.FreeHGlobal(flip);
            Marshal.FreeHGlobal(parms);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        // Must happen with the GL context current and before mpv itself is destroyed.
        if (_render != IntPtr.Zero)
        {
            Mpv.mpv_render_context_set_update_callback(_render, IntPtr.Zero, IntPtr.Zero);
            Mpv.mpv_render_context_free(_render);
            _render = IntPtr.Zero;
        }
        if (_self.IsAllocated) _self.Free();
        var p = _attached;
        _attached = null;
        p?.DetachRender();
    }

    protected override void OnOpenGlLost()
    {
        // The GL context died (driver reset): mpv's context went with it.
        _render = IntPtr.Zero;
        var p = _attached;
        _attached = null;
        p?.DetachRender();
        base.OnOpenGlLost();
    }
}
