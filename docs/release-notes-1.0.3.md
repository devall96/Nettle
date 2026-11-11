## Nettle 1.0.3

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/Nettle](https://devall96.github.io/Nettle/)** picks the right one for your device.
>
> - **Windows:** [**Nettle-win-Setup.exe**](https://github.com/devall96/Nettle/releases/download/v1.0.3/Nettle-win-Setup.exe) (installer) · or [Nettle-win-Portable.zip](https://github.com/devall96/Nettle/releases/download/v1.0.3/Nettle-win-Portable.zip) to run without installing
> - **Linux:** [**Nettle.AppImage**](https://github.com/devall96/Nettle/releases/download/v1.0.3/Nettle.AppImage)
> - **Android TV / Google TV:** [**Nettle-AndroidTV.apk**](https://github.com/devall96/Nettle/releases/download/v1.0.3/Nettle-AndroidTV.apk), or enter the code **3148086** in the Downloader app on your TV
>
> Already using Nettle? **Settings → Updates → Check now** installs this for you, on Windows, Linux and Android TV.
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**A big Android TV update: a player that's easier to drive with a remote, chapter buttons, two more crash fixes, and a long list of smaller fixes** from an overnight test run on every Android TV version from 9 to 16. Most of the player changes come straight from a Reddit user's feedback; thank you, Wuuden. Nothing changes on Windows or Linux; they get the new number so every platform keeps sharing one version.

### 📺 The player, easier with a remote
- **OK pauses on the first press.** Before, the first press only showed the controls.
- **All the controls are in one row**, so Left and Right reach every button: ⏮ chapter, ⏪, ▶, ⏩, chapter ⏭, then Next episode, with Subtitles & audio at the right end, under the time.
- **New chapter buttons.** Files with chapters get ⏮ / ⏭ either side of Rewind and Forward. (Channel and Page Up / Down still skip chapters too.) Next episode has its own icon now, so the two aren't confused.
- **"Ends at" sits next to the time left.**
- **Up Next is easy to dismiss.** While the card is up, Right moves to Hide instead of skipping forward in the video, OK presses the button, and Back hides the card instead of closing the player.
- **Skip intro is focused when it appears**, so one OK skips.
- **The Back arrow at the top no longer takes focus.** The remote's Back key does the same thing.
- **The preview while you scrub was squashed into a thin strip.** It's now a proper thumbnail above the seek bar, on every server.
- **The format badges** (4K, HDR, audio) no longer look selected: they had the same coloured border as a focused button.
- **Dolby Vision on a TV without it is badged as HDR10+ (or HDR10)**, converted by the TV box, instead of warning that it may be shown in SDR.
- **When the TV can't decode a video** (AV1, 10-bit HEVC, MPEG-2 on many TVs), Nettle says so instead of playing sound over a black screen. Nettle never converts files, so the fix is a different version of the file or a TV that decodes it.
- **Wi-Fi drops:** Nettle now keeps trying for about 2 minutes, then offers **Connection lost → Try again**, which picks up where it stopped. Before, it gave up after 30 seconds with a misleading "format" message.
- **Error boxes need one OK, not two.**
- **Subtitles** are named by their format ("English · SRT", not "X-MEDIA3-CUES") and move up out of the way while the controls are showing.
- **Up Next no longer goes missing** for a whole episode when looking up the next one failed once; Nettle tries again.

### 📺 Crashes fixed
- **Searching while the server was busy or unreachable closed Nettle.** Now you get a short message instead.
- **Picking your profile on "Who's watching?" while the server was unreachable** (asleep, or Tailscale off) closed Nettle.
- **Choosing a library filter or sort no longer empties the page while it reloads.** 1.0.2 already fixed the crash this caused (Wuuden's "pressed All in the filter twice" photo was from 1.0.1); the old cards now stay up until the new ones arrive, and picking the option that's already chosen does nothing.

### 📺 Moving around
- **Rows remember which card you were on** when you go up, down or to the menu and back.
- **Library:** Left from the A–Z bar goes back to the grid instead of the menu, and Down from the filters returns to the card you came from.
- **Closing the lip sync check, or leaving a text box with Back,** puts you back where you were, not in the side menu.
- **The side-menu buttons have names for screen readers** (TalkBack).
- **An item page that fails to load** says "Couldn't load this" with Try again and Back, instead of staying blank.
- **Light themes (Daylight, Lavender, Sandstone):** "More info", the banner arrows, the Back arrow and white title logos were white on white, and text over bright artwork was hard to read. Both fixed.

### Windows and Linux
- No changes. 1.0.3 is the same app as 1.0.2.

> [!NOTE]
> **How this was tested:** on Android TV emulators for Android 9, 10, 11 (720p, 1 GB), 12, 13 (4K), Google TV 14 and Google TV 16, with a new set of automated tests that walk every screen with the remote, check where focus goes, play videos, open Up Next and Skip intro, press every Settings row, and screenshot every theme. Plus about 200,000 random remote presses, simulated server outages and Wi-Fi drops. Then on a real onn 4K (Google TV 14): the library filter, OK to pause, the new button row, both chapter buttons, and a 4K Dolby Vision film with Atmos, which played as HDR10+ on a TV without Dolby Vision.

Found a bug? [Open an issue](https://github.com/devall96/Nettle/issues/new?template=bug_report.md).
