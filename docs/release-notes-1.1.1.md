## Nettle 1.1.1

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/Nettle](https://devall96.github.io/Nettle/)** picks the right one for your device.
>
> - **Windows:** [**Nettle-win-Setup.exe**](https://github.com/devall96/Nettle/releases/download/v1.1.1/Nettle-win-Setup.exe) (installer) · or [Nettle-win-Portable.zip](https://github.com/devall96/Nettle/releases/download/v1.1.1/Nettle-win-Portable.zip) to run without installing
> - **Linux:** [**Nettle.AppImage**](https://github.com/devall96/Nettle/releases/download/v1.1.1/Nettle.AppImage)
> - **Android TV / Google TV:** [**Nettle-AndroidTV.apk**](https://github.com/devall96/Nettle/releases/download/v1.1.1/Nettle-AndroidTV.apk), or enter the code **3148086** in the Downloader app on your TV
>
> Already using Nettle? **Settings → Updates → Check now** installs this for you, on Windows, Linux and Android TV.
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**A small update that changes three defaults:** subtitles stay off until you pick one, "Are you still watching?" is off unless you turn it on, and Live TV no longer pauses. The same on Windows, Linux and Android TV.

### 💬 Subtitles are off until you want them
- **Videos no longer start with subtitles showing.** Before, Nettle followed your Jellyfin profile (or the file's default track), so some videos started with subtitles on.
- **To get them,** pick a track in the player, as before. A subtitle language you pick for a series is still remembered for its other episodes.
- **To have them turn on by themselves,** choose how in **Settings → Playback → Subtitles turn on**: the same as your Jellyfin profile, the file's default track, always, only forced (foreign dialogue), or smart (when the audio is in another language).
- If you had already chosen one of those in Settings, it is kept.

### ⏸️ "Are you still watching?" is opt-in
- **The question is off unless you turn it on** in **Settings → Playback**. It used to be on for everyone.
- **This update turns it off once for everyone,** including people who already had Nettle. If you want it, switch it on again after updating; your episode count is kept.

### 📡 Live TV doesn't pause
- **The play / pause button is gone from the Live TV controls,** and Space, K and the play / pause keys on a remote only bring the controls up. A live channel keeps playing, like a real one.
- **Android TV:** pressing Home still silences the channel, and it now returns to the live point by itself when you come back to Nettle.

> [!NOTE]
> **How this was tested:** 111 automated tests on Windows and the Android core tests, and a build of every app. These three changes were not tried by hand in a running app before release; please [report anything odd](https://github.com/devall96/Nettle/issues/new?template=bug_report.md).

Found a bug? [Open an issue](https://github.com/devall96/Nettle/issues/new?template=bug_report.md).
