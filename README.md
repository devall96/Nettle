<p align="center">
  <img src="docs/images/banner.png" alt="Nettle: a fast, beautiful Jellyfin client for Windows and Linux, built on mpv" width="100%">
</p>

<p align="center">
  <b><a href="https://github.com/devall96/Slopfin/releases/latest">⬇ Download Nettle for Windows or Linux</a></b>
  &nbsp;·&nbsp; <a href="#-testers-wanted">Testers wanted</a>
  &nbsp;·&nbsp; <a href="#features">Features</a>
  &nbsp;·&nbsp; <a href="#install">Install</a>
  &nbsp;·&nbsp; <a href="#using-nettle">Using Nettle</a>
  &nbsp;·&nbsp; <a href="#privacy">Privacy</a>
  &nbsp;·&nbsp; <a href="#building-from-source">Build</a>
</p>

---

**Nettle** is a Jellyfin client for Windows and Linux that plays everything in **original quality**.
Instead of asking your server to transcode, it plays the actual file with the **mpv** engine, using
your graphics card for 4K, HDR and Dolby Vision. Nettle never transcodes: it has no code path that
asks the server to.

It also brings in a TV-style live guide for **NostalgiaTV**, **Seerr** requests, skippable intros
and credits, multiple profiles, and sixteen hand-made themes. It has **no telemetry of any kind**.

> [!NOTE]
> **Nettle is in beta on Windows and Linux.** It works well on the machines it has been tested
> on, but it hasn't been tried on much other hardware yet, so expect some rough edges.
>
> **Entirely AI-coded, single-user tested.** All of Nettle's code was written by an AI coding
> assistant (Anthropic's Claude), directed and reviewed by one person. So far it has been tested by
> that one person, on one Windows PC and one Ubuntu PC. That's why [testers are wanted](#-testers-wanted).

> [!TIP]
> **Nettle used to be called Slopfin.** If you installed Slopfin 0.1 or 0.2, **Settings → Updates**
> brings you to Nettle, and your settings and saved sign-ins come along.

## 🧪 Testers wanted

Nettle needs people to try it on hardware it hasn't seen yet: **NVIDIA and Intel graphics,
laptops, other Linux distros and desktops (KDE, XFCE…), multi-monitor and HDR setups.** If you have a
Jellyfin server and a few minutes, you'd help a lot:

1. [Download](https://github.com/devall96/Slopfin/releases/latest) the Windows installer or the Linux AppImage.
2. Sign in and play a few different things: 4K / HDR / Dolby Vision files, older SD shows, surround
   audio, subtitles, and Live TV if you use NostalgiaTV.
3. Tell us what broke, or that it worked, by
   [opening an issue](https://github.com/devall96/Slopfin/issues/new?template=bug_report.md). Please include:
   - Windows or Linux. On Linux, also the distro and desktop (e.g. *Fedora 41, KDE Plasma 6, Wayland*).
   - Your graphics card (e.g. *AMD RX 9070*, *NVIDIA RTX 3060*, *Intel Arc*).
   - What you played: resolution, HDR type and audio format. Nettle's format badges (press **I** while playing) show all of this.
   - A screenshot, if it's visual.
   - The end of Nettle's log: `%LOCALAPPDATA%\Nettle\nettle.log` on Windows, or
     `~/.local/share/Nettle/nettle.log` on Linux. Server tokens in the log are already masked, but
     you may want to blank out your server's address.

"It worked fine on my setup" reports are just as useful, so they're welcome too.

## Screenshots

<p align="center"><img src="docs/images/home.png" alt="Home screen with spotlight and rows" width="100%"></p>

| | |
|---|---|
| <img src="docs/images/details.png" alt="Details page"> | <img src="docs/images/library.png" alt="Library grid"> |
| <img src="docs/images/signin.png" alt="Sign-in with profile tiles and Quick Connect"> | <img src="docs/images/themes.png" alt="Sixteen built-in themes"> |

<sub>Screenshots show Jellyfin's public demo server.</sub>

## Features

### Playback
- **Direct play, always.** Nettle requests the original file and never asks the server for a transcode or remux. Your server's CPU stays idle and the picture is untouched.
- **mpv engine** (libmpv) with its modern renderer (`gpu-next` / libplacebo) and hardware decoding: D3D11VA on Windows, VA-API on Linux. It plays practically every container, codec and subtitle format.
- **HDR10, HDR10+, HLG and Dolby Vision,** including **Dolby Vision profile 5**, whose picture data is applied by the player. On Windows, HDR goes straight to your HDR display; on SDR screens it's tone-mapped carefully.
- **Smooth motion.** Playback syncs to your display's refresh rate, so 24 fps films don't judder on 60 Hz screens.
- **TV-style format badges.** When playback starts, badges like **4K · DOLBY VISION · DOLBY ATMOS** pop up, the way a TV shows them. Each badge is *verified*:
  - Resolution comes from the frames actually being decoded.
  - HDR is only claimed if the player is really sending an HDR signal, and Windows confirms your monitor is in HDR mode.
  - Audio formats only appear once that audio is actually being decoded.
- **Skip intro, recap, credits, previews and ads,** using the [Intro Skipper](https://github.com/intro-skipper/intro-skipper) plugin or Jellyfin media segments. Each one can show a button, skip automatically, or do nothing.
- **Up Next** card with a countdown during the credits, then automatic next episode.
- **"Are you still watching?"** After a few episodes in a row with no input (3 by default), or 4 hours of Live TV, Nettle pauses and asks. You can change the count or turn it off in **Settings → Playback**.
- **Scrubbing previews.** Hover over or drag the seek bar to see thumbnails, using the trickplay images your Jellyfin server already generates.
- **Audio passthrough** (optional): Dolby Digital, Dolby Digital Plus / Atmos, TrueHD, DTS and DTS-HD / DTS:X go to your AV receiver untouched, so it decodes them, height channels included.
- Resume points, watched status and your audio / subtitle choices stay in sync with your server. Closing Nettle mid-episode still saves where you were.
- Audio and subtitle track menu, including external subtitles. Adjustable network buffer for slow Wi-Fi.

### Browsing
- **Home:** a spotlight carousel with logo art, plus Continue Watching, Next Up, and Recently Added for each library.
- **Libraries:** a fast grid that only draws the posters on screen, with sort and filters (unwatched, in progress, favorites).
- **Details:** backdrop, logo, ratings, file info, cast and crew, seasons and episodes, More Like This, and a version picker.
- **Search** across your library. If something's missing, one click searches Seerr to request it.

### Live TV: NostalgiaTV
- A full cable-style **program guide** covering everything NostalgiaTV has scheduled (usually several days), with a live "now" line. Scroll through it, or jump ahead 90 minutes at a time. It opens instantly on later visits, and stays fast however far ahead NostalgiaTV schedules.
- **Original-quality channels.** By default Nettle plays each scheduled program's real file from your Jellyfin server at the live position, instead of NostalgiaTV's re-encoded broadcast feed.
  - It joins mid-program and rolls into the next show automatically.
  - Channel up/down works with the keyboard or the on-screen buttons.
- Channels with nothing scheduled are hidden.
- Only live channels appear. **NostalgiaTV's On Demand section is deliberately not included.**

### Requests: Seerr
- Browse Trending, Popular and Coming Soon, search, and request movies with one click. For series, **pick the seasons** you want; seasons you already have or requested are shown as such.
- See your own requests and their status (only yours, even if your Seerr account is an admin).
- Sign in with an API key, or with your Jellyfin username and password. Only the resulting session is kept, encrypted.

### Accounts and sign-in
- Finds Jellyfin servers on your network automatically. You can also type an address, and Nettle tries https, http and the default ports for you.
- Sign in with profile tiles, a password, or **Quick Connect**.
- **Multiple profiles** with a "Who's watching?" picker, quick switching, and optional automatic sign-in.
- **Passwords are never stored.** Only Jellyfin's session token is kept: on Windows it's encrypted with DPAPI for your Windows account, and on Linux it goes into your desktop keyring (GNOME Keyring, KWallet, KeePassXC…).

### Look and feel
- **Sixteen original themes:** Midnight, Obsidian (true black for OLED), Aurora, Ember, Sakura, Deep Sea, Evergreen, Neon Nights, Crimson, Slate, Mocha, Phosphor, High Contrast, and three light ones: Daylight, Lavender and Sandstone. Switching is instant.
- Smooth, lightweight animations, a navigation rail that expands over the page, and fade-in artwork. **Reduce motion** in Settings turns the animations off.
- **Ready for keyboard and TV remotes.** Arrow keys move focus the way a TV remote's d-pad does.
- Remembers its window size and position, and only one copy runs at a time (opening it again brings the open window forward).

## Install

### Windows

1. Download **`Nettle-win-Setup.exe`** from the [latest release](https://github.com/devall96/Slopfin/releases/latest).
2. Run it. It installs for your user account only, so no admin rights are needed, and adds Start-menu and Desktop shortcuts.
3. **About the Windows warning:** Nettle isn't code-signed yet, so Windows SmartScreen may say *"Windows protected your PC"*. Click **More info → Run anyway**. You only see this on the first install; updates don't trigger it.

Prefer not to install? Download **`Nettle-win-Portable.zip`**, extract it, and run `Nettle.exe`.

**Requirements**
- Windows 10 or 11 (64-bit).
- A Jellyfin server, 10.9 or newer.
- For HDR, an HDR display with **Settings → Display → Use HDR** turned on.

**Uninstall:** Windows Settings → Apps → Nettle → Uninstall. Your settings and saved sign-ins stay in `%LOCALAPPDATA%\Nettle`. Delete that folder too if you want everything gone.

### Linux

So far the Linux version has been tested mainly on Ubuntu 26.04 with GNOME and an AMD graphics
card. If you're on something else, please [report how it went](#-testers-wanted).

> [!WARNING]
> **The 0.3.0 Linux build is untested for a short while.** The Linux test bench is being
> reconfigured, so this release was only tested by hand on Windows. The Linux build still passes the
> automated decode tests on clean Ubuntu, Debian, Fedora and Arch machines. Hands-on Linux testing
> will follow soon.

1. Download **`Nettle.AppImage`** from the [latest release](https://github.com/devall96/Slopfin/releases/latest) and put it wherever you keep apps, for example `~/Applications`.
2. Make it executable: right-click → Properties → *Allow executing file as program*, or run `chmod +x Nettle.AppImage`.
3. Double-click it. On first run, Nettle offers to **add itself to your applications menu**, with its icon. This is also in **Settings → Desktop**. Nothing is installed system-wide and no root is needed.

mpv is built in, so you don't need to install anything else. It runs on 64-bit distros from 2024 onwards: Ubuntu 24.04+, Debian 13+, Fedora 40+, Arch and their derivatives.

*If double-clicking does nothing,* your distro may be missing FUSE 2, which AppImages use. Install `libfuse2` (Ubuntu/Debian: `sudo apt install libfuse2t64`) or run `./Nettle.AppImage --appimage-extract-and-run`.

**Uninstall:** in **Settings → Desktop**, press *Remove from menu*, then delete the AppImage. Settings and the image cache live in `~/.local/share/Nettle` and `~/.cache/Nettle`. Saved sign-ins are in your keyring under "Nettle".

## Using Nettle

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

Nettle contains **no telemetry, analytics, crash reporting, ads or tracking**. It only ever connects to:

- the Jellyfin, NostalgiaTV and Seerr servers *you* enter;
- your local network, when the sign-in screen looks for Jellyfin servers;
- your chosen update source, and only when you check for updates;
- TMDB, only if you turn on direct TMDB posters.

Other privacy details:
- Servers see the device as "Nettle (Windows)" or "Nettle (Linux)", never your PC's name.
- Images only load from the servers you configured.
- The player runs sealed off: it ignores any mpv config or scripts on your PC, never looks links up through youtube-dl, saves no watch history of its own, and identifies itself only as "Nettle".
- Diagnostic logs stay on your PC and have tokens masked.
- Even the build process has Avalonia's build statistics turned off.

> This program will not transfer any information to other networked systems unless specifically
> requested by the user or the person installing or operating it.

## Known limitations (0.3.0 beta)
- **Both platforms are beta.** Expect some bugs, and please report them.
- **Linux:**
  - It's untested so far on NVIDIA and Intel graphics, KDE and other desktops, and multiple monitors.
  - HDR is tone-mapped to SDR. Nettle's window runs through X11 (XWayland on Wayland desktops), which has no HDR path yet. The badge says so.
  - Video is drawn through OpenGL inside Nettle's own window. If your graphics driver can't do that, Nettle falls back to mpv's own window, or you can force that with `NETTLE_VIDEO=window`.
  - The AppImage carries mpv 0.37 (from Ubuntu 24.04); Windows builds use mpv 0.42.
  - If your desktop has no keyring at all, sign-ins are kept in a file only your user can read.
- **Audio passthrough** depends on your receiver and, on Linux, on your sound server allowing it for that output. It's off by default. On Windows it takes exclusive use of the audio device while playing.
- Not code-signed yet, so SmartScreen shows a warning on first install.
- Windows and Linux for now. The code is structured for Android TV and iOS versions later.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/).

```bash
git clone https://github.com/devall96/Slopfin.git
cd Slopfin
```

**Windows:** fetch libmpv once (a pinned, hash-checked build), then run:

```bash
powershell -ExecutionPolicy Bypass -File get-libmpv.ps1
dotnet run --project src/Nettle.Desktop
```

**Linux:** install libmpv from your distro (Ubuntu/Debian `sudo apt install libmpv2`, Fedora `sudo dnf install mpv-libs`, Arch `sudo pacman -S mpv`), then `dotnet run --project src/Nettle.Desktop`. Source builds use the system's libmpv; only the AppImage bundles its own.

The Linux AppImage is built by the **Linux AppImage** GitHub Actions workflow
(`.github/workflows/linux.yml`). It bundles libmpv, then decodes H.264, 4K HEVC 10-bit and AV1
test clips on clean Ubuntu, Debian, Fedora and Arch machines before anything is published.

To build a Windows release (installer, portable zip and update packages in `releases\`):

```bash
powershell -ExecutionPolicy Bypass -File build-release.ps1
```

**Contributing:** run `git config core.hooksPath .githooks` once after cloning. A pre-commit check then blocks commits that contain files outside the project or anything that looks like a token or a private server address. The `.gitignore` is an allowlist, so only Nettle's own files can be tracked.

`build-release.ps1` reads the version from `Directory.Build.props`. The app draws the installer splash art itself during the build.

**Project layout**

```
src/
  Nettle.Core      Platform-neutral: Jellyfin / Seerr / NostalgiaTV clients, models, settings, account vault
  Nettle.UI        Avalonia views + view models, themes, image cache, mpv player (shared by every platform)
  Nettle.Desktop   Windows + Linux app: window, secure storage (DPAPI / keyring), HDR detection,
                    libmpv loading, applications-menu entry, installer/updater hooks
```

## Code signing policy

Release builds are **not yet code-signed**. The plan is to apply to the free SignPath Foundation
programme for open-source projects. Until then, check that you downloaded Nettle from
[this repository's Releases page](https://github.com/devall96/Slopfin/releases).

**Team roles:** [@devall96](https://github.com/devall96) is the author, reviewer and release approver.

## License

Nettle is free software under the **[GNU General Public License v3.0](LICENSE)**. Third-party
components and their licenses are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

**Name and logo:** the "Nettle" name and sea nettle logo identify this project. Forks are welcome under the
GPL, but please use a different name and icon for your own builds.

Nettle is an independent project. It is not affiliated with or endorsed by Jellyfin, the mpv
project, Seerr, TMDB or NostalgiaTV.
**How it was made:** Nettle's code is entirely AI-written (Anthropic's Claude), directed, reviewed and
tested by a single person.
