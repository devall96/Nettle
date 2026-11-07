using Nettle.UI.Playback;

namespace Nettle.Desktop;

/// <summary>
/// `Nettle --selftest [--window] [--hw] [--snap DIR] [--mpvopt=name=value ...] file...`
/// Plays each file for a few seconds and reports what mpv managed. By default there's no window
/// or sound device, so CI can prove the bundled libmpv decodes on a clean machine. --window uses
/// real video/audio outputs (mpv's own window), --hw allows hardware decoding, --snap saves a
/// decoded frame of each file. Returns the process exit code.
/// </summary>
internal static class SelfTest
{
    public static int Run(IEnumerable<string> args)
    {
        var list = args.ToList();
        bool window = list.Remove("--window");
        bool hw = list.Remove("--hw");
        string? snapDir = null;
        int snapAt = list.IndexOf("--snap");
        if (snapAt >= 0 && snapAt + 1 < list.Count) { snapDir = Path.GetFullPath(list[snapAt + 1]); list.RemoveRange(snapAt, 2); Directory.CreateDirectory(snapDir); }
        var extra = list.Where(a => a.StartsWith("--mpvopt=")).Select(a => a["--mpvopt=".Length..].Split('=', 2)).Select(p => new KeyValuePair<string, string>(p[0], p.Length > 1 ? p[1] : "yes")).ToList();
        list.RemoveAll(a => a.StartsWith("--mpvopt="));

        var options = PlaybackEngine.BaseOptions(hw).Concat(PlaybackEngine.PlatformOptions)
            .Where(o => !window || o.Key is not ("gpu-context" or "vo")) // own window: mpv's own output and context
            .ToList();
        if (!window) { options.Add(new("vo", "null")); options.Add(new("ao", "null")); }
        options.AddRange(extra);

        int failures = 0;
        foreach (var file in list)
        {
            using var done = new ManualResetEventSlim();
            string? error = null;
            var player = new MpvPlayer(options);
            player.Error += e => { error = e; done.Set(); };
            player.EndReached += () => done.Set();
            player.LogMessage += (_, line) => Console.WriteLine("  " + line);
            player.Initialize(null);
            if (file == list[0]) Console.WriteLine($"{player.GetProperty("mpv-version")} / {player.GetProperty("ffmpeg-version")}");
            player.Load(new MpvLoad { Url = file.Contains("://") ? file : Path.GetFullPath(file) });

            // Wait for ~1.5 s of playback (or failure).
            var until = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < until && !done.IsSet && player.TimeMs < 1500) Thread.Sleep(100);

            var (w, h) = player.VideoSize;
            var vdec = player.GetProperty("current-tracks/video/decoder-desc") ?? player.GetProperty("video-codec");
            var hwdec = player.GetProperty("hwdec-current");
            var adec = player.GetProperty("current-tracks/audio/decoder-desc") ?? player.GetProperty("audio-codec");
            var ao = player.GetProperty("current-ao");
            var vo = player.GetProperty("current-vo");
            if (snapDir != null)
            {
                var shot = Path.Combine(snapDir, Path.GetFileNameWithoutExtension(file) + (hw ? "-hw" : "-sw") + ".png");
                Console.WriteLine(player.Screenshot(shot) ? $"  snapshot: {shot}" : "  snapshot failed");
            }
            bool ok = error == null && w > 0 && player.HasActiveAudio && player.TimeMs >= 1000;
            if (!ok) failures++;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {Path.GetFileName(file)}: {w}x{h}, t={player.TimeMs} ms, video {vdec} (hwdec {hwdec ?? "no"}, vo {vo}), audio {adec} (ao {ao})" + (error != null ? $", error: {error}" : ""));
            player.Terminate();
        }
        return failures == 0 ? 0 : 1;
    }
}
