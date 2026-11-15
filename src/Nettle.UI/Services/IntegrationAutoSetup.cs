using Nettle.Core.Services;

namespace Nettle.UI.Services;

/// <summary>
/// After a Jellyfin sign-in, looks for Seerr (port 5055) and NostalgiaTV (port 19850) on the same
/// machine as the Jellyfin server, with the same http / https, and sets up whichever answers.
/// Seerr is signed in with the Jellyfin username and password just entered (Seerr's own "Sign in
/// with Jellyfin"); the password is only passed on, never stored. Anything already set up in
/// Settings is left alone. Only the Jellyfin server's own host is ever contacted.
/// </summary>
public static class IntegrationAutoSetup
{
    public const int SeerrPort = 5055;
    public const int NtvPort = 19850;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

    public static async Task RunAsync(string jellyfinUrl, string? username, string? password)
    {
        try
        {
            if (!Uri.TryCreate(jellyfinUrl, UriKind.Absolute, out var jf)) return;
            var s = AppServices.Settings.Current;
            var host = jf.HostNameType == UriHostNameType.IPv6 ? "[" + jf.IdnHost + "]" : jf.IdnHost;
            // Same scheme as Jellyfin first; https setups often still run these add-ons on plain http.
            string[] schemes = jf.Scheme == "https" ? ["https", "http"] : ["http"];
            var done = new List<string>();
            bool changed = false;

            // True once NostalgiaTV is set up (now, or already). Asked again further down when it doesn't answer.
            async Task<bool> TryNtvAsync(TimeSpan timeout)
            {
                if (AppServices.Ntv.IsConfigured || !string.IsNullOrWhiteSpace(s.NtvUrl)) return true;
                foreach (var scheme in schemes)
                {
                    var url = $"{scheme}://{host}:{NtvPort}";
                    if (!await AppServices.Ntv.ProbeAsync(url, timeout)) continue;
                    s.NtvUrl = url;
                    AppServices.Ntv.Configure(url, null);
                    Log.Info("auto-setup: NostalgiaTV found on the Jellyfin host");
                    return true;
                }
                Log.Info("auto-setup: NostalgiaTV didn't answer on the Jellyfin host");
                return false;
            }

            bool ntvWasSet = AppServices.Ntv.IsConfigured || !string.IsNullOrWhiteSpace(s.NtvUrl);
            bool ntvFound = await TryNtvAsync(ProbeTimeout);
            if (ntvFound && !ntvWasSet) { changed = true; done.Add("Live TV"); }

            await AppServices.SeerrReady; // this profile's own Seerr sign-in, if it has one
            var owner = AppServices.Session.Current?.Id;
            if (!AppServices.Seerr.IsConfigured)
            {
                foreach (var scheme in schemes)
                {
                    var url = $"{scheme}://{host}:{SeerrPort}";
                    if (!await AppServices.Seerr.ProbeAsync(url, ProbeTimeout)) continue;
                    s.SeerrUrl = url; // remembered even if signing in fails, so Settings only needs a login
                    changed = true;
                    // The password only goes where it's no less protected than it was for Jellyfin:
                    // never downgraded from https to http, never in plain http across the internet.
                    bool downgrade = jf.Scheme == "https" && scheme == "http";
                    bool insecure = Nettle.Core.Api.NetworkSafety.IsInsecureRemote(url);
                    if ((downgrade || insecure) && password != null)
                        Log.Info("auto-setup: Seerr found over plain http; not sending the password there (sign in from Settings)");
                    else if (!string.IsNullOrEmpty(username) && password != null)
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                            await AppServices.Seerr.SignInWithJellyfinAsync(url, username, password, cts.Token);
                            if (AppServices.Session.Current?.Id != owner)
                            {
                                // Another profile took over meanwhile: this sign-in is not theirs to inherit.
                                AppServices.Seerr.Configure(null, null, null);
                                await AppServices.LoadSeerrForAsync(AppServices.Session.Current?.Id);
                                Log.Info("auto-setup: profile changed during Seerr sign-in; not kept");
                            }
                            else
                            {
                                await AppServices.SaveSeerrSecretsAsync(null, AppServices.Seerr.SessionCookie);
                                done.Add("Requests");
                                Log.Info("auto-setup: signed in to Seerr on the Jellyfin host");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppServices.Seerr.Configure(null, null, null);
                            Log.Info("auto-setup: Seerr found but sign-in failed: " + ex.Message);
                        }
                    }
                    else Log.Info("auto-setup: Seerr found; no password to sign in with (Quick Connect / profile without password)");
                    break;
                }
            }

            if (changed) await AppServices.Settings.SaveAsync();
            if (done.Count > 0)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => AppServices.Main?.Toast($"Set up {string.Join(" and ", done)} automatically", 4000));

            // One quick look right after sign-in isn't always enough: over a VPN or Tailscale the first
            // connection to a new port can take longer than the probe waits. Two more tries, spaced out.
            foreach (var wait in new[] { 6, 20 })
            {
                if (ntvFound) break;
                await Task.Delay(TimeSpan.FromSeconds(wait));
                if (AppServices.Session.Current == null) return; // signed out meanwhile
                ntvFound = await TryNtvAsync(TimeSpan.FromSeconds(10));
                if (!ntvFound || ntvWasSet) continue;
                await AppServices.Settings.SaveAsync();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => AppServices.Main?.Toast("Set up Live TV automatically", 4000));
            }
        }
        catch (Exception ex) { Log.Warn("auto-setup failed: " + ex.Message); }
    }
}
