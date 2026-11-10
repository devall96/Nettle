# Native Android TV app (Kotlin + Compose): plan

Status: **shipped.** First public release in 0.4.2 (2026-11-07); built-in updater since 0.4.3;
same features and version number as Windows and Linux since then (0.5.0 at the time of writing).
This page is the original plan, kept as design notes; the README describes what the app does today.

Decisions (2026-11-07):
- Canvas 1440 units wide by default, with an "Interface size" setting (1280 / 1440 / 1600 / 1920).
- **Music and music videos are both hidden everywhere.**
- ~~No updater for now (APKs installed by hand).~~ Since 0.4.3: the same opt-in Settings → Updates
  as desktop (GitHub Releases, `REQUEST_INSTALL_PACKAGES`, Android's own install prompt).
- No FFmpeg audio decoder in the first builds; with passthrough off, a track the box can't decode
  falls back to another audio track, with a message. Can be added later.
- Same version number as Windows and Linux: the Android build reads `<Version>` from
  `Directory.Build.props` (versionCode = major·10000 + minor·100 + patch).
- Toolchain: AGP 8.13, Gradle 8.14.3, Kotlin 2.3, JDK 17, compileSdk 36, Media3 1.11.1.

It replaced an earlier experiment that ran the Avalonia UI on Android (never released). It is written natively in Kotlin + Jetpack Compose so it starts and scrolls like a
native TV app. The goal is **1:1 parity with the Windows app, and the same look**: layouts,
spacing, type, cards, rail, player OSD, badges, and all 16 themes with their exact colours.

The C# code can't run here, so `Nettle.Core` (Jellyfin, Seerr and NostalgiaTV clients, sessions,
segments, the NTV item resolver, settings, the account vault) is ported to Kotlin by hand, file by
file, keeping the same names and behaviour so the two stay easy to compare.

## Hard rules (unchanged from desktop)

| Rule | How the native app keeps it |
|---|---|
| **Never transcode** | One URL builder for video: `/Videos/{id}/stream?static=true&mediaSourceId=…&playSessionId=…&deviceId=…`, token in the `Authorization` header only. `PlaybackInfo` is posted with direct play on and direct stream, transcoding and stream copy off, no transcoding profiles. Kotlin `NeverTranscodeTests` (below) fail the build otherwise. No HLS/DASH module is even on the classpath (`media3-exoplayer-hls` / `-dash` are banned by the dependency check). |
| **NostalgiaTV: live only** | Same as desktop: channel list + XMLTV guide, never the On Demand section. |
| **Zero phoning home** | Every request goes through one OkHttp client with a **host allowlist interceptor**: only the signed-in Jellyfin server, the configured Seerr and NostalgiaTV servers, LAN discovery, and image.tmdb.org only when the user opts in. Coil (images) and Media3 (video) share that client, so nothing can bypass it. No Play Services, Firebase, analytics or crash reporting. A Gradle check fails the build if a dependency from a banned group appears (see "Dependencies"). |
| **Secrets** | Tokens encrypted with an AES-GCM key in the **Android Keystore**, stored in app-private files. `allowBackup=false`. Passwords never stored. |
| **Music hidden** | `music` and `musicvideos` libraries, `MusicAlbum`, `Audio`, `MusicArtist` and `MusicVideo` are filtered out of the rail, Home, search and libraries. |
| **Licences** | Only Apache-2.0 / MIT / OFL dependencies. **No Wholphin code** is copied or translated (GPL-2.0-only); ideas only. `THIRD_PARTY_NOTICES.md` gets an Android section. |

## Repository layout

```
android/                       Gradle project (Kotlin DSL, version catalog)
  settings.gradle.kts
  gradle/libs.versions.toml    every dependency pinned in one place
  core/                        pure Kotlin/JVM library, no Android imports: the Nettle.Core port
    src/main/kotlin/io/github/devall96/nettle/core/
      api/        JellyfinClient, JellyfinSocket, SeerrClient, NtvClient, HttpFactory
      models/     Jellyfin, Seerr, Ntv, AppSettings, VideoFormat (kotlinx.serialization)
      services/   SessionService, AccountStore, SettingsStore, SegmentService, NtvItemResolver, Log
    src/test/kotlin/…          JUnit tests, run on the PC in seconds (incl. NeverTranscodeTests)
  app/                         the Android TV app
    src/main/kotlin/io/github/devall96/nettle/tv/
      NettleApp.kt, MainActivity.kt, AppServices.kt
      theme/      Palettes (16 themes), Typography, NettleTheme, Icons (MDI paths), BrandMark
      ui/         shared components: NettleButton (primary/ghost/glass/icon/chip/nav), MediaCard,
                  CardRow, Panel, Toggle, TvTextField, Dropdown, Spinner, Toast
      screens/    one package per screen (see checklist); a ViewModel per screen
      player/     NettlePlayer (Media3), PlayerScreen (OSD), badges, trickplay, refresh-rate switching
      platform/   KeystoreSecureStore, AndroidPlatformInfo, Discovery (UDP + MulticastLock)
    src/main/res/ font/ (Inter), drawable/banner, mipmap/, xml/network_security_config.xml
  baselineprofile/             startup + scroll baseline profile (androidx.profileinstaller)
```

Package / applicationId **`io.github.devall96.nettle.tv`** (the Avalonia port is
`io.github.devall96.nettle`, so both install side by side). `minSdk 28`, `targetSdk 35`.
ABIs: Kotlin is ABI-neutral; the only native code would be the optional FFmpeg audio decoder
(question 2), built for **armeabi-v7a** (the onn) and arm64-v8a.

Repo plumbing: add `!/android/` to the `.gitignore` allowlist and `android/.*` to the `allowed`
regex in `.githooks/pre-commit`; add `android/**/build/`, `.gradle/` and `local.properties` to the
ignore list. The C# `NeverTranscodeTests.Source_NeverRequestsTranscodedStreams` scan is widened to
also read `android/**/*.kt`, so either test suite catches a regression.

## Looking exactly like Windows

### One coordinate system for both apps

Desktop layouts use Avalonia units (margins 56, hero 560 tall, rail 80/250, card 12 radius…).
The TV's own logical size is 960×540 dp, smaller than any desktop window, which is what made the
Avalonia port cramped. The native app instead draws in **"Nettle units"** on a fixed canvas: it
overrides Compose's `LocalDensity` so the screen is **1440 units wide** (desktop's default window
width, the size the layouts were tuned at), 810 tall. Then every number from the `.axaml` files
is used **verbatim as `dp`**, and the TV shows the same proportions as the Windows window,
sharp at 1080p and 4K UI. (1 unit = 1.33 px on the onn.) An **Interface size** setting can offer
1280 / 1440 / 1600 / 1920 if wanted (question 1).

### Tokens ported 1:1

- **Themes:** all 16 `ThemePalette`s copied exactly (`Background, BackgroundAlt, Surface,
  SurfaceHigh, Accent, Accent2, OnAccent, Text, TextMuted, Border`) plus the derived brushes:
  `SurfaceTranslucent` (Surface @ 0xE6), `AccentSoft` (Accent @ 0x33), `Scrim` (#B0000000),
  `AccentGradient` (Accent→Accent2, diagonal), `HeroFadeBottom` / `HeroFadeLeft` (same stops) and
  `AmbientGlow` (radial at 10%,0, Accent @ 0x22). Switching is instant (one `CompositionLocal`).
- **Type:** Inter (the same font, OFL-1.1, taken from the Avalonia.Fonts.Inter package already used
  on desktop). `display` 52/ExtraBold/-1, `h1` 36/Bold/-0.5, `h2` 26/Bold, `h3` 19/SemiBold, body
  14, `caption` 12.5, `section` 13/Bold/1.2 tracking/muted.
- **Controls** from `Controls.axaml`: button radius 12, padding 18×10, 2-unit border that turns
  Accent on focus; `primary` (gradient, 26×12, 15 pt, OnAccent text, Text-coloured focus border);
  `ghost`, `glass` (#33FFFFFF), `icon` (round, 22-unit icon), `chip` (pill, Accent when selected),
  `nav`; media card (12 radius, 2.5-unit frame Accent on focus, play hint fade, watched tick,
  count badge, thin progress bar); text box (12 radius, Surface, Accent border on focus); panel
  (18 radius, 24 padding, 1-unit border); thin progress (4 tall, #40FFFFFF track); spinner arc
  (260° sweep, 0.9 s); page entrance (fade + 14-unit slide, 0.32 s); image fade-in 0.15 s on TV
  (0.35 s on desktop; shortened to halve redraw work on low-power boxes).
  **Reduce motion** turns all of that off, as on desktop (off by default; an on-by-default TV build was tried
  on 2026-11-07 and reverted for now; it may come back for low-power boxes like the onn 4K, Mali-G31).
- **Icons:** the same Material Design Icons path data (Apache-2.0) as `Icons.axaml`, parsed into
  `ImageVector`s. **BrandMark / app icon:** Nettle's own drawing code (`BrandMark.cs`,
  `AppIcon.cs`) redrawn on a Compose `Canvas`, recoloured per theme. Launcher banner 320×180
  drawn from the same art.
- **TV-only differences, kept minimal** (the same ones the Avalonia port settled on, from
  `Tv.axaml`): no mouse hover, so focus carries the feedback; cards don't zoom on focus (rows
  clip the zoom), they get a 3-unit Accent frame instead; no scroll bars; tooltips' text appears
  where needed (e.g. the HDR warning is already shown as text on Details). Every focus style is
  checked in all 16 themes, light ones included.

### Navigation model

A small back stack like `ShellViewModel` (no Navigation-Compose): `NavigateRoot` for rail items,
`Navigate` for pushes, `GoBack`. **Back** closes popups first, then steps back, and from Home leaves
for the launcher (`moveTaskToBack`). Focus is remembered per page and per row when coming back.
Text fields: D-pad focus only highlights; **OK opens the on-screen keyboard**; Back/Done closes it.

## Screen-by-screen parity checklist

Each screen is built, then screenshotted on the onn next to the Windows app at the same theme,
and only ticked off when they match.

| # | Screen | Windows source | Must match |
|---|---|---|---|
| 1 | Splash | `MainView.axaml` | BrandMark ×1.6, spinner, status text, ambient glow; shows only until the cached Home is ready |
| 2 | Connect (server) | `ServerSelectView` | "Profiles" back link, h2 "Connect to your server", http/https chips, address box (IP only, port added: 8096/8920), Connect, "On your network" list with rescan (UDP 7359 discovery with `MulticastLock`) |
| 3 | Sign in | `LoginView` | public user tiles, "Sign in with a different user", username/password, Remember me, plain-http-over-internet warning, Quick Connect code + "Waiting for approval…", password-never-stored note; after sign-in, **NostalgiaTV (19850) and Seerr (5055) auto-setup** on the same host (`IntegrationAutoSetup`) |
| 4 | Who's watching? | `AccountPickerView` | profile tiles with avatars, Add account, Manage/Forget, "Sign in automatically" toggle |
| 5 | Shell + rail | `ShellView` | rail 80 → 250 over the page on focus (shadow 12 0 40 #66000000), BrandMark + "Nettle" + version, Home/Search, libraries (music hidden), Live TV/Requests/Settings, avatar menu (Switch profile, Sign out & forget), cross-fade 0.18 s |
| 6 | Home | `HomeView` | 560-tall spotlight carousel (backdrop, left + bottom fades, logo or display title, meta, 3-line overview, Play + More info, prev/next), Continue Watching, Next Up, Recently Added per library (respecting the user's hidden libraries), instant from cache (`HomeCache`) then refreshed |
| 7 | Library | `LibraryView` | h1 + count, Play all / Shuffle, filters (genre, decade, 4K/HD/SD, unwatched/in progress/favorites) and sort as TV dropdowns, virtualized grid, A–Z strip when sorted by name; also used for collections, folders and **person** pages |
| 8 | Details | `DetailsView` | fixed 720 backdrop with fades, 240×360 poster (wide still for episodes), logo/title, subtitle, meta, HDR badge + media info + HDR warning, Play/Resume, From start, Shuffle (series), Trailer, watched/favourite, Go to series, version picker, progress + "Ends at", tagline, overview, genres, season chips, episode list (260×146 thumbs), Cast & Crew circles, Extras, More Like This |
| 9 | Search | `SearchView` | h1, 760-wide search box, results rows by kind, "Nothing in your library matches that" + Seerr button, "Not in your library: request it" row |
| 10 | Live TV | `LiveTvView` | not-configured state, 200-tall info panel (image, channel, h1 title, time + progress, description), time header with earlier/now/later, 200-wide channel column + 68-tall program cells (now/gap styles, Accent fill on focus), now line, virtualized both ways, opens instantly from the cached guide |
| 11 | Requests | `RequestsView` | not-configured state, search TMDB box, My Requests cards, Trending / Popular / Coming Soon rows of 164×246 cards with status pill and Request hint, season picker panel (Select all, checkboxes, Cancel / Request) |
| 12 | Settings | `SettingsView` | Appearance (16 theme swatches, Reduce motion), Playback (all toggles and fields: passthrough, auto-play next, still watching + count, per-segment actions, languages, subtitle mode, buffer, seek step, + Android: match frame rate), Accounts, Seerr, NostalgiaTV, Server plugins, About, Clear image cache. Desktop-only sections (Desktop menu entry, hardware decoding, Velopack updates) are left out; Updates: see question 3 |
| 13 | Player | `PlayerView` | see next section: OSD top/bottom gradients, title, seek bar with chapter marks and trickplay preview, time, play/pause and transport, LIVE pill + channel up/down, tracks panel (Audio, Subtitles, Find subtitles online, Playback fine-tuning), flash messages, channel banner, "between programs", Skip button, Up Next card with countdown, format badges, "Are you still watching?", buffering, error panel |
| 14 | Toasts / overlays | `MainView` | pill toast bottom centre |

Also carried over: live updates from the server WebSocket (watched state, new items refresh the
visible page after a burst settles, not while playing), "Play on Nettle" remote control, session
rejected → back to sign-in.

## Kotlin port of Nettle.Core

| C# | Kotlin | Notes |
|---|---|---|
| `Api/JellyfinClient.cs` (597 lines) | `api/JellyfinClient.kt` | Same endpoints, fields constants and auth header (`MediaBrowser Client="Nettle", Device="Nettle (Android TV)", DeviceId, Version, Token`). Device name stays generic, never the box's name. `DirectStreamUrl` and `ExternalSubtitleUrl` carry no token. `ProbeServerAsync` (http/https + default port), `DiscoverAsync` (UDP 7359), Quick Connect, trickplay, segments, Intro Skipper, remote subtitles, reporting |
| `Api/JellyfinSocket.cs` | `api/JellyfinSocket.kt` | OkHttp WebSocket, token in the header, reconnect with backoff, KeepAlive |
| `Api/SeerrClient.cs` | `api/SeerrClient.kt` | API key or Jellyfin sign-in (cookie jar, only the session cookie kept, encrypted); image proxy URLs unless TMDB direct is opted in; "my requests" filtered to the signed-in user |
| `Api/NtvClient.cs` | `api/NtvClient.kt` | channel list + XMLTV guide (streaming XML parse), on-disk guide cache, per-channel schedule; live channels only |
| `Models/*` | `models/*` | kotlinx.serialization, `ignoreUnknownKeys`, PascalCase names as on the server; `PlaybackInfoDto` defaults identical to C# |
| `Models/VideoFormat.cs` | `models/VideoFormat.kt` | HDR label + warning, DV profile 5 detection, audio labels, channel layout, resolution |
| `Services/SessionService.cs` | `services/SessionService.kt` | resume, validate, complete login, switch user, sign out & forget, preferred languages from the Jellyfin profile |
| `Services/AccountStore.cs` + `ISecureStore` | `services/AccountStore.kt` + `KeystoreSecureStore` | vault JSON encrypted with a Keystore AES-GCM key |
| `Services/SettingsStore.cs` | `services/SettingsStore.kt` | same `AppSettings` shape as desktop (JSON file), damaged-file backup |
| `Services/SegmentService.cs` | `services/SegmentService.kt` | media segments API first, Intro Skipper fallback, per-kind action |
| `Services/NtvItemResolver.cs` | `services/NtvItemResolver.kt` | fixes stale item ids by series/season/episode lookup |
| `Services/Log.cs` | `services/Log.kt` | app-private log file with the same token masking; nothing leaves the device |
| UI services: `HomeCache`, `ImageCache`, `IntegrationAutoSetup`, `RemoteControl`, `AppServices.IsAllowedHost` | `app/…` | Coil disk cache sized for 2 GB RAM boxes, decode at display size; allowlist used for **all** traffic |

The view models are ported screen by screen into Compose `ViewModel`s with `StateFlow`, following
the C# ones (`HomeViewModel`, `LibraryViewModel`, … `PlayerViewModel`).

## Player (Media3 1.11.x)

- **Engine:** `ExoPlayer` with an `OkHttpDataSource` from the shared, allowlisted client; the
  `Authorization` header is set on the data source, never in the URL. Progressive media only
  (MKV, MP4, TS…); no HLS/DASH modules. Read-ahead sized to each file's bitrate × the "Network
  buffer" setting (as desktop), with a cap for 2 GB RAM.
- **Picture:** `SurfaceView` (not `TextureView`) via `AndroidView`, Compose OSD layered above, so
  HDR10 / HLG / HDR10+ / Dolby Vision frames go straight to the display pipeline. On TVs without DV
  (the user's TCL) Media3 picks the HDR10 base layer of profile 7/8; profile 5 is warned about on
  Details, as desktop does.
- **Frame rate / resolution matching** (own implementation): `Surface.setFrameRate()` with
  `CHANGE_FRAME_RATE_ALWAYS` for seamless refresh switching, and optionally
  `preferredDisplayModeId` to also switch resolution (e.g. 1080p24 for a 1080p film). Setting:
  Off / Refresh rate / Refresh rate + resolution. Restores the original mode on exit.
- **Audio:** passthrough **on**: `DefaultAudioSink` with the device's real capabilities, so AC-3,
  E-AC-3 (+Atmos), DTS, DTS-HD, TrueHD go to the TV/receiver as bitstream. **Off**: audio sink
  limited to PCM so everything is decoded on the box. (A format the box can't decode needs the
  optional FFmpeg audio decoder, question 2.)
- **Verified badges** (`4K · DOLBY VISION · DOLBY ATMOS`, press Info to show again): resolution
  from the decoder's output size; HDR only when the chosen decoder's output colour info says so
  **and** the display reports it can take that HDR type (`Display.HdrCapabilities` / mode); audio
  only once `AnalyticsListener.onAudioTrackInitialized` shows the real output encoding
  (bitstream vs. decoded PCM), so "DOLBY ATMOS" is only claimed when it really goes out as E-AC-3
  JOC.
- **Tracks:** Jellyfin audio/subtitle choice mapped to Media3 track groups (container track id ↔
  ffprobe stream index, as in the Avalonia port's `Media3Player`); external subtitles added as
  side-loaded `SubtitleConfiguration`s; subtitle modes default / always / only forced / smart /
  off and preferred languages from the Jellyfin profile; Find subtitles online through the server.
  Subtitles drawn by Media3's `SubtitleView` styled like desktop (size setting applies). ASS/SSA
  styling is more basic than mpv's: known limitation.
- **Fine-tuning:** speed (remembered), subtitle delay, audio delay (via a delay audio processor),
  subtitle size (remembered).
- **Seeking:** D-pad left/right seek by the seek step; **hold to accelerate** (10 s → 30 s → 1 min
  → 5 min steps the longer it is held), with the trickplay thumbnail and chapter name shown while
  seeking and the jump committed on release. Chapters from the server's item chapters (marks on
  the bar, Channel up/down or Page keys jump between them).
- **Trickplay:** Jellyfin tile sheets loaded through the same image client, cropped per thumbnail,
  only the sheets near the seek position kept in memory.
- **Segments, Up Next, still watching:** ported from `PlayerViewModel` (skip button / auto-skip /
  nothing per kind; Up Next card with countdown during credits; "Are you still watching?" after N
  auto-played episodes or 4 h of Live TV).
- **Queues:** Play all, Shuffle, Shuffle series, next/previous in queue, remote "play next / last".
- **Live TV:** NTV direct-play mode plays the scheduled item's original file at the live offset and
  rolls into the next slot; channels without a Jellyfin item use NostalgiaTV's own MPEG-TS feed.
  Channel up/down keys, channel banner, "between programs", feed watchdog.
- **Reporting:** start / progress (every 10 s and on events) / stopped, also when the activity is
  stopped (Home button) so resume points are saved.
- **Remote control:** WebSocket `Play`, `Playstate`, `GeneralCommand` (volume, tracks, display
  message) like `RemoteControl.cs`; capabilities posted with `SupportsMediaControl`.
- Keep screen on while playing; audio focus; media keys (play/pause, FF/RW, next/previous).

## Speed targets

- Cold start to a usable Home **under 1.5 s** on the onn (from cache), with a baseline profile and
  R8; Home rows appear from the cached snapshot while fresh data loads.
- Library grid and guide scroll at 60 fps: `LazyVerticalGrid` / custom lazy guide layout, stable
  keys, images decoded at their on-screen size, no recomposition of rows on focus moves.
- Measured each build with `adb shell am start -W` and `dumpsys gfxinfo`.

## Dependencies (all checked for network behaviour)

| Library | Licence | Why | Phones home? |
|---|---|---|---|
| Kotlin stdlib, kotlinx.coroutines, kotlinx.serialization | Apache-2.0 | language, async, JSON | no |
| AndroidX Compose (foundation, ui, animation; no `tv-material` look) , activity, lifecycle | Apache-2.0 | UI | no |
| AndroidX Media3 1.11.x: `exoplayer`, `datasource-okhttp`, `ui` (SubtitleView only) | Apache-2.0 | player | no (no analytics; IMA/Cast modules not included) |
| OkHttp + Okio | Apache-2.0 | HTTP + WebSocket | no |
| Coil 3 (`coil-compose`, `coil-network-okhttp`) | Apache-2.0 | images | no |
| AndroidX profileinstaller | Apache-2.0 | baseline profile | no (local only) |
| Inter font | OFL-1.1 | UI font | n/a |
| Material Design Icons path data | Apache-2.0 | icons | n/a |
| Optional: Media3 FFmpeg audio decoder (FFmpeg built LGPL, audio decoders only) | LGPL-2.1+ (FFmpeg), Apache-2.0 (module) | DTS / TrueHD with passthrough off | no |

Build-time guard: a Gradle task fails if any resolved runtime dependency is outside an allowlist
of groups (`androidx.*`, `org.jetbrains.*`, `com.squareup.okhttp3`, `com.squareup.okio`,
`io.coil-kt.coil3`, `com.google.guava` (Media3 needs it), …) and always fails on
`com.google.android.gms`, `com.google.firebase`, `com.google.android.datatransport`, crash
reporters and analytics SDKs, and `media3-exoplayer-hls`/`-dash`/`-ima`/`cast`. Runtime guard:
the allowlist interceptor logs and blocks anything else.

## Tests

- `core` JVM tests (run on the PC, no device):
  - **NeverTranscodeTests**, equivalent of the C# file: `PlaybackInfoDto` flags; serialized JSON has
    `"EnableTranscoding":false`, `"EnableDirectStream":false`, `"AllowVideoStreamCopy":false`,
    `"AllowAudioStreamCopy":false`, `"TranscodingProfiles":[]`; the stream URL starts with
    `/Videos/{id}/stream?`, has `static=true`, no `m3u8`, no `transcod`, **no token / `api_key`**;
    and a source scan of `android/**/*.kt` for HLS playlists, `TranscodingUrl`, transcoding or
    remuxing turned on, `/Audio/…/universal`, `static=false`.
  - Ports of the other Core tests in `tests/Nettle.Tests` (URL normalisation, XMLTV parsing,
    segments, VideoFormat labels, settings round-trip).
  - Host allowlist test.
- On the onn: scripted adb runs per your test rules (force-stop other apps except Tailscale and
  Projectivy, kill + uninstall the previous build, install, screenshot every step), against the
  Jellyfin demo server for UI, and your server for playback / HDR / passthrough / Live TV / Seerr.

## Build order (screen by screen, each checked against Windows before the next)

1. Gradle project, repo plumbing, `core` port + NeverTranscodeTests green.
2. Theme system, fonts, icons, BrandMark, shared components; a hidden "component gallery" screen
   screenshotted in all 16 themes.
3. Splash → Connect → Sign in → Who's watching (with Keystore vault, auto sign-in, Quick Connect,
   auto-setup of Seerr / NTV).
4. Shell + rail, Home (with cache).
5. Details, Library (+ person / collections), Search.
6. Player: playback, OSD, tracks, badges, segments, Up Next, still watching, trickplay, chapters,
   hold-to-seek, frame-rate switching, reporting.
7. Live TV guide + live playback.
8. Requests + season picker, Settings.
9. WebSocket live updates + remote control.
10. Performance pass (baseline profile, start-up and scroll measurements), notices, README section.

## Questions for you (answered)

Answers: 1 → 1440 by default, with the Interface size setting. 2 → no FFmpeg decoder yet (see the
README's known limitations). 3 → updater added in 0.4.3. 4 → music and music videos both hidden.

1. **Size on the TV.** Draw on a 1440-unit-wide canvas (same proportions as the Windows app at its
   default window size; body text ≈ 19 px at 1080p)? Or 1920 (exactly like a full-screen 1080p
   Windows app, smaller text), or 1280 (bigger)? I'd default to 1440 and offer the others as an
   "Interface size" setting.
2. **Audio with passthrough off.** The onn can likely decode Dolby Digital / DD+ itself but not DTS
   or TrueHD. Include Media3's FFmpeg audio decoder (LGPL FFmpeg, audio only, built for 32-bit ARM)
   so those still play when passthrough is off? Without it, such files only play with passthrough
   on (or another audio track is picked automatically).
3. **Updates.** Leave updates out (install new APKs by hand), or add the same opt-in "check GitHub
   Releases" as desktop, which on Android also needs the "install unknown apps" permission?
4. **Music videos.** Hide only music (audio) everywhere, and keep Music Video libraries? (That's
   what I'd do.)

## Remote buttons in the player (TV defaults)

Videos (Jellyfin library):
- **Controls hidden:** Left / Right scrub (since 0.5.0: the video pauses and a preview moves along
  the seek bar; a tap moves one seek step, holding glides faster the longer you hold; OK jumps
  there, Back cancels); Up, Down or OK show the controls.
- **Controls showing:** the D-pad moves around the controls.
- **Play/Pause, Play, Pause** as labelled; **Fast-forward / Rewind** jump 3 × the seek step; **Next / Previous** go to the next / previous item.
- **Channel up / down:** next / previous chapter.
- **0–9:** jump to 0–90 % of the video.
- **Info:** format badges; **Captions:** the audio and subtitle panel; **Audio track:** next audio track.
- **Back:** closes a panel, then the player.

Live TV (NostalgiaTV):
- **Controls hidden (full screen):** Up / Down surf channels; only the channel banner shows, so you can keep pressing. Left / Right or OK show the controls.
- **Controls showing:** the D-pad works the controls (Up / Down no longer surf).
- **Channel up / down, Next / Previous:** change channel at any time.
- **0–9:** type a channel number; it tunes 1.5 s after the last digit, or on OK.
- **Last channel:** back to the previous channel.
- **Guide / TV:** leave the player and open the Live TV guide.
