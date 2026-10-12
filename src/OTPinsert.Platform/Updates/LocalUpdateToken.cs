using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OTPinsert.Platform;

internal static class LocalUpdateToken
{
    internal static byte[] RepositoryKey(string repository) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(repository.TrimEnd('/').ToLowerInvariant()));

    internal static string TokenPath(string repository, string? directory = null) =>
        Path.Combine(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OTPinsert", "update-auth"), Convert.ToHexString(RepositoryKey(repository)) + ".update-token.dat");

    internal static string? Load(string repository, string? directory = null)
    {
        var path = TokenPath(repository, directory);
        byte[]? plain = null;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            plain = ProtectedData.Unprotect(File.ReadAllBytes(path), RepositoryKey(repository), DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(plain).Trim();
            return token.Length == 0 ? null : token;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // An unavailable local credential must not prevent startup or public updates.
            return null;
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }
}
