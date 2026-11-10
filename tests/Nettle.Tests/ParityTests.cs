using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Nettle.Core.Models;
using Nettle.Core.Services;

namespace Nettle.Tests;

/// <summary>
/// Windows, Linux and Android TV must offer the same features (Android TV is separate Kotlin code under
/// android/; Windows and Linux share src/Nettle.UI, with Linux-only parts in src/Nettle.Desktop/Linux).
/// These read the sources of all three and check that each feature added with the 0.4.4 work is present,
/// with the same numbers, in every one of them.
/// </summary>
public class ParityTests
{
    internal static string Root => NeverTranscodeTests.RepoRoot();
    internal static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private const string Tv = "android/app/src/main/kotlin/io/github/devall96/nettle/tv";
    internal static string TvFile(string rel) => Read(Tv, rel);
    internal static string KtCore(string rel) => Read("android/core/src/main/kotlin/io/github/devall96/nettle/core", rel);
    internal static string Desk(string rel) => Read("src/Nettle.UI", rel);
    internal static string Linux(string rel) => Read("src/Nettle.Desktop/Linux", rel);

    private static double KotlinConst(string source, string name) =>
        double.Parse(Regex.Match(source, $@"const val {name}\s*=\s*([\d.]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

    // ---------- Scrubbing ----------

    [Fact]
    public void ScrubSpeed_SameNumbersOnTvAndDesktop()
    {
        var kt = KtCore("services/ScrubSpeed.kt");
        Assert.Equal(ScrubSpeed.Start, KotlinConst(kt, "START"));
        Assert.Equal(ScrubSpeed.CrossSeconds, KotlinConst(kt, "CROSS_SECONDS"));
        // Same formula: 30 × 2^(held s), capped at length / 8 s.
        Assert.Contains("START * 2.0.pow(heldMs.coerceAtLeast(0) / 1000.0)", kt);
        Assert.Contains("lengthMs / 1000.0 / CROSS_SECONDS", kt);
        Assert.Contains("sinceLastMs.coerceIn(0, 500)", kt);
    }

    [Fact]
    public void Scrubbing_WorksTheSameWayEverywhere()
    {
        var tv = TvFile("player/PlayerModel.kt");
        var tvKeys = TvFile("player/PlayerScreen.kt");
        var desk = Desk("ViewModels/PlayerViewModel.cs");
        var deskView = Desk("Views/PlayerView.axaml.cs");

        // Entering scrubbing pauses the video and remembers where it was; it doesn't seek yet.
        Assert.Contains("if (scrubWasPlaying) engine.setPause(true)", tv);
        Assert.Contains("if (_scrubWasPlaying) Player.SetPause(true);", desk);
        // A tap moves one seek step; holding glides, driven by time (ScrubSpeed), on a timer.
        Assert.Contains("settings.seekStepSeconds * 1000L", tv);
        Assert.Contains("_settings.SeekStepSeconds * 1000.0", desk);
        Assert.Contains("ScrubSpeed.advanceMs(", tv);
        Assert.Contains("ScrubSpeed.AdvanceMs(", desk);
        // OK / Enter / play commit; Back / Esc cancel; both resume if it was playing.
        Assert.Contains("fun commitScrub()", tv);
        Assert.Contains("fun cancelScrub()", tv);
        Assert.Contains("private void CommitScrub()", desk);
        Assert.Contains("private void CancelScrub()", desk);
        Assert.Matches(@"KEYCODE_BACK, AKey\.KEYCODE_MEDIA_STOP -> \{ if \(up\) vm\.cancelScrub\(\)", tvKeys);
        Assert.Matches(@"KEYCODE_DPAD_CENTER[^\n]*\n[^\n]*KEYCODE_MEDIA_PLAY_PAUSE, AKey\.KEYCODE_MEDIA_PLAY -> \{ if \(up\) vm\.commitScrub\(\)", tvKeys);
        Assert.Matches(@"case Key\.Enter or Key\.Space or Key\.K or Key\.MediaPlayPause:\s*CommitScrub\(\)", desk);
        Assert.Matches(@"case Key\.Escape or Key\.Back or Key\.BrowserBack or Key\.MediaStop:\s*CancelScrub\(\)", desk);
        // The play button commits too.
        Assert.Contains("if (isScrubbing) { commitScrub(); return }", tv);
        Assert.Contains("if (_scrubbing) { CommitScrub(); return; }", desk);
        // "Held" comes from real key events, never from guessing at repeat timing (that made it lurch).
        Assert.Contains("ScrubKeyUp", deskView);
        Assert.Contains("fun scrubKeyUp()", tv);
        // Each logs, so a test run can be checked from the log.
        Assert.Contains("\"seek: ${if (scrubGlided) \"scrubbed\" else \"stepped\"}", tv);
        Assert.Contains("seek: scrubbed", desk);
        Assert.Contains("seek: scrub cancelled", tv);
        Assert.Contains("seek: scrub cancelled", desk);
    }

    // ---------- Chapters ----------

    [Fact]
    public void Chapters_NextPreviousAndLogging_Everywhere()
    {
        var tv = TvFile("player/PlayerModel.kt");
        var desk = Desk("ViewModels/PlayerViewModel.Extras.cs");
        foreach (var name in new[] { "nextChapter", "previousChapter" })
            Assert.Contains($"fun {name}()", tv);
        Assert.Contains("public void NextChapter()", desk);
        Assert.Contains("public void PreviousChapter()", desk);
        // Same "just after a chapter starts, go to the one before" rule (3 s).
        Assert.Contains("pos - cur.ms < 3000", tv);
        Assert.Contains("pos - cur.Seconds * 1000 < 3000", desk);
        Assert.Contains("chapter: next", tv);
        Assert.Contains("chapter: next", desk);
        Assert.Contains("chapter: previous", tv);
        Assert.Contains("chapter: previous", desk);
        // The scrub preview names the chapter on both.
        Assert.Contains("chapterAt(m)?.title", tv);
        Assert.Contains("\"  ·  \" + chapter", Desk("ViewModels/PlayerViewModel.cs"));
    }

    // ---------- Audio timing ----------

    [Fact]
    public void AudioTiming_SavedPerRefreshRate_SameLimitsAndSteps()
    {
        // One setting with the same name in both settings files.
        Assert.Contains("var audioOffsets: Map<String, Int> = emptyMap()", KtCore("models/AppSettings.kt"));
        Assert.Contains("public Dictionary<string, int> AudioOffsets", Read("src/Nettle.Core/Models/AppSettings.cs"));
        // ±500 ms, 10 ms steps.
        Assert.Contains("AUDIO_OFFSET_LIMIT_MS = 500", TvFile("player/Engine.kt"));
        var deskExtras = Desk("ViewModels/PlayerViewModel.Extras.cs");
        Assert.Contains("AudioOffsetLimitMs = 500", deskExtras);
        var tv = TvFile("player/PlayerModel.kt");
        Assert.Contains("applyAudioOffset(audioOffsetMs - 10)", tv);
        Assert.Contains("applyAudioOffset(audioOffsetMs + 10)", tv);
        Assert.Contains("NudgeAudio(-10)", deskExtras);
        Assert.Contains("NudgeAudio(10)", deskExtras);
        // Keyed by the screen's rounded refresh rate, and it follows the screen when it switches.
        Assert.Contains("Math.round(it).toString()", tv);
        Assert.Contains("(int)Math.Round(hz)", deskExtras);
        Assert.Contains("onDisplayChanged", tv);
        Assert.Contains("UpdateRefreshKey()", Desk("ViewModels/PlayerViewModel.cs"));
        // Not part of "Reset to normal" on either: it's the screen's calibration.
        Assert.DoesNotContain("AudioDelayMs = 0", deskExtras);
    }

    [Fact]
    public void AudioOffsets_RoundTripThroughSettingsJson()
    {
        var s = new AppSettings();
        s.Playback.AudioOffsets["24"] = 210;
        s.Playback.AudioOffsets["60"] = 40;
        var json = System.Text.Json.JsonSerializer.Serialize(s, Nettle.Core.Json.LocalJson.Default.AppSettings);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, Nettle.Core.Json.LocalJson.Default.AppSettings)!;
        Assert.Equal(210, back.Playback.AudioOffsets["24"]);
        Assert.Equal(40, back.Playback.AudioOffsets["60"]);
    }

    // ---------- Lip sync check, themes ----------

    [Fact]
    public void LipSyncCheck_InSettings_Everywhere()
    {
        Assert.Contains("\"Lip sync check\"", TvFile("screens/SettingsScreen.kt"));
        Assert.Contains("Text=\"Lip sync check\"", Desk("Views/SettingsView.axaml"));
        Assert.Contains("fun playLipSyncCheck(", TvFile("vm/Main.kt"));
        Assert.Contains("public void PlayLipSyncCheck()", Desk("ViewModels/MainViewModel.cs"));
        // Both play the bundled clip on a loop, at the film rate so a TV / monitor that switches for films does here too.
        Assert.Contains("asset:///lipsync-check.mkv", TvFile("player/PlayerModel.kt"));
        Assert.Contains("avares://Nettle.UI/Assets/lipsync-check.mkv", Desk("ViewModels/PlayerViewModel.cs"));
        Assert.Contains("REPEAT_MODE_ONE", TvFile("player/PlayerModel.kt"));
        Assert.Contains("Loop = true", Desk("ViewModels/PlayerViewModel.cs"));
        Assert.Contains("23.976f", TvFile("player/PlayerModel.kt"));
        Assert.Contains("24000 / 1001.0", Desk("ViewModels/PlayerViewModel.cs"));
        // ← / → move the sound in the check; Linux gets them before any control can (tunnel).
        Assert.Contains("AKey.KEYCODE_DPAD_LEFT -> if (down) vm.audioEarlier()", TvFile("player/PlayerScreen.kt"));
        Assert.Contains("case Key.Left or Key.OemMinus or Key.Subtract: AudioEarlier()", Desk("ViewModels/PlayerViewModel.cs"));
        Assert.Contains("OnCheckKey, RoutingStrategies.Tunnel", Desk("Views/PlayerView.axaml.cs"));
    }

    [Fact]
    public void Themes_OnTheirOwnScreen_Everywhere()
    {
        var tv = TvFile("screens/SettingsScreen.kt");
        Assert.Contains("private fun ThemePicker(", tv);
        Assert.Contains("\"Change theme ›\"", tv);
        Assert.Contains("BackHandler { vm.closeThemes() }", tv);
        var desk = Desk("Views/SettingsView.axaml");
        Assert.Contains("IsVisible=\"{Binding IsThemesOpen}\"", desk);
        Assert.Contains("Content=\"Change theme\"", desk);
        Assert.Contains("Gesture=\"Escape\" Command=\"{Binding CloseThemesCommand}\"", desk);
        // The swatches live only in the picker, not in the main Settings list.
        var mainList = desk[..desk.IndexOf("Settings → Appearance → Change theme", StringComparison.Ordinal)];
        Assert.DoesNotContain("ItemsSource=\"{Binding Themes}\"", mainList);
    }

    // ---------- Things that differ on purpose (checked, so they stay deliberate) ----------

    [Fact]
    public void TvOnly_VolumeIsTheTvs_AndBackTwiceCloses()
    {
        var screen = TvFile("player/PlayerScreen.kt");
        Assert.DoesNotContain("VolumeBar", screen);
        Assert.DoesNotContain("Icons.Volume", screen);
        Assert.Contains("var volume by mutableStateOf(100)", TvFile("player/PlayerModel.kt"));
        var act = TvFile("MainActivity.kt");
        Assert.Contains("Press Back again to close Nettle", act);
        Assert.Contains("finishAndRemoveTask()", act);
        // The desktop's TV profile hides its volume too.
        Assert.Contains("the remote controls the TV's own volume", Desk("ViewModels/PlayerViewModel.cs"));
    }

    [Fact]
    public void TvSettings_EveryControlRowTakesFocusAsAWhole()
    {
        var tv = TvFile("screens/SettingsScreen.kt");
        var settingsBody = tv[..tv.IndexOf("/** Grid.row:", StringComparison.Ordinal)];
        // Dropdowns and steppers used to sit at the right edge where the remote skipped them.
        Assert.DoesNotMatch(@"SettingRow\([^)]*\)\s*\{\s*Dropdown\(", settingsBody);
        Assert.DoesNotMatch(@"SettingRow\([^)]*\)\s*\{\s*Stepper\(", settingsBody);
        foreach (var row in new[] { "\"Interface size\"", "\"Match frame rate\"", "\"Subtitles turn on\"" })
            Assert.Matches($@"DropdownRow\({Regex.Escape(row)}", settingsBody);
        foreach (var row in new[] { "\"Network buffer (seconds)\"", "\"Seek step (seconds)\"" })
            Assert.Matches($@"StepperRow\({Regex.Escape(row)}", settingsBody);
    }

    // ---------- Linux ----------

    [Fact]
    public void Linux_KeepAwake_FallbackIsDetachedFromTheTerminal_AndReleases()
    {
        var ka = Linux("LinuxKeepAwake.cs");
        Assert.Contains("gnome-session-inhibit", ka);
        Assert.Contains("systemd-inhibit", ka);
        // Ends with Nettle even after a crash.
        Assert.Contains("\"tail\", \"--pid=\" + pid", ka);
        // All three streams redirected (a shared terminal froze Nettle), and only one helper at a time.
        Assert.Contains("RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true", ka);
        Assert.Contains("p.StandardInput.Close();", ka);
        Assert.Contains("old.Kill(entireProcessTree: true)", ka);
        Assert.Contains("keep-awake: released", ka);
    }

    [Fact]
    public void Linux_TestProfiles_NeverTouchTheKeyringOrTheMenu()
    {
        Assert.Contains("useKeyring: !throwawayProfile", Read("src/Nettle.Desktop/Program.cs"));
        Assert.Contains("#if DEBUG || NETTLE_DEV", Linux("LinuxPlatform.cs"));
        // "Add to applications menu" only from the AppImage (an unpacked build's path is temporary).
        Assert.Contains("d.OfferOnFirstRun || d.IsInMenu", Desk("ViewModels/SettingsViewModel.cs"));
        Assert.Contains("OfferOnFirstRun => !string.IsNullOrEmpty(AppImagePath)", Linux("LinuxDesktopIntegration.cs"));
    }

    [Fact]
    public void Desktop_SeekBar_DoesNotSnapBackOrTakeKeyboardFocus()
    {
        var vm = Desk("ViewModels/PlayerViewModel.cs");
        Assert.Contains("bool seekLanding", vm);
        Assert.Contains("Focusable=\"{Binding IsTv}\"", Desk("Views/PlayerView.axaml"));
        // The check's clip goes into Nettle's own cache, not /tmp.
        Assert.Contains("AppServices.Platform.CacheDirectory, \"lipsync-check.mkv\"", vm);
    }
}

/// <summary>
/// The user and their dog are epileptic, and other people may be too. The lip sync check is the only thing in
/// Nettle that blinks on purpose: these keep it gentle, warned about, and the same on every platform.
/// </summary>
public class SeizureSafetyTests
{
    /// <summary>The gentle clip: a mid-grey box, a quarter of the width, once a second, on black (tools/avsync --gentle).</summary>
    private const string GentleClipSha256 = "a7bdfce73c8944b5e8ba2a6200322059f85827bbadd5f71b7ef5d01ae91a0065";

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Root => ParityTests.Root;

    [Fact]
    public void BothApps_BundleTheSameGentleClip()
    {
        var tv = Path.Combine(Root, "android/app/src/main/assets/lipsync-check.mkv");
        var desk = Path.Combine(Root, "src/Nettle.UI/Assets/lipsync-check.mkv");
        // If this fails, the clip changed: check the new one frame by frame (no full-screen flashing, at most
        // 3 flashes a second, WCAG 2.3.1) BEFORE updating this checksum, and never by playing it on someone's screen.
        Assert.Equal(GentleClipSha256, Sha256(tv));
        Assert.Equal(GentleClipSha256, Sha256(desk));
    }

    [Fact]
    public void NoFlashingTestClipShipsInAnyApp()
    {
        // The full-screen white sync-test clips live only in tools/avsync/clips (phone measurements); they may only
        // reach an APK through the explicit -Pnettle.syncTest=true build, never as a normal asset.
        foreach (var dir in new[] { "android/app/src/main/assets", "src/Nettle.UI/Assets", "src/Nettle.Desktop/Assets" })
        {
            var full = Path.Combine(Root, dir);
            if (!Directory.Exists(full)) continue;
            var videos = Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mkv") || f.EndsWith(".mp4") || f.EndsWith(".webm")).Select(Path.GetFileName).ToList();
            Assert.True(videos.All(v => v == "lipsync-check.mkv"), $"{dir} ships other video files: {string.Join(", ", videos)}");
        }
        var gradle = File.ReadAllText(Path.Combine(Root, "android/app/build.gradle.kts"));
        Assert.Contains("if (syncTest) sourceSets[\"main\"].assets.srcDir", gradle);
    }

    [Fact]
    public void ClipGenerator_GentleModeIsASmallGreyBox()
    {
        var py = File.ReadAllText(Path.Combine(Root, "tools/avsync/avsync.py"));
        // A quarter of the width and height (1/16 of the screen), mid-grey, never white or full screen.
        Assert.Contains("drawbox=x=iw*3/8:y=ih*3/8:w=iw/4:h=ih/4:color=0x8C8C8C", py);
        // One blink per ~second (24 frames at 23.976), 2 frames long: far below 3 flashes a second.
        Assert.Contains("frames_per_event = round(num / den)", py);
        Assert.Contains("flash_frames = 2", py);
    }

    // TV: the check's rows call PhotosensitivityWarning() (its text is in that composable further down).
    // Desktop: the warning's Border is written right under the check's row.
    [Theory]
    [InlineData("android", "app/src/main/kotlin/io/github/devall96/nettle/tv/screens/SettingsScreen.kt", "PhotosensitivityWarning()")]
    [InlineData("src", "Nettle.UI/Views/SettingsView.axaml", "Epilepsy warning")]
    public void EpilepsyWarning_NextToTheLipSyncCheck(string top, string rel, string shownBy)
    {
        var text = File.ReadAllText(Path.Combine(Root, top, rel));
        var check = text.IndexOf("Lip sync check", StringComparison.Ordinal);
        var shown = text.IndexOf(shownBy, StringComparison.Ordinal);
        var warning = text.IndexOf("Epilepsy warning", StringComparison.Ordinal);
        Assert.True(check >= 0 && shown >= 0 && warning >= 0, "the lip sync check or its warning is missing");
        // The warning is shown right with the check (Settings → Playback), not somewhere else on the page.
        Assert.InRange(shown - check, 0, 2500);
        var line = text[warning..text.IndexOf('\n', warning + 200)];
        Assert.Contains("blinks", line);
        Assert.Contains("once a second", line);
        Assert.Contains("epilepsy", line);
        Assert.Contains("pets included", line);
        Assert.Contains("don't run it", line);
    }

    [Fact]
    public void EpilepsyWarning_IsTheSameTextInAllApps()
    {
        static string Warning(string text)
        {
            var m = Regex.Match(text, @"⚠ Epilepsy warning:[^""]*?(?:""\s*\+\s*""[^""]*?)*don't run it\.");
            Assert.True(m.Success, "no epilepsy warning");
            return Regex.Replace(Regex.Replace(m.Value, @"""\s*\+\s*""", ""), @"\s+", " ");
        }
        var tv = Warning(ParityTests.TvFile("screens/SettingsScreen.kt"));
        var desk = Warning(ParityTests.Desk("Views/SettingsView.axaml"));
        Assert.Equal(tv, desk);
    }

    [Fact]
    public void OnScreenText_DescribesABlinkingBox_NotAFlash()
    {
        foreach (var text in new[] { ParityTests.TvFile("player/PlayerScreen.kt"), ParityTests.Desk("Views/PlayerView.axaml"),
                     ParityTests.TvFile("screens/SettingsScreen.kt"), ParityTests.Desk("ViewModels/SettingsViewModel.cs") })
        {
            Assert.DoesNotContain("flashes white", text);
            Assert.DoesNotContain("Plays a flash", text);
        }
        Assert.Contains("A grey box blinks in the middle of the screen once a second", ParityTests.TvFile("player/PlayerScreen.kt"));
        Assert.Contains("A grey box blinks in the middle of the screen once a second", ParityTests.Desk("Views/PlayerView.axaml"));
    }
}
