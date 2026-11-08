## Nettle 0.4.3 · parity hotfix (Windows & Linux)

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> - **Windows:** [**Nettle-win-Setup.exe**](https://github.com/devall96/Nettle/releases/download/v0.4.3/Nettle-win-Setup.exe) (installer) · or [Nettle-win-Portable.zip](https://github.com/devall96/Nettle/releases/download/v0.4.3/Nettle-win-Portable.zip) to run without installing
> - **Linux:** [**Nettle.AppImage**](https://github.com/devall96/Nettle/releases/download/v0.4.3/Nettle.AppImage)
> - **Android TV / Google TV:** [**Nettle-AndroidTV.apk**](https://github.com/devall96/Nettle/releases/download/v0.4.3/Nettle-AndroidTV.apk) (unchanged from 0.4.2; Downloader code below still works)
>
> Already on Windows or Linux? **Settings → Updates → Check now** installs this for you.
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

A hotfix that brings the **Windows and Linux** apps level with the new Android TV app, and fixes the desktop window. Windows and Linux share this version number; the Android TV app stays at 0.4.2 (its APK is attached here too, so the install link keeps working).

### 🪟 The window, fixed and with real display modes
- **Fixed:** Nettle could open half off the screen, and then couldn't be moved or resized until you pressed F11 a few times.
- **New: Settings → Appearance → Display mode**, with three choices:
  - **Windowed**: a normal window with the system's title bar and borders. Drag it, resize it from any edge, make it as small or as large as you like. Below 1280 × 720 the whole app scales down to fit instead of getting cut off.
  - **Borderless full screen**: covers the monitor, easy to alt-tab out of.
  - **Exclusive full screen**: stays on top, and on Windows the monitor's **refresh rate follows the video** (23.976 / 24 / 25 / 50 / 59.94 Hz…) so films don't judder, the same as the TV app. Your normal refresh rate comes back when you leave full screen, switch to another app, or close Nettle.
- **F11** switches between windowed and the full-screen type you picked. Nettle opens in the mode you chose.

### 📺 Live TV, now the same as on the TV
- **Tuning screen:** every channel switch is covered with the channel's logo, number and what's on until the new picture is up, so letterboxed films never flash stretched.
- **Channel logo with the format badges:** the logo sits on top of the 4K · HDR10 · ATMOS badges and fades with them.
- **Type a channel number** with the number keys (top row or numpad). It tunes after a short pause, or straight away on Enter.
- **Last channel:** press **R**, or use the new button next to the channel arrows.
- **Number keys on videos:** 0–9 jump to 0–90 % of the way in.

### Install
- **Windows:** run `Nettle-win-Setup.exe`. It installs for your user only, no admin rights needed. If Windows shows *"Windows protected your PC"* (Nettle isn't code-signed yet), click **More info → Run anyway**.
- **Linux:** make `Nettle.AppImage` executable (`chmod +x Nettle.AppImage`, or *Properties → Allow executing as program*) and double-click it.
- **Android TV:** in **Downloader**, enter `https://github.com/devall96/Nettle/releases/latest/download/Nettle-AndroidTV.apk` (full steps in the [0.4.2 notes](https://github.com/devall96/Nettle/releases/tag/v0.4.2)).

> [!WARNING]
> **Linux is still untested by hand** in this release. The AppImage passes the automated tests on clean Ubuntu, Debian, Fedora and Arch machines. Exclusive full screen on Linux asks the compositor to step aside; it doesn't switch refresh rates there yet. A [test report](https://github.com/devall96/Nettle/issues/new?template=bug_report.md) would really help.

Found a bug? [Open an issue](https://github.com/devall96/Nettle/issues).
