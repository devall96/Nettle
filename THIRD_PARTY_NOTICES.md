# Third-party notices

Slopfin is built on the following open-source components. Each remains under its own license;
this file is shipped next to the application. Before any public release, include the full
license texts for each component (linked below) in the distribution.

| Component | Use in Slopfin | License | Source |
|---|---|---|---|
| libVLC / VLC media player engine (VideoLAN) | Video/audio decoding and playback | LGPL-2.1-or-later (libVLC core; some bundled plugins are GPL-2.0-or-later) | https://code.videolan.org/videolan/vlc |
| VideoLAN.LibVLC.Windows (NuGet) | Prebuilt libVLC 3.x binaries for Windows | LGPL-2.1-or-later (see package) | https://code.videolan.org/videolan/libvlc-nuget |
| LibVLCSharp / LibVLCSharp.Avalonia | .NET bindings for libVLC | LGPL-2.1-or-later | https://code.videolan.org/videolan/LibVLCSharp |
| Avalonia UI (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent) | Cross-platform UI framework | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Fonts.Inter / Inter typeface | UI font | MIT (package) / SIL Open Font License 1.1 (font) | https://github.com/rsms/inter |
| CommunityToolkit.Mvvm | MVVM helpers | MIT | https://github.com/CommunityToolkit/dotnet |
| System.Security.Cryptography.ProtectedData | Windows DPAPI secret storage | MIT | https://github.com/dotnet/runtime |
| Material Design Icons (Pictogrammers) | Icon path data | Apache-2.0 | https://github.com/Templarian/MaterialDesign |
| Velopack (runtime + `vpk` build tool) | Installer and opt-in updates | MIT | https://github.com/velopack/velopack |

The Slopfin app icon (gradient tile with fin) is original artwork made for this project.

## LGPL compliance notes (libVLC / LibVLCSharp)

* libVLC and LibVLCSharp are used as separate, dynamically loaded libraries (`libvlc.dll`,
  `LibVLCSharp.dll`) and are not modified. Users may replace them with their own builds.
* The corresponding source code is available from VideoLAN at the links above. A release build
  should include (or link to) the exact source version of the bundled libVLC.

## Services and trademarks

Slopfin is an independent project. It is not affiliated with or endorsed by the Jellyfin project,
VideoLAN, Seerr, The Movie Database (TMDB) or NostalgiaTV. Those names are used only to describe
compatibility. Poster images shown on the Requests page are served by TMDB through the user's own
Seerr server.
