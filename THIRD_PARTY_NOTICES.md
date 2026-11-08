# Third-party notices

Nettle is built on the following open-source components. Each remains under its own license;
this file is shipped next to the application.

| Component | Use in Nettle | License | Source |
|---|---|---|---|
| mpv (libmpv) | Video/audio decoding and playback | GPL-2.0-or-later as built here (parts LGPL-2.1-or-later) | https://github.com/mpv-player/mpv |
| FFmpeg, libplacebo, libass, dav1d and other libraries inside libmpv | Demuxing, decoding, rendering, subtitles | LGPL-2.1-or-later / GPL-2.0-or-later / BSD-style (per library) | see below |
| Avalonia UI (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent) | Cross-platform UI framework | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Fonts.Inter / Inter typeface | UI font | MIT (package) / SIL Open Font License 1.1 (font) | https://github.com/rsms/inter |
| CommunityToolkit.Mvvm | MVVM helpers | MIT | https://github.com/CommunityToolkit/dotnet |
| System.Security.Cryptography.ProtectedData | Windows DPAPI secret storage | MIT | https://github.com/dotnet/runtime |
| Material Design Icons (Pictogrammers) | Icon path data | Apache-2.0 | https://github.com/Templarian/MaterialDesign |
| Velopack (runtime + `vpk` build tool) | Installer and opt-in updates | MIT | https://github.com/velopack/velopack |
| Tmds.DBus.Protocol | Linux: keeping the screen on during playback (session D-Bus) | MIT | https://github.com/tmds/Tmds.DBus |
| libsecret (used from the system, not bundled) | Linux: saving sign-ins in the desktop keyring | LGPL-2.1-or-later | https://gitlab.gnome.org/GNOME/libsecret |

Nettle itself is GPL-3.0, which is compatible with mpv's GPL-2.0-or-later licensing.

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
these libraries is loaded dynamically and can be replaced: Nettle uses the system's own copy of
a library whenever one is installed.

The Nettle app icon (gradient tile with fin) is original artwork made for this project.

### Android TV app (native, `android/`)

| Component | Use in Nettle | License | Source |
|---|---|---|---|
| Kotlin standard library, kotlinx.coroutines, kotlinx.serialization | Language runtime, async work, JSON | Apache-2.0 | https://github.com/JetBrains/kotlin |
| AndroidX (Jetpack Compose, Activity, Lifecycle, ProfileInstaller) | User interface | Apache-2.0 | https://developer.android.com/jetpack/androidx |
| AndroidX Media3 (ExoPlayer, OkHttp data source, subtitle view) | Video and audio playback | Apache-2.0 | https://github.com/androidx/media |
| OkHttp and Okio | HTTP and WebSocket | Apache-2.0 | https://github.com/square/okhttp |
| Coil | Image loading | Apache-2.0 | https://github.com/coil-kt/coil |
| Accompanist Drawable Painter (used by Coil) | Image drawing | Apache-2.0 | https://github.com/google/accompanist |
| Guava (used by Media3) | Utilities | Apache-2.0 | https://github.com/google/guava |
| Inter typeface (the same files as the desktop app) | UI font | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| Material Design Icons (Pictogrammers) | Icon path data | Apache-2.0 | https://github.com/Templarian/MaterialDesign |

None of these contact anything on their own. The Android build fails if any other dependency
appears, and always fails on Google Play Services, Firebase, crash-reporting or analytics
libraries. kxml2 (BSD) is used only to run the tests on a PC and is not shipped.

## Services and trademarks

Nettle is an independent project. It is not affiliated with or endorsed by the Jellyfin project,
the mpv project, Seerr, The Movie Database (TMDB) or NostalgiaTV. Those names are used only to
describe compatibility. Poster images shown on the Requests page are served by TMDB through the
user's own Seerr server.
