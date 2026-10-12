using System.IO;

namespace OTPinsert.Platform;

// Retains only DPAPI ciphertext. All file operations and invalidation are serialized;
// an in-flight sign-in cannot recreate credentials deleted through this cache.
internal sealed class ProtectedTokenCache(string path)
{
    private readonly object sync = new();
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    private byte[]? encrypted;
    private (DateTime LastWrite, long Length)? stamp;
    private long version;

    internal (byte[]? Encrypted, long Version) Read()
    {
        lock (sync)
        {
            Refresh();
            return (encrypted, version);
        }
    }

    internal void Commit(long expectedVersion, byte[]? replacement)
    {
        lock (sync)
        {
            Refresh();
            if (version != expectedVersion)
            {
                throw new InvalidOperationException("OAuth cache changed during authorization. Please sign in again.");
            }
            if (replacement is null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, replacement);
                File.Move(temporary, path, true);
                var writtenStamp = GetStamp() ?? throw new IOException("OAuth cache disappeared while writing.");
                encrypted = replacement;
                stamp = writtenStamp;
                version++;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    internal void Delete()
    {
        lock (sync)
        {
            encrypted = null;
            stamp = null;
            version++;
            try
            {
                File.Delete(path);
            }
            catch (DirectoryNotFoundException) { /* No credential directory yet. */ }
        }
    }

    private void Refresh()
    {
        var currentStamp = GetStamp();
        if (currentStamp == stamp)
        {
            return;
        }
        encrypted = null;
        stamp = null;
        version++;
        if (currentStamp is not null)
        {
            var bytes = File.ReadAllBytes(path);
            // If another process replaces the file while reading, fail instead of
            // retaining bytes whose metadata belongs to a different file version.
            if (GetStamp() != currentStamp)
            {
                throw new IOException("OAuth cache changed while reading.");
            }
            encrypted = bytes;
            stamp = currentStamp;
        }
    }

    private (DateTime LastWrite, long Length)? GetStamp()
    {
        var info = new FileInfo(path);
        return info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
    }
}
