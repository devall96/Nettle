## Nettle 0.6.0

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/Nettle](https://devall96.github.io/Nettle/)** picks the right one for your device.
>
> - **Windows:** [**Nettle-win-Setup.exe**](https://github.com/devall96/Nettle/releases/download/v0.6.0/Nettle-win-Setup.exe) (installer) · or [Nettle-win-Portable.zip](https://github.com/devall96/Nettle/releases/download/v0.6.0/Nettle-win-Portable.zip) to run without installing
> - **Linux:** [**Nettle.AppImage**](https://github.com/devall96/Nettle/releases/download/v0.6.0/Nettle.AppImage)
> - **Android TV / Google TV:** [**Nettle-AndroidTV.apk**](https://github.com/devall96/Nettle/releases/download/v0.6.0/Nettle-AndroidTV.apk)
>
> Already using Nettle? **Settings → Updates → Check now** installs this for you, on Windows, Linux and Android TV.
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**Watch together** with friends and family on any of your screens, **HDR that switches on and off with the video** on Windows, a calmer player, and a long list of fixes from a full review of all three apps. Windows, Linux and Android TV all get the same features and share this version number. **This release was tested on all three at once:** one watch-together group with a Windows PC, an Ubuntu PC and an onn 4K Google TV box.

### 👥 Watch together (SyncPlay)
- **New: Watch together** on the rail. **Start a group** or **join** one, then play anything from your libraries: it starts for everyone in the group at the same moment.
- **Pause, play, seek and skip happen for everyone,** whoever presses them, on a PC or with a TV remote.
- Each player waits until everyone has loaded, then they start together. Nettle measures the difference between each device's clock and your server's, so devices whose clocks disagree still start at the same moment.
- **Players that drift apart are brought back in step:** a small gap is closed by playing very slightly faster or slower for a few seconds (you won't notice), a big one by a jump.
- It's Jellyfin's own SyncPlay, so Nettle can share a group with other Jellyfin apps that support it. Each person needs SyncPlay allowed in their Jellyfin user settings (it is by default).
- *Watching together · 2* in the player shows how many are in the group.

### 🌈 HDR follows the video on Windows
- **New: Settings → Appearance → Match HDR to the video.** Like a TV, Windows' HDR turns **on for HDR videos** and **off for SDR ones**, and goes back to how it was when you leave the player.
- Only on screens that can do HDR. The screen goes dark for a moment when it switches, and the video starts after that.
- If Nettle closes unexpectedly while it had switched your screen, it puts it back the next time it starts.
- It's on by default; turn it off there if you prefer to manage HDR yourself.

### 🎬 A calmer player
- **The mouse pointer hides with the controls** while a video plays, and comes back as soon as you move the mouse.
- **The controls no longer pop up by themselves.** Windows reported "the mouse moved" whenever the controls' window changed, and keys the player doesn't use (Shift, Ctrl, the Windows key, the keyboard's volume keys) brought them up too.
- **Live TV:** the channel now shows once, on the tuning screen and then in the controls bar, without two extra boxes in the top right. **Format badges** (4K, HDR, Atmos…) moved to the **bottom left**, above the controls, on every platform.
- **Up Next no longer cuts off the end** of episodes that have no credits marker: the card counts down to the real end of the file.
- **Reconnects by itself** at the same spot after a short network drop (Wi-Fi, a router or server restart), instead of ending the video. A Live TV feed that stops is tuned in again once.
- **Remembers your languages per series:** pick Japanese audio with English subtitles in one show, and its other episodes start that way.
- No more freeze at the start of files with external subtitles (Windows and Linux).

### 📺 Android TV
- **Fixed: videos were cut and silently reconnected every 30 seconds** (the player used a connection limit meant for quick requests). Live TV feeds restarted with it.
- **Fixed: old MPEG-4 / DivX / Xvid videos showed as green stripes** on boxes like the onn 4K, whose hardware decoder gets that format wrong. They're now decoded in software, which is easily fast enough for them.

### 🔒 Privacy and security
- **Seerr sign-ins are now per profile.** Before, one Seerr sign-in was shared by everyone on the device, so a second profile could see and make requests as the first person (an admin's requests are approved straight away). After updating, the existing sign-in belongs to the first profile that opens Nettle; others sign in to Seerr once from Settings.
- **Linux: HTTPS certificates are now checked for video too.** The Linux player's network code didn't check them by default, and the video request carries your sign-in token. (Windows already checked.)
- **Your build machine's folder names are no longer inside the app files.** Windows builds up to 0.5.0 contained the path of the folder they were built in, including the builder's Windows user name. 0.6.0 replaces it with a neutral placeholder.
- **Forgetting a profile also signs it out on its server,** even when it isn't the profile in use.
- **Plain http to a public IPv6 address now gets the same warning** as plain http to any other public address.

### 🐛 Fixes
- **Live TV guide (Windows and Linux):** episode titles and categories were missing, and the program right after one outside the guide's time range was dropped, sometimes the one airing now.
- **Switching to a profile whose server is unreachable** left Nettle showing one person while talking to the server as another. It now stays on the current profile.
- Skip buttons from the previous episode could apply to the next one after a quick switch.
- A plain DTS track whose title contained "ma" (e.g. "German") was badged as DTS-HD MA. Newer Dolby Vision types from recent Jellyfin versions now get their badge.
- A Quick Connect code left waiting could sign in a second time after a password sign-in.
- Double-clicking the video for full screen no longer also pauses it.
- Channel names containing commas were cut short.
- Exclusive full screen (Windows) now uses plain 24 Hz for 23.976 fps films when the monitor has no 23.976 Hz mode.
- A slow server now shows "The server took too long to respond" instead of an empty page.
- An unexpected error in one button or page no longer closes the whole app (Windows and Linux).

### ⚡ Faster
- Home loads once at start instead of twice. The A–Z jump fetches everything up to that letter in one request. Library pages after the first skip the server's counting query.
- My Requests looks each title up only once. Scrubbing thumbnails use about half the memory. The guide refreshes itself when it's been left open a while.
- Server discovery now asks every local network, so a VPN can't hide your server.

### Also
- **Google Play:** the Android TV app can now be built without its updater (`gradlew :app:bundlePlay`), as Play requires. The APK here keeps the updater.
- Android TV builds are now checked automatically on every change, alongside Windows and Linux. Over 45 new automated tests, for the fixes above and for watch together.

### Install
- **Windows:** run `Nettle-win-Setup.exe`. It installs for your user only, no admin rights needed. If Windows shows *"Windows protected your PC"* (Nettle isn't code-signed yet), click **More info → Run anyway**.
- **Linux:** make `Nettle.AppImage` executable (`chmod +x Nettle.AppImage`, or *Properties → Allow executing as program*) and double-click it.
- **Android TV:** **Settings → Updates → Check now**. New install: in the **Downloader** app, enter the code **3148086** ([full steps](https://github.com/devall96/Nettle#android-tv)).

**Privacy:** no new connections. Watch together only talks to your own Jellyfin server, and HDR switching happens on your PC.

> [!NOTE]
> **Known limitations:** Match HDR to the video is new, so if your screen misbehaves, turn it off and please open an issue. Watch together was tested between Nettle apps; reports from groups with other Jellyfin apps are very welcome. On Linux, HDR still isn't possible: Nettle's window runs through XWayland, which has no HDR path, so switching the desktop to HDR wouldn't make videos HDR.

Found a bug? [Open an issue](https://github.com/devall96/Nettle/issues).
