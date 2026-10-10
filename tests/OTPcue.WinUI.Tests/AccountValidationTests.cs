using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using OTPcue.Platform;
using Xunit;

public sealed class AccountValidationTests
{
    private static MailAccount ValidAccount() => new(Guid.NewGuid(), "Test", "imap.example.test", 993,
        "user@example.test", "synthetic-password", false, true);

    public static IEnumerable<object[]> InvalidAccounts()
    {
        var valid = ValidAccount();
        yield return [valid with { Username = " " }, AccountField.Username];
        yield return [valid with { Username = null! }, AccountField.Username];
        yield return [valid with { Host = "https://imap.example.test" }, AccountField.Host];
        yield return [valid with { Host = null! }, AccountField.Host];
        yield return [valid with { Port = 0 }, AccountField.Port];
        yield return [valid with { Port = 65536 }, AccountField.Port];
        yield return [valid with { Password = "" }, AccountField.Password];
        yield return [valid with { Password = null! }, AccountField.Password];
    }

    [Theory]
    [MemberData(nameof(InvalidAccounts))]
    public void FormAndModelRejectTheSameInvalidValues(object value, object field)
    {
        var account = (MailAccount)value;
        Assert.Throws<InvalidDataException>(account.Validate);
        var draft = new AccountDraft(account.Id, account.DisplayName, account.Host, account.Port,
            account.Username, account.Password, account.StartTls, account.Enabled,
            account.PollingIntervalSeconds, account.Provider, account.OAuthClientId, account.OAuthClientSecret);
        Assert.Equal((AccountField)field, draft.Validate()?.Field);
    }

    [Fact]
    public void LoadingInvalidStoredAccountsFailsWithoutOverwritingTheFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OTPcue-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "accounts.dat");
            var plain = JsonSerializer.SerializeToUtf8Bytes(new[] { ValidAccount() with { Port = -1 } });
            byte[] encrypted;
            try
            {
                encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
            File.WriteAllBytes(file, encrypted);
            Assert.Throws<InvalidDataException>(() => new AccountRepository(directory).Load());
            Assert.Equal(encrypted, File.ReadAllBytes(file));
        }
        finally
        {
            File.Delete(Path.Combine(directory, "accounts.dat"));
            Directory.Delete(directory);
        }
    }
}
