# Third-party notices

Slopfin is built on the following open-source components. Each remains under its own license;
this file is shipped next to the application.

| Component | Use in Slopfin | License | Source |
|---|---|---|---|
| mpv (libmpv) | Video/audio decoding and playback | GPL-2.0-or-later as built here (parts LGPL-2.1-or-later) | https://github.com/mpv-player/mpv |
| FFmpeg, libplacebo, libass, dav1d and other libraries inside libmpv | Demuxing, decoding, rendering, subtitles | LGPL-2.1-or-later / GPL-2.0-or-later / BSD-style (per library) | see below |
| Avalonia UI (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent) | Cross-platform UI framework | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Fonts.Inter / Inter typeface | UI font | MIT (package) / SIL Open Font License 1.1 (font) | https://github.com/rsms/inter |
| CommunityToolkit.Mvvm | MVVM helpers | MIT | https://github.com/CommunityToolkit/dotnet |
| System.Security.Cryptography.ProtectedData | Windows DPAPI secret storage | MIT | https://github.com/dotnet/runtime |
| Material Design Icons (Pictogrammers) | Icon path data | Apache-2.0 | https://github.com/Templarian/MaterialDesign |
| Velopack (runtime + `vpk` build tool) | Installer and opt-in updates | MIT | https://github.com/velopack/velopack |
| libsecret (used from the system, not bundled) | Linux: saving sign-ins in the desktop keyring | LGPL-2.1-or-later | https://gitlab.gnome.org/GNOME/libsecret |

Slopfin itself is GPL-3.0, which is compatible with mpv's GPL-2.0-or-later licensing.

### Windows: libmpv-2.dll

`libmpv-2.dll` is the unmodified "mpv-dev" build by shinchiro's mpv-winbuild-cmake (build
20261107, mpv git `9028187538`). It statically includes FFmpeg, libplacebo, libass, dav1d, libdovi
and other libraries.

- Corresponding mpv source: https://github.com/mpv-player/mpv/tree/9028187538
- Build scripts, with every bundled library and its pinned version: https://github.com/shinchiro/mpv-winbuild-cmake
- `get-libmpv.ps1` in this repository downloads exactly this build and checks its SHA-256.

### Linux AppImage: bundled libraries

The Linux AppImage carries libmpv and the libraries it needs (FFmpeg, libplacebo, libass, dav1d
and others), taken unmodified from Ubuntu 24.04's packages. Inside the AppImage:
- `mpv/licenses/` holds the copyright and license file of every package used.
- `mpv/licenses/SOURCES.txt` lists each source package and its exact version.

The corresponding source code is available from Ubuntu at
`https://launchpad.net/ubuntu/+source/<source package>/<version>` (or `apt-get source`). Each of
these libraries is loaded dynamically and can be replaced: Slopfin uses the system's own copy of
a library whenever one is installed.

The Slopfin app icon (gradient tile with fin) is original artwork made for this project.

## Services and trademarks

Slopfin is an independent project. It is not affiliated with or endorsed by the Jellyfin project,
the mpv project, Seerr, The Movie Database (TMDB) or NostalgiaTV. Those names are used only to
describe compatibility. Poster images shown on the Requests page are served by TMDB through the
user's own Seerr server.
