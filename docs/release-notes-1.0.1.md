## Nettle 1.0.1

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/Nettle](https://devall96.github.io/Nettle/)** picks the right one for your device.
>
> - **Windows:** [**Nettle-win-Setup.exe**](https://github.com/devall96/Nettle/releases/download/v1.0.1/Nettle-win-Setup.exe) (installer) · or [Nettle-win-Portable.zip](https://github.com/devall96/Nettle/releases/download/v1.0.1/Nettle-win-Portable.zip) to run without installing
> - **Linux:** [**Nettle.AppImage**](https://github.com/devall96/Nettle/releases/download/v1.0.1/Nettle.AppImage)
> - **Android TV / Google TV:** [**Nettle-AndroidTV.apk**](https://github.com/devall96/Nettle/releases/download/v1.0.1/Nettle-AndroidTV.apk), or enter the code **3148086** in the Downloader app on your TV
>
> Already using Nettle? **Settings → Updates → Check now** installs this for you, on Windows, Linux and Android TV.
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**A fix for Android TV on Android 9, 10, 11 and 12, where Nettle closed right after signing in, and a screen that tells you why if Nettle ever closes unexpectedly.** Nothing changes on Windows or Linux; they get the new number so every platform keeps sharing one version.

### 📺 Android TV: fixed a crash on Android 9 to 12
- **Nettle closed as soon as you signed in, and then every time you opened it,** on TVs and boxes running Android 9, 10, 11 or 12. That includes many TVs with Android TV built in (Hisense, Sony, TCL, Philips…) and the NVIDIA Shield.
- **Why:** the app used a system function that only exists on Android 13 and newer, in the code that builds every request to your server. Signing in didn't use it, so the sign-in screen worked; the first request after it didn't. Nettle had only been tested on an Android 14 box, where the function exists.
- **If you were affected, install this version by hand once:** in the **Downloader** app, enter the code **3148086** again ([full steps](https://github.com/devall96/Nettle#android-tv)). The broken version can't get far enough to update itself. Your saved sign-in is kept.
- **So it can't come back:** the shared code is now checked, every time Nettle is built, against what Android 9 has. A call that older TVs don't have stops the build instead of reaching your TV.
- Thank you to the Reddit user who tried Nettle on a Hisense TV and reported it.

### 📺 Android TV: Nettle now says why it closed
- **New: if Nettle ever closes unexpectedly, the next start tells you why.** Instead of going straight back to the screen that failed, it opens on a "Nettle closed unexpectedly last time" screen with the error, and two ways on: **Try again**, or **Go to the sign-in screen** without opening the last profile. Before, a crash on Home repeated on every start, and the only way out was clearing the app's data in the TV's settings.
- **It makes problems reportable from a TV.** Take a photo of that screen and post it in an [issue](https://github.com/devall96/Nettle/issues/new?template=bug_report.md): it names the Nettle version, the Android version, the device and where the error happened.
- **Nothing is sent anywhere.** The error is saved in the app's private folder on the TV and only ever shown on that screen. Sign-in tokens in it are masked; it may show your server's address.
- Errors now show real names (which part of Nettle, which line) instead of scrambled ones. The app file is about 0.5 MB larger for it.

### Windows and Linux
- No changes. 1.0.1 is the same app as 1.0.0.

> [!NOTE]
> **Tested on an Android 9 emulator:** 1.0.0 closes there right after sign-in with exactly this error, and 1.0.1 signs in, shows Home and reopens into it. It hasn't been run on a real Android 9 to 12 TV yet, so if you have one, a report either way is very welcome.

Found a bug? [Open an issue](https://github.com/devall96/Nettle/issues/new?template=bug_report.md).
