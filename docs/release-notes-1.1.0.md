## Nettle 1.1.0

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/Nettle](https://devall96.github.io/Nettle/)** picks the right one for your device.
>
> - **Windows:** [**Nettle-win-Setup.exe**](https://github.com/devall96/Nettle/releases/download/v1.1.0/Nettle-win-Setup.exe) (installer) · or [Nettle-win-Portable.zip](https://github.com/devall96/Nettle/releases/download/v1.1.0/Nettle-win-Portable.zip) to run without installing
> - **Linux:** [**Nettle.AppImage**](https://github.com/devall96/Nettle/releases/download/v1.1.0/Nettle.AppImage)
> - **Android TV / Google TV:** [**Nettle-AndroidTV.apk**](https://github.com/devall96/Nettle/releases/download/v1.1.0/Nettle-AndroidTV.apk), or enter the code **3148086** in the Downloader app on your TV
>
> Already using Nettle? **Settings → Updates → Check now** installs this for you, on Windows, Linux and Android TV.
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**Simpler media tags with SDR and more, Skip Intro that shows up reliably, Live TV that finds NostalgiaTV on its own, and about fifty fixes** from a full review of the code on every platform.

### 🏷️ Media tags
- **Simpler:** when a video starts, small tags now sit in one row (`4K`, `HDR10`, `ATMOS 7.1`) instead of large cards with a sentence under each. A line of explanation only appears when something isn't reaching your screen (for example HDR on a display that's in SDR).
- **More of them:** `SDR`, `720p` and `SD` are tagged now, not only 1080p and up and HDR. Every audio track gets a tag with its channels (`AAC 2.0`, `DD+ 5.1`), and there is an optional tag for the video format (`HEVC`, `H.264`, `AV1`).
- **Your choice:** **Settings → Playback → Media tags** turns them off, or picks which ones show (resolution, HDR / SDR, video format, audio). `I` on the keyboard, or Info on the remote, still shows them again.
- 4:3 pictures are tagged by their height too, so 1440×1080 is **1080p** (it was 720p).

### ⏭️ Skip Intro and Skip Credits
- **The buttons were missing on some episodes.** If the server was briefly busy when an episode started, Nettle gave up and showed no Skip buttons for the whole episode. It now asks again.
- **Servers that publish only part of what Intro Skipper found** (credits but no intro) now get the missing intro from the plugin too.
- **A segment you had set to "Off" no longer hides a Skip button for another one** that overlaps it (an intro inside a recap, say).
- **With "Auto-play next episode" off, the Next button is back.** It was missing entirely; now only the automatic start follows the setting.

### 📡 Live TV
- **Up and Down now go the way the guide is listed:** Down moves to the channel below, Up to the one above. Channel + / − and Page Up / Down on a remote or keyboard are unchanged.
- **NostalgiaTV is found more reliably after signing in.** One quick look wasn't always enough over Tailscale or a VPN; Nettle now tries again a little later.
- **At the end of each programme**, Nettle used to reload the programme that was ending and then switch late. It now goes straight to the next one.
- **Live TV no longer plays a different show** when NostalgiaTV's item ID is out of date and several shows share a name, or when NostalgiaTV sends more than the one channel asked for.
- **A guide file that's missing or damaged** no longer takes the channel list down with it, and a connection that drops while the guide downloads no longer locks Live TV until a restart.
- **Removing NostalgiaTV in Settings** forgets its guide straight away (it stayed for up to 10 minutes).

### 📺 Android TV
- **Old DVDs and SD channels** with non-square pixels are shown at the right shape (they were squeezed).
- **The server connection comes back after the server restarts.** Before, remote control and watch together stopped working until Nettle was restarted.
- **Switching to a saved profile whose sign-in had expired** no longer signs out the person who is currently watching.
- **A finished episode running out while the next one loads** no longer closes the player or skips an item in a queue.
- **Pressing Rewind or Forward while a film is still opening** no longer jumps to the start and loses your resume point (Windows and Linux too).
- **The guide refreshing itself** while left open no longer jumps back to the start of the timeline.
- **If the server isn't reachable when Nettle starts**, Home and the menu fill in by themselves once it is, instead of staying empty until a restart.
- **More profiles or users than fit on the sign-in screens** can now be scrolled to.
- **Gamepad B** closes a menu or the keyboard first, like Back on the remote.
- Server replies are read off the main thread, so long library pages don't stall scrolling.
- Smaller fixes: "null" in episode labels for specials; the Live TV page could start several background refreshes; a Quick Connect code stayed on screen after checking stopped; a rare crash on the cast row.

### 🪟 Windows and Linux
- **Requests that stop halfway** (Wi-Fi drops mid-download) now give up after the normal timeout instead of leaving a spinner forever.
- **Closing a video** no longer leaves anything running that could switch your screen's HDR or refresh rate afterwards, and no longer keeps the closed player in memory.
- **Home and Live TV stop their background work while a video plays** (the banner rotation and the guide refresh), and pages you left before they finished loading no longer keep timers running.
- **Closing a video refreshed the page twice**; it's once now. Refreshing an item page only redraws the episodes that changed.
- **Scrolling a library while jumping to a letter** could show the same titles twice.
- **Seerr:** a mistyped password in Settings no longer breaks a sign-in that was working, and "you don't have permission" is no longer reported as wrong credentials.
- **Settings → Accounts → Forget** removes the profile from the list straight away.
- **Clicking X twice while closing** no longer skips saving your settings.
- Smaller fixes: the image cache can no longer keep a half-written picture; the "Tuning…" spinner no longer animates hidden behind every video.

### Under the hood
- Saving settings, the server connection's keep-alive, the log's privacy filter (it now also hides `X-Api-Key` values) and several other small things were made sturdier. Nettle still never converts (transcodes) a video and only contacts the servers you set up.

> [!NOTE]
> **How this was tested:** 109 automated tests on Windows and the Android core tests, then playback on the public Jellyfin demo server in the Windows app and on a Google TV 14 emulator: the new tags, closing the player and exiting. Not yet tried on a real TV box or on Linux; please [report anything odd](https://github.com/devall96/Nettle/issues/new?template=bug_report.md).

Found a bug? [Open an issue](https://github.com/devall96/Nettle/issues/new?template=bug_report.md).
