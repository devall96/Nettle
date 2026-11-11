## Nettle 1.0.2

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/Nettle](https://devall96.github.io/Nettle/)** picks the right one for your device.
>
> - **Windows:** [**Nettle-win-Setup.exe**](https://github.com/devall96/Nettle/releases/download/v1.0.2/Nettle-win-Setup.exe) (installer) · or [Nettle-win-Portable.zip](https://github.com/devall96/Nettle/releases/download/v1.0.2/Nettle-win-Portable.zip) to run without installing
> - **Linux:** [**Nettle.AppImage**](https://github.com/devall96/Nettle/releases/download/v1.0.2/Nettle.AppImage)
> - **Android TV / Google TV:** [**Nettle-AndroidTV.apk**](https://github.com/devall96/Nettle/releases/download/v1.0.2/Nettle-AndroidTV.apk), or enter the code **3148086** in the Downloader app on your TV
>
> Already using Nettle? **Settings → Updates → Check now** installs this for you, on Windows, Linux and Android TV.
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**A second Android TV fix: Nettle could close when a list on screen became empty,** for example when you cleared the search box. Nothing changes on Windows or Linux; they get the new number so every platform keeps sharing one version.

### 📺 Android TV: fixed a crash when a list empties
- **Nettle closed when a list that had items in it became empty.** It was reported when sorting a library, which empties the page before filling it again. Search did it too: type something that finds results, then delete the text.
- **This one affected every Android version,** not only older TVs.
- **Why:** Nettle builds the cards just off screen ahead of time, so moving with the remote stays smooth. The Google library that does this (Jetpack Compose) had a bug in exactly that feature: it asked for the last visible item of a list that no longer had any. Google fixed it in a newer version of the library, and Nettle now uses that version (Compose 1.11.4 instead of 1.10.6).
- **Found thanks to 1.0.1's new "Nettle closed unexpectedly" screen.** The same Reddit user who reported the first crash sent a photo of it from their Hisense TV, and it named the exact line. That photo also confirmed that the 1.0.1 fix works on a real Android 9 TV.

### Windows and Linux
- No changes. 1.0.2 is the same app as 1.0.1.

> [!NOTE]
> **Tested on the Android 9 TV emulator:** 1.0.1 closes there with exactly this error when the search box is cleared after results; 1.0.2 doesn't. 1.0.2 was also taken through sign-in, Home, Search, the Movies and Shows libraries, a library filter, an item page and playing an episode. It hasn't been run on a real TV yet.

Found a bug? [Open an issue](https://github.com/devall96/Nettle/issues/new?template=bug_report.md).
