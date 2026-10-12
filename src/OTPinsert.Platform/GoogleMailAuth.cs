using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Util.Store;

namespace OTPinsert.Platform;

internal static class GoogleMailAuth
{
    internal const string Scope = "https://www.googleapis.com/auth/gmail.readonly";
    private static readonly ConcurrentDictionary<Guid, ProtectedTokenCache> Caches = new();
    private static ProtectedTokenCache Cache(Guid id) => Caches.GetOrAdd(id, key => new ProtectedTokenCache(CachePath(key)));
    private static string CachePath(Guid id) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OTPinsert", "oauth", id.ToString("N") + ".google.dat");
    internal static bool HasCache(Guid id) => File.Exists(CachePath(id));
    internal static void DeleteCache(Guid id) => Cache(id).Delete();

    internal static PkceGoogleAuthorizationCodeFlow CreateFlow(MailAccount account, IDataStore? dataStore = null)
        => new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = account.OAuthClientId, ClientSecret = account.OAuthClientSecret },
            Scopes = [Scope],
            DataStore = dataStore ?? new MemoryTokenStore(),
            LoginHint = account.Username,
            Prompt = "consent select_account"
        });

    internal static Task<string> GetTokenAsync(MailAccount account, bool interactive, CancellationToken stop, IProgress<string>? progress = null)
        => GetTokenAsync(account, Cache(account.Id), interactive, stop, progress);

    internal static async Task<string> GetTokenAsync(MailAccount account, ProtectedTokenCache cache,
        bool interactive, CancellationToken stop, IProgress<string>? progress = null)
    {
        account.Validate();
        var gate = cache.Gate;
        await gate.WaitAsync(stop);
        try
        {
            var (encryptedSnapshot, version) = cache.Read();
            byte[]? originalHash = null;
            var memory = new MemoryTokenStore();
            if (encryptedSnapshot is not null)
            {
                var plain = ProtectedData.Unprotect(encryptedSnapshot, null, DataProtectionScope.CurrentUser);
                try
                {
                    originalHash = SHA256.HashData(plain);
                    var snapshot = JsonSerializer.Deserialize<TokenSnapshot>(plain) ?? throw new InvalidDataException("Ungültiger OAuth-Cache.");
                    if (snapshot.ClientId == account.OAuthClientId && string.Equals(snapshot.Username, account.Username, StringComparison.OrdinalIgnoreCase))
                    {
                        memory.Token = snapshot.Token;
                    }
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            using var flow = CreateFlow(account, memory);
            UserCredential credential;
            string token;
            try
            {
                if (interactive)
                {
                    credential = await new AuthorizationCodeInstalledApp(flow, new ProgressCodeReceiver(progress)).AuthorizeAsync(account.Username, stop);
                }
                else if (memory.Token is not null)
                {
                    credential = new UserCredential(flow, account.Username, memory.Token);
                }
                else
                {
                    throw new MailAuthenticationRequiredException(new InvalidOperationException("Google-Anmeldung fehlt."));
                }

                token = await credential.GetAccessTokenForRequestAsync(cancellationToken: stop);
            }
            catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
            {
                if (!interactive)
                {
                    throw new MailAuthenticationRequiredException(ex);
                }

                memory.Token = null;
                credential = await new AuthorizationCodeInstalledApp(flow, new ProgressCodeReceiver(progress)).AuthorizeAsync(account.Username, stop);
                token = await credential.GetAccessTokenForRequestAsync(cancellationToken: stop);
            }
            if (interactive)
            {
                progress?.Report(Localization.Text(TextId.GoogleSignInReceivedCheckingGmailAccess));
                using var profile = await GmailApi.GetAsync("profile", token, stop);
                var email = profile.RootElement.GetProperty("emailAddress").GetString();
                if (!string.Equals(email, account.Username, StringComparison.OrdinalIgnoreCase))
                {
                    throw new GoogleAccountMismatchException();
                }
            }
            stop.ThrowIfCancellationRequested();
            var serialized = JsonSerializer.SerializeToUtf8Bytes(new TokenSnapshot(account.OAuthClientId, account.Username, credential.Token));
            try
            {
                // Compare serialized snapshots before encrypting: DPAPI ciphertext is
                // randomized even when the underlying token has not changed.
                var unchanged = originalHash is not null
                    && CryptographicOperations.FixedTimeEquals(originalHash, SHA256.HashData(serialized));
                stop.ThrowIfCancellationRequested();
                cache.Commit(version, unchanged ? null
                    : ProtectedData.Protect(serialized, null, DataProtectionScope.CurrentUser));
            }
            finally { CryptographicOperations.ZeroMemory(serialized); }
            return token;
        }
        finally { gate.Release(); }
    }

    private sealed record TokenSnapshot(string ClientId, string Username, TokenResponse Token);
    private sealed class ProgressCodeReceiver(IProgress<string>? progress) : ICodeReceiver
    {
        private readonly LocalServerCodeReceiver _receiver = new("<html><head><meta charset=\"utf-8\"><title>Google-Anmeldung</title></head><body>Der Anmeldecode wurde an OTPinsert &uuml;bergeben. Die App pr&uuml;ft jetzt die Anmeldung. Bitte zur App zur&uuml;ckkehren.</body></html>");
        public string RedirectUri => _receiver.RedirectUri;
        public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url, CancellationToken stop)
        {
            progress?.Report(Localization.Text(TextId.CompleteGoogleSignIn));
            var response = await _receiver.ReceiveCodeAsync(url, stop).ConfigureAwait(false);
            progress?.Report(Localization.Text(TextId.BrowserCallbackReceivedConfirmingGoogleAccess));
            return response;
        }
    }

    internal static string DescribeSignInFailure(TokenResponseException error)
    {
        if (error.Error?.Error == "invalid_request" && error.Error.ErrorDescription?.Contains("client_secret", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Localization.Text(TextId.GoogleClientSecretRequired);
        }

        return error.Error?.Error switch
        {
            "invalid_client" => Localization.Text(TextId.GoogleClientRejected),
            "invalid_grant" => Localization.Text(TextId.GoogleAuthorizationExpired),
            "access_denied" => Localization.Text(TextId.GoogleConsentRequired),
            _ => Localization.Text(TextId.GoogleSignInFailed)
        };
    }
    // Google's SDK stores and refreshes tokens in memory. Persist only after checking identity.
    private sealed class MemoryTokenStore : IDataStore
    {
        internal TokenResponse? Token;
        public Task StoreAsync<T>(string key, T value)
        {
            Token = value as TokenResponse ?? throw new InvalidDataException("Unbekannter OAuth-Datentyp.");
            return Task.CompletedTask;
        }
        public Task DeleteAsync<T>(string key)
        {
            Token = null;
            return Task.CompletedTask;
        }
        public Task ClearAsync()
        {
            Token = null;
            return Task.CompletedTask;
        }
        public Task<T> GetAsync<T>(string key) => Task.FromResult(Token is T value ? value : default!);
    }
}

internal sealed class GoogleAccountMismatchException : Exception
{
    internal GoogleAccountMismatchException() : base(Localization.Text(TextId.GoogleSignInNeeded)) { }
}
