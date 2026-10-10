using Xunit;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using OTPcue.Platform;
using Localization = OTPcue.Platform.Localization;

public sealed class AccountTests
{
    [Fact]
    public void PreservesLegacyData()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "migration-tests", Guid.NewGuid().ToString("N")));
        var previous = Path.Combine(root, "OtpAutofill");
        var current = Path.Combine(root, "OTPcue");
        Directory.CreateDirectory(Path.Combine(previous, "update-auth"));
        File.WriteAllBytes(Path.Combine(previous, "accounts.dat"), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(previous, "settings.json"), "legacy");
        File.WriteAllBytes(Path.Combine(previous, "update-auth", "test.update-token.dat"), new byte[] { 4, 5 });
        LegacyDataMigration.CopyMissingFiles(root);
        Check(File.ReadAllBytes(Path.Combine(current, "accounts.dat")).SequenceEqual(new byte[] { 1, 2, 3 }), "migration preserves encrypted account bytes");
        Check(File.ReadAllBytes(Path.Combine(current, "update-auth", "test.update-token.dat")).SequenceEqual(new byte[] { 4, 5 }), "migration preserves encrypted update token bytes");
        Check(File.ReadAllText(Path.Combine(current, "settings.json")) == "legacy", "migration copies settings");
        File.WriteAllText(Path.Combine(current, "settings.json"), "current");
        LegacyDataMigration.CopyMissingFiles(root);
        Check(File.ReadAllText(Path.Combine(current, "settings.json")) == "current", "migration never overwrites current settings");
        Check(File.Exists(Path.Combine(previous, "accounts.dat")), "migration retains original accounts");
    }
    [Fact]
    public void ResolvesLanguageAndTranslations()
    {
        foreach (var culture in new[] { "de-DE", "de-AT", "de-CH", "en-US", "fr-FR", "ja-JP" })
        {
            Check(Localization.ResolveLanguage("system", new System.Globalization.CultureInfo(culture)) == (culture.StartsWith("de") ? "de" : "en"), "system language selection: " + culture);
        }

        Check(Localization.ResolveLanguage("en", new System.Globalization.CultureInfo("de-DE")) == "en", "English overrides German system");
        Check(Localization.ResolveLanguage("de", new System.Globalization.CultureInfo("fr-FR")) == "de", "German overrides other system languages");
        Check(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"CountdownSeconds\":60}")!.Language == "system", "legacy settings default to system language");
        foreach (var language in new[] { "de", "en", "system" })
        {
            new AppSettings(Language: language).Validate();
        }

        var rejected = false;
        try
        {
            new AppSettings(Language: "fr").Validate();
        }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "unsupported preferences are rejected");
        Localization.Configure("en");
        Check(Localization.Text(TextId.Settings) == "Settings", "English interface translation");
        Check(Localization.Format(TextId.AccessibleCountdown, 42) == "Code valid for another 42 seconds", "accessible countdown translation");
        Localization.Configure("de");
        Check(Localization.Text(TextId.Settings) == "Einstellungen", "German interface translation");
    }
    [Fact]
    public void PersistsValidatedAccountsWithDpapi()
    {
        var directory = Path.GetFullPath(Path.Combine("artifacts", "account-tests", Guid.NewGuid().ToString("N")));
        var repository = new AccountRepository(directory);
        var settingsRepository = new SettingsRepository(directory);
        Check(!settingsRepository.Exists, "first launch has no saved settings");
        Check(settingsRepository.Load() == new AppSettings(), "settings default to 60 seconds and single use");
        Check(settingsRepository.Load() is { StartWithWindows: true, StartInTray: true, CloseToTray: true }, "first launch enables startup and tray defaults");
        var settings = new AppSettings(120, false, true, StartWithWindows: false, StartInTray: false, CloseToTray: false, Language: "en");
        settingsRepository.Save(settings);
        Check(settingsRepository.Exists, "saved settings mark first launch complete");
        Check(new SettingsRepository(directory).Load() == settings, "settings survive restart");
        var invalid = false;
        try
        {
            settingsRepository.Save(settings with
            {
                CountdownSeconds = 0
            });
        }
        catch (InvalidDataException) { invalid = true; }
        Check(invalid && settingsRepository.Load() == settings, "invalid settings preserve saved values");
        var first = new MailAccount(Guid.NewGuid(), "Privat", "imap.example.test", 993, "first@example.test", "TEST-secret-123", false, true);
        Check(first.PollingIntervalSeconds == 3, "new accounts default to three-second polling");
        var second = first with
        {
            Id = Guid.NewGuid(),
            DisplayName = "Arbeit",
            Username = "second@example.test",
            Password = "TEST-secret-456",
            StartTls = true,
            Port = 143,
            FolderId = "INBOX/Bestätigungen",
            FolderName = "INBOX/Bestätigungen"
        };
        Check(repository.Load().Count == 0, "missing storage returns empty accounts");
        repository.Save(new[] { first, second });
        Check(repository.Load().SequenceEqual(new[] { first, second }), "multiple credentials roundtrip through DPAPI");
        var google = first with
        {
            Provider = MailProvider.Google,
            Host = "gmail.googleapis.com",
            Port = 443,
            Password = "",
            OAuthClientId = "TEST.apps.googleusercontent.com",
            OAuthClientSecret = "TEST-desktop-config"
        };
        repository.Save(new[] { google });
        Check(repository.Load().Single() == google, "Google desktop configuration roundtrips through encrypted account storage");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "accounts.dat"))).Contains(google.OAuthClientSecret), "Google desktop configuration is encrypted on disk");
        repository.Save(new[] { google });
        foreach (var bad in new[] { google with { OAuthClientId = "secret" }, google with { Host = "imap.example.test" }, google with { StartTls = true }, google with { Port = 993 }, first with { Provider = (MailProvider)999 }, first with { FolderId = "" }, first with { FolderName = " " } })
        {
            var invalidProvider = false;
            try
            {
                repository.Save(new[] { bad });
            }
            catch (InvalidDataException) { invalidProvider = true; }
            Check(invalidProvider && repository.Load().Single() == google, "invalid provider configuration preserves saved account");
        }
        repository.Save(new[] { first, second });
        var file = Path.Combine(directory, "accounts.dat");
        var bytes = File.ReadAllBytes(file);
        var visible = Encoding.UTF8.GetString(bytes);
        Check(!visible.Contains(first.Password) && !visible.Contains(second.Username), "credentials and metadata are encrypted on disk");
        Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "atomic save leaves no temporary file");
        var edited = second with
        {
            Enabled = false,
            Host = "other.example.test",
            PollingIntervalSeconds = 15
        };
        repository.Save(new[] { edited });
        Check(repository.Load().Single() == edited, "edit, disable and remove persist");
        foreach (var interval in new[] { 0, -1, 601 })
        {
            var invalidPolling = false;
            try
            {
                repository.Save(new[] { edited with { PollingIntervalSeconds = interval } });
            }
            catch (InvalidDataException) { invalidPolling = true; }
            Check(invalidPolling && repository.Load().Single() == edited, "invalid polling interval preserves saved account: " + interval);
        }
        var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new[] { first }))!;
        legacyJson[0]!.AsObject().Remove("PollingIntervalSeconds");
        legacyJson[0]!.AsObject().Remove("Provider");
        legacyJson[0]!.AsObject().Remove("OAuthClientId");
        legacyJson[0]!.AsObject().Remove("OAuthClientSecret");
        legacyJson[0]!.AsObject().Remove("FolderId");
        legacyJson[0]!.AsObject().Remove("FolderName");
        var legacyPlain = Encoding.UTF8.GetBytes(legacyJson.ToJsonString());
        File.WriteAllBytes(file, ProtectedData.Protect(legacyPlain, null, DataProtectionScope.CurrentUser));
        CryptographicOperations.ZeroMemory(legacyPlain);
        Check(repository.Load().Single() == first, "existing accounts without polling setting retain three-second default");
        repository.Save(new[] { edited });
        bytes = File.ReadAllBytes(file);
        bytes[bytes.Length / 2] ^= 0x42;
        File.WriteAllBytes(file, bytes);
        var rejected = false;
        try
        {
            repository.Load();
        }
        catch (CryptographicException) { rejected = true; }
        Check(rejected && File.ReadAllBytes(file).SequenceEqual(bytes), "damaged store is rejected without being overwritten");
        repository.Save(Array.Empty<MailAccount>());
        Check(repository.Load().Count == 0, "remove all accounts persists");
        Console.WriteLine("All account persistence checks passed.");
    }
    private static void Check(bool result, string name)
    {
        if (!result)
        {
            throw new Exception("FAIL: " + name);
        }

        Console.WriteLine("PASS: " + name);
    }

    [Fact]
    public void RollsBackStartupWhenSettingsSaveFails()
    {
        var settings = new AppSettings(StartWithWindows: true);
        string? command = "original startup command";
        var original = command;
        var saveError = new IOException("synthetic write failure");
        var service = new SettingsService(_ => throw saveError, () => command,
            _ => command = "new command", previous => command = previous);
        Exception? failure = null;
        try
        {
            service.Save(settings);
        }
        catch (IOException ex) { failure = ex; }
        Check(ReferenceEquals(failure, saveError) && command == original,
            "Failed settings write restores the exact previous startup command");

        var changed = false;
        service = new SettingsService(_ => changed = true, () => throw new IOException("read failed"),
            _ => changed = true, _ => changed = true);
        try
        {
            service.Save(settings);
        }
        catch (IOException) { }
        Check(!changed, "Failed startup read cannot alter startup or settings");

        var restoreError = new UnauthorizedAccessException("synthetic restore failure");
        service = new SettingsService(_ => throw saveError, () => original,
            _ => { }, _ => throw restoreError);
        AggregateException? combined = null;
        try
        {
            service.Save(settings);
        }
        catch (AggregateException ex) { combined = ex; }
        Check(combined?.InnerExceptions.SequenceEqual(new Exception[] { saveError, restoreError }) == true,
            "Rollback failure preserves both errors for the caller");

        AppSettings? saved = null;
        service = new SettingsService(value => saved = value, () => command,
            enabled => command = enabled ? "enabled" : null, _ => throw new Exception("Unexpected rollback"));
        service.Save(settings);
        Check(saved == settings && command == "enabled", "Successful transaction saves settings and startup");
    }

    [Fact]
    public void ValidatesAccountForm()
    {
        var draft = new AccountDraft(Guid.NewGuid(), "  ", " imap.example.test ", 993,
            " user@example.test ", "test password", false, true, 3, MailProvider.Custom, "", "");
        var account = draft.ToAccount();
        Check(account.DisplayName == "user@example.test" && account.Username == "user@example.test"
            && account.Host == "imap.example.test" && account.Password == "test password",
            "Account draft trims metadata and preserves the password");
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, 0, 1.5, 601 })
        {
            Check((draft with
            {
                PollingInterval = value
            }).Validate()?.Field == AccountField.PollingInterval,
                "Invalid form polling value is rejected: " + value);
        }
        foreach (var value in new[] { double.NaN, double.NegativeInfinity, 0, 443.5, 65536 })
        {
            Check((draft with
            {
                Port = value
            }).Validate()?.Field == AccountField.Port,
                "Invalid form port is rejected: " + value);
        }
        Check((draft with
        {
            Username = " "
        }).Validate()?.Field == AccountField.Username,
            "Empty account username has a field-specific error");
        Check((draft with
        {
            Provider = (MailProvider)(-1)
        }).Validate()?.Field == AccountField.Provider,
            "Missing provider selection cannot create an account");
        Check((draft with
        {
            Password = ""
        }).Validate()?.Field == AccountField.Password,
            "IMAP requires a password");
        var google = draft with
        {
            Provider = MailProvider.Google,
            Host = "gmail.googleapis.com",
            Port = 443,
            ClientId = "TEST.apps.googleusercontent.com",
            ClientSecret = "TEST desktop",
            Password = "ignored"
        };
        Check(google.ToAccount().Password == "" && google.ToAccount().OAuthClientSecret == "TEST desktop",
            "OAuth account never retains an IMAP password");
    }

    [Fact]
    public async Task CancelsAllAccountsBeforeAwaitingWorkers()
    {
        var first = new MailAccount(Guid.NewGuid(), "Test", "imap.example.test", 993, "test", "test", false, true);
        var second = first with
        {
            Id = Guid.NewGuid()
        };
        using var started = new System.Threading.SemaphoreSlim(0);
        var bothCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellations = 0;
        var lateStatuses = 0;
        CancellationToken firstToken = default;
        var monitor = new AccountMonitor(async (_, report, stop) =>
        {
            using var registration = stop.Register(() =>
            {
                if (Interlocked.Increment(ref cancellations) == 2)
                {
                    bothCancelled.TrySetResult();
                }
            });
            report(new(TextId.StartingConnection));
            started.Release();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                report(new(TextId.Disabled));
                // Both workers must receive cancellation before either is awaited.
                await bothCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                throw;
            }
        });
        void Status(LocalizedText message, CancellationToken token)
        {
            if (message.Id == TextId.Disabled)
            {
                Interlocked.Increment(ref lateStatuses);
            }
            else
            {
                firstToken = token;
            }
        }
        try
        {
            monitor.Start(first, Status);
            var duplicateRejected = false;
            try
            {
                monitor.Start(first, Status);
            }
            catch (InvalidOperationException) { duplicateRejected = true; }
            Check(duplicateRejected, "An account cannot start duplicate background workers");
            monitor.Start(second, Status);
            Check(await started.WaitAsync(TimeSpan.FromSeconds(5)) && await started.WaitAsync(TimeSpan.FromSeconds(5)),
                "Both account workers started");
            await monitor.StopAllAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(cancellations == 2 && lateStatuses == 0 && firstToken.IsCancellationRequested,
                "Shutdown cancels all accounts and suppresses stale status callbacks");
            monitor.Start(first, Status);
            Check(await started.WaitAsync(TimeSpan.FromSeconds(5)), "Stopped account can restart");
            await monitor.StopAsync(first.Id).WaitAsync(TimeSpan.FromSeconds(5));
            await monitor.StopAsync(first.Id);
            Check(cancellations == 3, "Repeated stop is safe and restart has a new cancellation source");
        }
        finally { await monitor.StopAllAsync(); }
    }
}
