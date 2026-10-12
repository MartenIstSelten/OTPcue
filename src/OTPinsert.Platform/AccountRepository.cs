using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace OTPinsert.Platform;

internal sealed class AccountRepository
{
    private readonly string file;
    public AccountRepository(string? directory = null)
    {
        file = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OTPinsert"), "accounts.dat");
    }
    public List<MailAccount> Load()
    {
        if (!File.Exists(file))
        {
            return new();
        }

        var encrypted = File.ReadAllBytes(file);
        var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try
        {
            var accounts = JsonSerializer.Deserialize<List<MailAccount>>(plain) ?? throw new InvalidDataException("Ungültige Kontodatei.");
            foreach (var account in accounts)
            {
                account.Validate();
            }

            return accounts;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Save(IEnumerable<MailAccount> accounts)
    {
        var snapshot = accounts.ToArray();
        foreach (var account in snapshot)
        {
            account.Validate();
        }

        var plain = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        byte[] encrypted;
        try
        {
            encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, file, true);
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
