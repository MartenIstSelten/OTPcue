using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Responses;
using OTPinsert.Platform;
using Xunit;

public sealed class ProtectedTokenCacheTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "OTPinsert-cache-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "token.dat");

    private static byte[] Encrypt(string value)
        => ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);

    [Fact]
    public void UnchangedCacheDoesNotReadOrRewriteTokenBytes()
    {
        var cache = new ProtectedTokenCache(FilePath);
        var initial = cache.Read();
        cache.Commit(initial.Version, Encrypt("synthetic-token"));
        var loaded = cache.Read();
        var modified = File.GetLastWriteTimeUtc(FilePath);
        // Metadata checks are still allowed; any read or write of the file fails.
        using (File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Same(loaded.Encrypted, cache.Read().Encrypted);
            cache.Commit(loaded.Version, null);
        }
        Assert.Equal(modified, File.GetLastWriteTimeUtc(FilePath));
        Assert.DoesNotContain("synthetic-token", Encoding.UTF8.GetString(loaded.Encrypted!));
    }

    [Fact]
    public void DeletionInvalidatesInFlightAuthorizationEvenIfNoFileExisted()
    {
        var cache = new ProtectedTokenCache(FilePath);
        var initial = cache.Read();
        cache.Delete();
        Assert.Throws<InvalidOperationException>(() => cache.Commit(initial.Version, Encrypt("late-token")));
        Assert.False(File.Exists(FilePath));
        cache.Commit(cache.Read().Version, Encrypt("new-token"));
        var loaded = cache.Read();
        cache.Delete();
        Assert.Null(cache.Read().Encrypted);
        Assert.Throws<InvalidOperationException>(() => cache.Commit(loaded.Version, null));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ExternalReplacementAndDeletionInvalidateCachedCredentials()
    {
        var cache = new ProtectedTokenCache(FilePath);
        cache.Commit(cache.Read().Version, Encrypt("first-token"));
        var original = cache.Read();
        var replacement = Encrypt(new string('x', 1000));
        File.WriteAllBytes(FilePath, replacement);
        var reloaded = cache.Read();
        Assert.Equal(replacement, reloaded.Encrypted);
        Assert.NotEqual(original.Version, reloaded.Version);
        Assert.Throws<InvalidOperationException>(() => cache.Commit(original.Version, Encrypt("stale-token")));
        File.Delete(FilePath);
        Assert.Null(cache.Read().Encrypted);
        Assert.Throws<InvalidOperationException>(() => cache.Commit(reloaded.Version, null));
    }

    [Fact]
    public void RefreshedTokensRemainEncryptedAndSurviveRestart()
    {
        var cache = new ProtectedTokenCache(FilePath);
        var ciphertext = Encrypt("refreshed-token");
        cache.Commit(cache.Read().Version, ciphertext);
        Assert.Equal(ciphertext, new ProtectedTokenCache(FilePath).Read().Encrypted);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task PollingReusesAnUnexpiredTokenWithoutRewritingAndChecksAccountIdentity()
    {
        var account = new MailAccount(Guid.NewGuid(), "Test", "gmail.googleapis.com", 443,
            "user@example.test", "", false, true)
        {
            Provider = MailProvider.Google,
            OAuthClientId = "synthetic.apps.googleusercontent.com"
        };
        var snapshot = new
        {
            ClientId = account.OAuthClientId,
            Username = account.Username,
            Token = new TokenResponse
            {
                AccessToken = "synthetic-access-token",
                RefreshToken = "synthetic-refresh-token",
                ExpiresInSeconds = 3600,
                IssuedUtc = DateTime.UtcNow
            }
        };
        var cache = new ProtectedTokenCache(FilePath);
        cache.Commit(cache.Read().Version, Encrypt(JsonSerializer.Serialize(snapshot)));
        // Any file-content access fails here. A valid token needs no network request.
        using (File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(snapshot.Token.AccessToken,
                await GoogleMailAuth.GetTokenAsync(account, cache, false, CancellationToken.None));
            Assert.Equal(snapshot.Token.AccessToken,
                await GoogleMailAuth.GetTokenAsync(account, cache, false, CancellationToken.None));
            await Assert.ThrowsAsync<MailAuthenticationRequiredException>(() =>
                GoogleMailAuth.GetTokenAsync(account with
                {
                    Username = "other@example.test"
                },
                    cache, false, CancellationToken.None));
            await Assert.ThrowsAsync<MailAuthenticationRequiredException>(() =>
                GoogleMailAuth.GetTokenAsync(account with
                {
                    OAuthClientId = "other.apps.googleusercontent.com"
                },
                    cache, false, CancellationToken.None));
        }
        cache.Delete();
        await Assert.ThrowsAsync<MailAuthenticationRequiredException>(() =>
            GoogleMailAuth.GetTokenAsync(account, cache, false, CancellationToken.None));
    }

    public void Dispose()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory);
        }
    }
}
