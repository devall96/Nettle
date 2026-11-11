# Microsoft Store listing for Nettle

Text to paste into the Partner Center submission. Section names follow Partner Center.

## Properties

- **Category:** Entertainment
- **Privacy policy URL:** https://devall96.github.io/Nettle/privacy.html
- **Website:** https://devall96.github.io/Nettle/
- **Support contact info:** https://github.com/devall96/Nettle/issues

## Store listing (English, United States)

### Product name

Nettle

### Short description

A fast, private client for your own Jellyfin server. Nettle plays the original file in full quality and never asks your server to transcode.

### Description

Nettle is a client for your own Jellyfin media server. It plays the actual file in its original quality, using the mpv engine, so 4K, HDR and Dolby Vision arrive on your screen untouched and your server's processor stays idle.

You need a Jellyfin server to use Nettle. It does not come with any films, shows or channels of its own.

ORIGINAL QUALITY, ALWAYS
Nettle never transcodes. There is no setting for it and no code that asks the server to. On an HDR screen, Windows HDR turns on for HDR videos and off again for everything else, the way a TV does it. Audio passthrough can send Dolby and DTS sound to your receiver as it is.

MADE FOR THE BIG SCREEN
Home, libraries, details and search with streaming-app polish. Skip intros and credits where your server provides them. Watch together with friends through Jellyfin SyncPlay. Several people can keep their own sign-in and switch from a profile picker.

LIVE TV AND REQUESTS
If you run NostalgiaTV, Nettle shows its live channels in a TV-style guide. If you run Seerr, you can ask for new films and shows from inside Nettle.

PRIVATE BY DESIGN
No telemetry, analytics, ads or tracking of any kind. Nettle only connects to the servers you enter. Sign-ins are saved encrypted for your Windows account only.

SIXTEEN THEMES
From true black for OLED screens to bright daylight, each one made by hand.

GOOD TO KNOW
Nettle is open source (GPL-3.0) and free. All of its code was written by an AI coding assistant, directed and reviewed by one person, and it has been tested on a small number of devices so far. Bug reports are welcome on GitHub.

Nettle is an independent project. It is not affiliated with or endorsed by Jellyfin, mpv, Seerr or NostalgiaTV.

### What's new in this version

First release on the Microsoft Store. Windows keeps this copy up to date through the Store.

### Product features

- Plays the original file with mpv: no transcoding, ever
- 4K, HDR10, HLG and Dolby Vision, with hardware decoding
- Windows HDR switches on and off with the video
- Audio passthrough for Dolby and DTS to your receiver
- Skip intros and credits
- Watch together with Jellyfin SyncPlay
- Multiple profiles with saved, encrypted sign-ins
- Live TV guide for NostalgiaTV
- Film and show requests through Seerr
- Sixteen hand-made themes
- No telemetry, ads or tracking

### Search terms

- jellyfin
- jellyfin client
- media player
- mpv
- home media server
- 4k hdr player
- dolby vision

### Copyright and trademark info

Copyright (c) Nettle contributors. Jellyfin is a trademark of its owners; Nettle is not affiliated with Jellyfin.

### Additional license terms

Nettle is free software under the GNU General Public License, version 3: https://github.com/devall96/Nettle/blob/main/LICENSE
Licenses of the open-source parts it includes: https://github.com/devall96/Nettle/blob/main/THIRD_PARTY_NOTICES.md

### Screenshots (docs/images, 1440 x 900)

1. home.png: "Home: continue watching and what's new on your server"
2. details.png: "Details, with format badges for resolution, HDR and audio"
3. library.png: "Browse a whole library"
4. livetv.png: "A TV-style guide for NostalgiaTV channels"
5. signin.png: "Sign in to your own server"

## Submission options

### Restricted capabilities: why does the product need runFullTrust?

Nettle is a classic desktop (Win32) application written in .NET with the Avalonia UI framework, packaged as MSIX. It plays video through the native libmpv library with Direct3D 11, and uses desktop APIs to switch Windows HDR on and off for the video being played. These need a full-trust desktop process, so the app cannot run in the UWP sandbox. It does not use full trust for anything else: it installs no services or drivers and needs no administrator rights.

### Notes for certification

Nettle is a client for Jellyfin media servers, so it needs a server to sign in to. Please use the Jellyfin project's public demo server:

- Server address: https://demo.jellyfin.org/stable
- User name: demo
- Password: leave empty

Steps: start Nettle, choose to add a server by address, enter the address above, then sign in as "demo" with no password. Open any film from Home and press Play to test playback.

The app contains no media of its own, no purchases, no ads and no data collection. It only connects to the server the user enters. Updates are delivered by the Store; the app's own updater is disabled in this package.
