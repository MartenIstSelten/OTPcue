using System.IO;

namespace OTPcue.Platform;

internal static class LegacyDataMigration
{
    internal static void CopyMissingFiles(string? localDirectory = null)
    {
        var local = localDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // Keep the previous installation's files intact during the rename.
        var previous = Path.Combine(local, "OtpAutofill");
        var current = Path.Combine(local, "OTPcue");
        foreach (var name in new[] { "accounts.dat", "settings.json" })
        {
            CopyIfMissing(Path.Combine(previous, name), Path.Combine(current, name));
        }

        var tokens = Path.Combine(previous, "update-auth");
        if (Directory.Exists(tokens))
        {
            foreach (var source in Directory.EnumerateFiles(tokens, "*.update-token.dat"))
            {
                CopyIfMissing(source, Path.Combine(current, "update-auth", Path.GetFileName(source)));
            }
        }
    }

    private static void CopyIfMissing(string source, string target)
    {
        if (!File.Exists(source) || File.Exists(target))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: false);
    }
}
