namespace Nettle.Desktop;

/// <summary>
/// Nettle was called Slopfin up to version 0.2.0. On the first start after the rename, the data
/// saved under the old name (settings, signed-in accounts, image cache) is moved to the new
/// folder, so nobody is signed out or loses their settings. Nothing is deleted: if the move can't
/// happen (say the old version is still open), the data is copied and the old folder stays.
/// </summary>
internal static class LegacyData
{
    public const string OldName = "Slopfin";

    /// <summary>What happened, written to the log once logging has started.</summary>
    public static readonly List<string> Notes = [];

    public static void MoveFolder(string oldDir, string newDir, string what)
    {
        try
        {
            if (!Directory.Exists(oldDir)) return;
            if (Directory.Exists(newDir))
            {
                // A new folder holding no settings or sign-ins yet (only a log, say) still gets the old data.
                if (!IsUnused(newDir)) return;
                MergeInto(oldDir, newDir);
                Notes.Add($"moved the {what} from the old name ({OldName}) into the new folder");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(newDir)!);
            try
            {
                Directory.Move(oldDir, newDir);
                Notes.Add($"moved the {what} folder from the old name ({OldName})");
            }
            catch (Exception moveError)
            {
                try
                {
                    CopyTree(oldDir, newDir);
                    Notes.Add($"copied the {what} folder from the old name ({OldName}); the old folder couldn't be moved: {moveError.Message}");
                }
                catch (Exception copyError)
                {
                    // Leave no half-copied folder behind, so the next start tries again.
                    try { Directory.Delete(newDir, recursive: true); } catch { }
                    Notes.Add($"couldn't bring over the {what} folder from the old name ({OldName}): {copyError.Message}");
                }
            }
        }
        catch (Exception ex) { Notes.Add($"{what} folder migration skipped: {ex.Message}"); }
    }

    private static bool IsUnused(string dir)
    {
        if (File.Exists(Path.Combine(dir, "settings.json"))) return false;
        var secure = Path.Combine(dir, "secure");
        return !Directory.Exists(secure) || !Directory.EnumerateFileSystemEntries(secure).Any();
    }

    /// <summary>Moves each top-level item of <paramref name="from"/> into <paramref name="to"/>, keeping anything already there.</summary>
    private static void MergeInto(string from, string to)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(from).ToList())
        {
            var name = Path.GetFileName(entry);
            if (name == "instance.lock") continue;
            var target = Path.Combine(to, name);
            if (Directory.Exists(entry))
            {
                if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target);
                if (!Directory.Exists(target)) Directory.Move(entry, target);
            }
            else if (!File.Exists(target)) File.Move(entry, target);
        }
        try { if (!Directory.EnumerateFileSystemEntries(from).Any(e => Path.GetFileName(e) != "instance.lock")) Directory.Delete(from, recursive: true); }
        catch { /* leftovers stay; nothing important */ }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "instance.lock") continue; // held by the running old version
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }
}
