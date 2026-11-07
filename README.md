<p align="center">
  <img src="docs/images/banner.png" alt="Slopfin: a fast, beautiful Jellyfin client for Windows and Linux, built on VLC" width="100%">
</p>

<p align="center">
  <b><a href="https://github.com/devall96/Slopfin/releases/latest">⬇ Download Slopfin for Windows or Linux</a></b>
  &nbsp;·&nbsp; <a href="#features">Features</a>
  &nbsp;·&nbsp; <a href="#install">Install</a>
  &nbsp;·&nbsp; <a href="#using-slopfin">Using Slopfin</a>
  &nbsp;·&nbsp; <a href="#privacy">Privacy</a>
  &nbsp;·&nbsp; <a href="#building-from-source">Build</a>
</p>

---

**Slopfin** is a Jellyfin client for Windows and Linux that plays everything in **original quality**. Instead of
asking your server to transcode, it plays the actual file with the VLC engine, using your graphics
card for 4K and HDR. Slopfin never transcodes: it has no code path that asks the server to.

It also brings in a TV-style live guide for **NostalgiaTV**, **Seerr** requests, skippable intros
and credits, multiple profiles, and eight hand-made themes. It has **no telemetry of any kind**.

> **Slopfin 0.1.0 is the first tester preview.** It's ready for daily use, but expect rough edges.
> Please [report anything odd](https://github.com/devall96/Slopfin/issues).

## Screenshots

<p align="center"><img src="docs/images/home.png" alt="Home screen with spotlight and rows" width="100%"></p>

| | |
|---|---|
| <img src="docs/images/details.png" alt="Details page"> | <img src="docs/images/library.png" alt="Library grid"> |
| <img src="docs/images/signin.png" alt="Sign-in with profile tiles and Quick Connect"> | <img src="docs/images/themes.png" alt="Eight built-in themes"> |

<sub>Screenshots show Jellyfin's public demo server.</sub>

## Features

### Playback
- **Direct play, always.** Slopfin requests the original file and never asks the server for a transcode or remux. Your server's CPU stays idle and the picture is untouched.
- **VLC engine** (libVLC), with hardware decoding (D3D11/DXVA). It plays practically every container, codec and subtitle format.
- **HDR10, HLG and Dolby Vision** (via its HDR10 base layer), passed straight to HDR displays.
- **TV-style format badges.** When playback starts, badges like **4K · DOLBY VISION · DTS-HD MA** pop up, the way a TV shows them. Each badge is *verified*:
  - Resolution comes from the frames actually being decoded.
  - HDR is only claimed if Windows confirms your monitor is in HDR mode right now.
- **Skip intro, recap, credits, previews and ads,** using the [Intro Skipper](https://github.com/intro-skipper/intro-skipper) plugin or Jellyfin media segments. Each one can show a button, skip automatically, or do nothing.
- **Up Next** card with a countdown during the credits, then automatic next episode.
- Resume points and watched status stay in sync with your server.
- Audio and subtitle track menu, including external subtitles.

### Browsing
- **Home:** a spotlight carousel with logo art, plus Continue Watching, Next Up, and Recently Added for each library.
- **Libraries:** a fast grid that only draws the posters on screen, with sort and filters (unwatched, in progress, favorites).
- **Details:** backdrop, logo, ratings, file info, cast and crew, seasons and episodes, More Like This, and a version picker.
- **Search** across your library. If something's missing, one click searches Seerr to request it.

### Live TV: NostalgiaTV
- A full cable-style **program guide**, with a live "now" line and paging.
- **Original-quality channels.** By default Slopfin plays each scheduled program's real file from your Jellyfin server at the live position, instead of NostalgiaTV's re-encoded broadcast feed.
  - It joins mid-program and rolls into the next show automatically.
  - Channel up/down works with the keyboard or the on-screen buttons.
- Channels with nothing scheduled are hidden.
- Only live channels appear. **NostalgiaTV's On Demand section is deliberately not included.**

### Requests: Seerr
- Browse Trending, Popular and Coming Soon, search, and request movies or whole series with one click.
- See your own requests and their status.
- Sign in with an API key, or with your Jellyfin username and password. Only the resulting session is kept, encrypted.

### Accounts and sign-in
- Finds Jellyfin servers on your network automatically. You can also type an address, and Slopfin tries https, http and the default ports for you.
- Sign in with profile tiles, a password, or **Quick Connect**.
- **Multiple profiles** with a "Who's watching?" picker, quick switching, and optional automatic sign-in.
- **Passwords are never stored.** Only Jellyfin's session token is kept: on Windows it's encrypted with DPAPI for your Windows account, and on Linux it goes into your desktop keyring (GNOME Keyring, KWallet, KeePassXC…).

### Look and feel
- **Eight original themes:** Midnight, Obsidian (true black for OLED), Aurora, Ember, Sakura, Deep Sea, Evergreen and Daylight. Switching is instant.
- Smooth, lightweight animations, a navigation rail that expands over the page, and fade-in artwork.
- **Ready for keyboard and TV remotes.** Arrow keys move focus the way a TV remote's d-pad does.

## Install

### Windows

1. Download **`SlopfinApp-win-Setup.exe`** from the [latest release](https://github.com/devall96/Slopfin/releases/latest).
2. Run it. It installs for your user account only, so no admin rights are needed, and adds Start-menu and Desktop shortcuts.
3. **About the Windows warning:** Slopfin isn't code-signed yet, so Windows SmartScreen may say *"Windows protected your PC"*. Click **More info → Run anyway**. You only see this on the first install; updates don't trigger it.

Prefer not to install? Download **`SlopfinApp-win-Portable.zip`**, extract it, and run `Slopfin.exe`.

**Requirements**
- Windows 10 or 11 (64-bit).
- A Jellyfin server, 10.9 or newer.
- For HDR, an HDR display with **Settings → Display → Use HDR** turned on.

**Uninstall:** Windows Settings → Apps → Slopfin → Uninstall. Your settings and saved sign-ins stay in `%LOCALAPPDATA%\Slopfin`. Delete that folder too if you want everything gone.

### Linux

1. Download **`SlopfinApp.AppImage`** from the [latest release](https://github.com/devall96/Slopfin/releases/latest) and put it wherever you keep apps, for example `~/Applications`.
2. Make it executable: right-click → Properties → *Allow executing file as program*, or run `chmod +x SlopfinApp.AppImage`.
3. Double-click it. On first run, Slopfin offers to **add itself to your applications menu**, with its icon. This is also in **Settings → Desktop**. Nothing is installed system-wide and no root is needed.

VLC is built in, so you don't need to install anything else. It runs on any 64-bit distro from about 2022 onwards: Ubuntu 22.04+, Debian 12+, Fedora, Arch and their derivatives.

*If double-clicking does nothing,* your distro may be missing FUSE 2, which AppImages use. Install `libfuse2` (Ubuntu/Debian: `sudo apt install libfuse2t64`, or `libfuse2` on older releases) or run `./SlopfinApp.AppImage --appimage-extract-and-run`.

**Uninstall:** in **Settings → Desktop**, press *Remove from menu*, then delete the AppImage. Settings and the image cache live in `~/.local/share/Slopfin` and `~/.cache/Slopfin`. Saved sign-ins are in your keyring under "Slopfin".

## Using Slopfin

### First run
1. Pick your server from **On your network**, or type its address, e.g. `192.168.1.20:8096` or `https://jellyfin.example.com`.
2. Choose your profile, or sign in with a username and password or Quick Connect.
3. Leave **Remember me** on to sign in automatically next time. You can switch or forget profiles from the avatar at the bottom of the side rail, or in **Settings → Accounts**.

### Keyboard and remote

| Key | Browsing | Player | Live TV |
|---|---|---|---|
| Arrow keys | Move focus | ←/→ seek, ↑/↓ volume | ↑/↓ change channel |
| Enter | Open / select | Show controls | Show controls |
| Space / K | | Play / pause | Pause (resumes at the live point) |
| Esc / Back | Go back (mouse back button too) | Close player | Close player |
| F / F11 | Full screen | Full screen | Full screen |
| S | | Skip intro/credits | |
| N | | Next episode | |
| M | | Mute | Mute |
| I | | Show format badges | Show format badges |
| Page Up / Page Down | | | Channel up / down |

### Live TV (NostalgiaTV)
Go to **Settings → NostalgiaTV** and enter your NostalgiaTV address, for example `http://your-server:19850`. An API key is optional. Press **Save & test**, then open **Live TV**.

- *Play original files (full quality)* is on by default. Turn it off to use NostalgiaTV's own broadcast feed instead, which NostalgiaTV re-encodes.
- Channels without a Jellyfin item behind them, such as weather or external IPTV, always use the broadcast feed.

### Requests (Seerr)
Go to **Settings → Seerr (Requests)**, enter your Seerr address and either an API key or your Jellyfin login, then press **Save & test**.

Posters load through your Seerr server's image proxy, so TMDB never sees your IP address. If posters don't appear, turn on *Load posters directly from TMDB*.

### Updates
Updates are **off unless you use them**:

1. Go to **Settings → Updates** and enter `https://github.com/devall96/Slopfin`.
2. Press **Check now**, or turn on the once-per-launch check.
3. Updates only download what changed, and install with **Install & restart**.

## Privacy

Slopfin contains **no telemetry, analytics, crash reporting, ads or tracking**. It only ever connects to:

- the Jellyfin, NostalgiaTV and Seerr servers *you* enter;
- your local network, when the sign-in screen looks for Jellyfin servers;
- your chosen update source, and only when you check for updates;
- TMDB, only if you turn on direct TMDB posters.

Other privacy details:
- Servers see the device as "Slopfin (Windows)" or "Slopfin (Linux)", never your PC's name.
- Images only load from the servers you configured.
- VLC's online metadata lookups are disabled.
- Diagnostic logs stay on your PC and have tokens masked.
- Even the build process has Avalonia's build statistics turned off.

> This program will not transfer any information to other networked systems unless specifically
> requested by the user or the person installing or operating it.

## Known limitations (0.1.0)
- **Dolby Vision profile 5** files (no HDR10 fallback) show wrong colours in VLC 3. Slopfin warns you about these files.
- **Atmos and DTS:X** are decoded to PCM. Height channels need a receiver, and audio passthrough isn't available yet.
- Not code-signed yet, so SmartScreen shows a warning on first install.
- **Linux:** HDR video is tone-mapped to SDR, because VLC 3 has no HDR output on Linux. The badge says so. If your desktop has no keyring at all, sign-ins are kept in a file only your user can read.
- Windows and Linux for now. The code is structured for Android TV and iOS versions later.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/).

```bash
git clone https://github.com/devall96/Slopfin.git
cd Slopfin
dotnet run --project src/Slopfin.Desktop
```

On Linux, install VLC first (e.g. `sudo apt install vlc`). Source builds use the system's VLC; only the AppImage bundles its own.

The Linux AppImage is built by the **Linux AppImage** GitHub Actions workflow
(`.github/workflows/linux.yml`). It bundles libVLC, then decodes H.264, 4K HEVC 10-bit and AV1
test clips on clean Ubuntu, Debian, Fedora and Arch machines before anything is published.

To build a Windows release (installer, portable zip and update packages in `releases\`):

```bash
powershell -ExecutionPolicy Bypass -File build-release.ps1
```

**Contributing:** run `git config core.hooksPath .githooks` once after cloning. A pre-commit check then blocks commits that contain files outside the project or anything that looks like a token or a private server address. The `.gitignore` is an allowlist, so only Slopfin's own files can be tracked.

`build-release.ps1` reads the version from `Directory.Build.props`. The app draws the installer splash art itself during the build.

**Project layout**

```
src/
  Slopfin.Core      Platform-neutral: Jellyfin / Seerr / NostalgiaTV clients, models, settings, account vault
  Slopfin.UI        Avalonia views + view models, themes, image cache, VLC player (shared by every platform)
  Slopfin.Desktop   Windows + Linux app: window, secure storage (DPAPI / keyring), HDR detection,
                    libVLC loading, applications-menu entry, installer/updater hooks
```

## Code signing policy

Release builds are **not yet code-signed**. The plan is to apply to the free SignPath Foundation
programme for open-source projects. Until then, check that you downloaded Slopfin from
[this repository's Releases page](https://github.com/devall96/Slopfin/releases).

**Team roles:** [@devall96](https://github.com/devall96) is the author, reviewer and release approver.

## License

Slopfin is free software under the **[GNU General Public License v3.0](LICENSE)**. Third-party
components and their licenses are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

**Name and logo:** the "Slopfin" name and fin logo identify this project. Forks are welcome under the
GPL, but please use a different name and icon for your own builds.

Slopfin is an independent project. It is not affiliated with or endorsed by Jellyfin, VideoLAN,
Seerr, TMDB or NostalgiaTV.
