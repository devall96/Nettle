## Nettle 0.5.0

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/Nettle](https://devall96.github.io/Nettle/)** picks the right one for your device.
>
> - **Windows:** [**Nettle-win-Setup.exe**](https://github.com/devall96/Nettle/releases/download/v0.5.0/Nettle-win-Setup.exe) (installer) · or [Nettle-win-Portable.zip](https://github.com/devall96/Nettle/releases/download/v0.5.0/Nettle-win-Portable.zip) to run without installing
> - **Linux:** [**Nettle.AppImage**](https://github.com/devall96/Nettle/releases/download/v0.5.0/Nettle.AppImage)
> - **Android TV / Google TV:** [**Nettle-AndroidTV.apk**](https://github.com/devall96/Nettle/releases/download/v0.5.0/Nettle-AndroidTV.apk)
>
> Already using Nettle? **Settings → Updates → Check now** installs this for you, on Windows, Linux and Android TV.
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

Picture and sound in sync on every TV, scrubbing that works the way TV streaming apps do, and a calmer Settings page. Windows, Linux and Android TV all get the same features and share this version number. **Linux was tested by hand for this release** (Ubuntu 26.04, GNOME on Wayland).

### 🔊 Picture and sound in sync, on any TV
Some TVs delay their picture without delaying their sound to match, often only in their film (24 Hz) mode. On one TCL TV the sound came out **207 ms early in film mode** and 43 ms early at 60 Hz, which is very easy to see on people's lips.
- **New: Settings → Playback → Lip sync check.** A small grey box blinks in the middle of the screen once a second, with a beep. Press **←** / **→** (Left / Right on a remote) until they happen at the same moment, then **Back** / **Esc**. Each press moves the sound by 10 ms.
- **Audio timing is saved for your screen, separately for each refresh rate.** Nettle switches to the right value by itself whenever the TV or monitor changes rate (Match frame rate on Android TV, exclusive full screen on Windows), so films and everything else are both in sync.
- On Android TV there are two checks: **Usual rate**, and **for films (24 Hz)**, which switches the TV to its film mode first.
- The **Audio timing** row in the player's track panel now says which rate it's for (e.g. *Audio timing · 24 Hz*), moves in 10 ms steps, and stays set between files (it used to reset). "Reset to normal" leaves it alone, since it belongs to your screen.
- It works for decoded audio and for Dolby / DTS passthrough alike: Nettle moves the picture against the sound, never the sound itself.

> [!WARNING]
> **Epilepsy warning:** the lip sync check blinks a grey box once a second for as long as it's open. If you or anyone watching (pets included) has epilepsy or is sensitive to flashing or blinking light, don't run it. The same warning is shown next to the check in Settings. It's a small mid-grey box (about 1/16 of the screen, never white, never full screen) and stays well under the "three flashes a second" limit of the WCAG accessibility guidelines.

### ⏩ Scrubbing, the way TV streaming apps do it
- **Press ← / → while something plays** (Left / Right on a remote): the video pauses and a preview moves along the seek bar, with the thumbnail, the time and the chapter name.
  - **A tap** moves it 10 seconds (the *Seek step* in Settings).
  - **Holding** glides it smoothly, faster the longer you hold: about 30 seconds of video per second at first, doubling every second, so even a whole film takes only a few seconds.
- **Enter / OK / Play** jumps there and carries on playing. **Esc / Back** cancels and carries on from where you were.
- With a mouse, the seek bar works as before: hover for thumbnails, drag and let go to jump.
- **Fixed:** holding a key used to lurch (a 10 s jump, then a bigger one, then racing ahead at over an hour of video per second), and on Android TV a seek could get stuck, with the preview frozen on screen and the controls not responding. On Windows and Linux the seek bar could also jump back to where you were before landing.
- On Android TV, jumps after holding land on the nearest keyframe, which is instant even on 4K remuxes.

### 📺 Android TV
- **Settings work with the remote:** dropdowns and number settings used to be skipped when moving down the page. Every row now takes focus as a whole, like the switches: **OK** opens a list, **Left / Right** change a number.
- **No more volume slider in the player.** Your remote sets the TV's (or receiver's) own volume; a second slider in the app only made things quieter by surprise. Nettle now always plays at full level.
- **Closing Nettle:** press **Back** twice on the home screen ("Press Back again to close Nettle"). It used to only go to the background.

### 🎨 Settings, tidier
- **The themes have their own screen:** Settings → Appearance → **Change theme**. They no longer push everything else down the page. **Done** or **Back** / **Esc** returns.
- **Linux:** "Add to applications menu" is only offered when Nettle runs as the AppImage.

### 🐧 Linux fixes, from testing by hand
- **The screen now stays on during playback on GNOME (Wayland).** Where Nettle's own request to the desktop fails (seen on Ubuntu 26.04), it uses `gnome-session-inhibit` or `systemd-inhibit` instead. The screen may sleep again when you pause or close the player, and even if Nettle crashes.
- **Lip sync check keys:** ← / → reach the check before any button or the seek bar can take them.
- Tested by hand: sign-in, playback, keep-awake, chapters, number keys, scrubbing, display modes and F11, the new theme screen, the applications menu entry and Settings → Updates.

### Also
- Chapter jumps, number-key jumps and scrubbing are written to the log, which makes bug reports easier to check.
- New automated tests check that Windows, Linux and Android TV keep the same features, and that the lip sync check stays gentle and keeps its warning.

### Install
- **Windows:** run `Nettle-win-Setup.exe`. It installs for your user only, no admin rights needed. If Windows shows *"Windows protected your PC"* (Nettle isn't code-signed yet), click **More info → Run anyway**.
- **Linux:** make `Nettle.AppImage` executable (`chmod +x Nettle.AppImage`, or *Properties → Allow executing as program*) and double-click it.
- **Android TV:** **Settings → Updates → Check now** if you're on 0.4.3. New install: in the **Downloader** app, enter the code **3148086** ([full steps](https://github.com/devall96/Nettle#android-tv)).

**Privacy:** no new connections. The lip sync check plays a clip stored inside the app.

> [!NOTE]
> **Known limitations:** on Linux, Nettle can't read the screen's refresh rate, so audio timing is one value for all rates there, and exclusive full screen doesn't switch refresh rates. The Android TV app has no subtitle timing control yet (Windows and Linux do). Linux is tested on AMD graphics and GNOME so far; reports from NVIDIA, Intel, KDE and others are very welcome.

Found a bug? [Open an issue](https://github.com/devall96/Nettle/issues).
