using Avalonia;
using Avalonia.Controls;
using Avalonia.Metadata;
using Nettle.UI.Playback;

namespace Nettle.UI.Controls;

/// <summary>
/// The player's video area with its controls (<see cref="Content"/>) on top. Picks how video
/// is drawn:
///  • a platform-supplied view (<see cref="CustomView"/>, Android TV: Media3 on a SurfaceView),
///  • mpv's render API into an OpenGL control with the controls as normal UI above it
///    (<see cref="PlaybackEngine.UseRenderApi"/>, Linux), or
///  • mpv's own native window with the controls in a floating overlay window
///    (<see cref="VideoSurface"/>, Windows, and the Linux fallback when OpenGL isn't available).
/// </summary>
public sealed class VideoHost : Panel
{
    public static readonly StyledProperty<IVideoPlayer?> PlayerProperty =
        AvaloniaProperty.Register<VideoHost, IVideoPlayer?>(nameof(Player));

    public static readonly StyledProperty<Control?> ContentProperty =
        AvaloniaProperty.Register<VideoHost, Control?>(nameof(Content));

    public IVideoPlayer? Player { get => GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }

    [Content]
    public Control? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }

    /// <summary>
    /// Set by a platform head whose engine draws video itself: returns the control that shows
    /// the given player's picture. The controls (<see cref="Content"/>) are layered above it.
    /// </summary>
    public static Func<IVideoPlayer, Control>? CustomView { get; set; }

    /// <summary>Set once the render API failed in this session, so later players go straight to the fallback.</summary>
    private static bool _renderApiBroken;

    private bool _built;

    static VideoHost()
    {
        PlayerProperty.Changed.AddClassHandler<VideoHost>((h, _) => h.OnPlayerChanged());
    }

    private void OnPlayerChanged()
    {
        if (!_built) return;
        if (CustomView != null) { Build(false); return; }
        foreach (var c in Children)
        {
            if (c is MpvGlControl gl) gl.Player = Player as MpvPlayer;
            else if (c is VideoSurface s) s.Player = Player as MpvPlayer;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_built) Build(PlaybackEngine.UseRenderApi && !_renderApiBroken);
    }

    private void Build(bool renderApi)
    {
        _built = true;
        var content = Content;
        Children.Clear();
        if (CustomView != null)
        {
            if (Player is { } p) Children.Add(CustomView(p));
            if (content != null) Children.Add(content);
        }
        else if (renderApi)
        {
            var gl = new MpvGlControl();
            gl.Player = Player as MpvPlayer;
            gl.RenderFailed += reason =>
            {
                // Before any video was drawn: switch this player to mpv's own window instead.
                Nettle.Core.Services.Log.Warn("render API unavailable (" + reason + "), using mpv's own window");
                _renderApiBroken = true;
                Build(false);
            };
            Children.Add(gl);
            if (content != null) Children.Add(content);
        }
        else
        {
            var surface = new VideoSurface();
            surface.Player = Player as MpvPlayer;
            surface.Content = content;
            Children.Add(surface);
        }
    }
}
