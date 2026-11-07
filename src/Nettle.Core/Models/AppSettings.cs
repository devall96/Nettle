namespace Nettle.Core.Models;

public enum SegmentAction
{
    Off = 0,
    ShowButton = 1,
    AutoSkip = 2,
}

public sealed class PlaybackSettings
{
    public bool HardwareDecoding { get; set; } = true;
    public SegmentAction IntroAction { get; set; } = SegmentAction.ShowButton;
    public SegmentAction RecapAction { get; set; } = SegmentAction.ShowButton;
    public SegmentAction CreditsAction { get; set; } = SegmentAction.ShowButton;
    public SegmentAction PreviewAction { get; set; } = SegmentAction.ShowButton;
    public SegmentAction CommercialAction { get; set; } = SegmentAction.AutoSkip;
    public bool AutoPlayNextEpisode { get; set; } = true;
    public int UpNextCountdownSeconds { get; set; } = 10;
    public string PreferredAudioLanguage { get; set; } = "";
    public string PreferredSubtitleLanguage { get; set; } = "";
    /// <summary>"" = follow the user's Jellyfin profile; else Default, Always, OnlyForced, Smart or None.</summary>
    public string SubtitleMode { get; set; } = "";
    /// <summary>Playback speed kept between files (1 = normal).</summary>
    public double PlaybackSpeed { get; set; } = 1;
    /// <summary>Subtitle size, as mpv's sub-scale (1 = normal).</summary>
    public double SubtitleScale { get; set; } = 1;
    /// <summary>How many seconds of video mpv tries to keep downloaded ahead (its cache-secs).</summary>
    public int NetworkBufferSeconds { get; set; } = 10;
    public int SeekStepSeconds { get; set; } = 10;
    public int Volume { get; set; } = 100;
    /// <summary>Send Dolby / DTS audio untouched to an AV receiver (S/PDIF or HDMI passthrough).</summary>
    public bool AudioPassthrough { get; set; }
    /// <summary>Ask "Are you still watching?" after several episodes (or hours of live TV) with no input.</summary>
    public bool StillWatchingPrompt { get; set; } = true;
    /// <summary>Episodes auto-played in a row, with no input, before the prompt appears.</summary>
    public int StillWatchingEpisodes { get; set; } = 3;
    /// <summary>
    /// NostalgiaTV: play each scheduled program's original file from Jellyfin at the broadcast
    /// offset (full quality, nothing re-encoded) instead of NostalgiaTV's ffmpeg broadcast feed.
    /// </summary>
    public bool NtvDirectPlay { get; set; } = true;
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "midnight";
    public bool AutoLogin { get; set; } = true;
    public string? LastAccountId { get; set; }
    public PlaybackSettings Playback { get; set; } = new();

    public string? SeerrUrl { get; set; }
    public string? NtvUrl { get; set; }

    /// <summary>"http" or "https": picked on the Connect screen, where only the IP / name is typed.</summary>
    public string ServerScheme { get; set; } = "http";

    public bool ReduceMotion { get; set; }

    /// <summary>Where updates come from (GitHub repo URL, web folder or local folder). Empty = updates off.</summary>
    public string UpdateSource { get; set; } = "";
    /// <summary>Privacy: off by default. When on, checks the update source once per launch.</summary>
    public bool AutoCheckUpdates { get; set; }

    /// <summary>Privacy: allow Requests posters to load directly from image.tmdb.org (third party).</summary>
    public bool AllowDirectTmdbImages { get; set; }

    /// <summary>Linux: the one-time "add to applications menu?" question has been answered.</summary>
    public bool MenuPromptAnswered { get; set; }

    /// <summary>Main window size and position from the last session (null = first run).</summary>
    public WindowPlacement? Window { get; set; }
}

public sealed class WindowPlacement
{
    public double Width { get; set; }
    public double Height { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>A remembered sign-in. Stored only inside the encrypted secure store.</summary>
public sealed class SavedAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ServerUrl { get; set; } = "";
    public string? ServerId { get; set; }
    public string? ServerName { get; set; }
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? AvatarTag { get; set; }
    public string AccessToken { get; set; } = "";
    /// <summary>Each account gets its own device id: Jellyfin revokes older tokens issued to the same device id.</summary>
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsAdmin { get; set; }
    public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>The user's Jellyfin playback preferences as last seen, so they apply before the server answers at startup.</summary>
    public UserConfiguration? UserConfig { get; set; }
}

public sealed class AccountVault
{
    public List<SavedAccount> Accounts { get; set; } = new();
}
