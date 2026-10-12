using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using OTPinsert.Platform;
using Xunit;

public sealed class MailFolderTests
{
    [Theory]
    [InlineData("INBOX")]
    [InlineData("INBOX/Codes")]
    public async Task OpensTheSelectedFolderReadOnly(string folderId)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var commands = new List<string>();
        var server = ServeAsync(listener, commands, stop.Token);
        try
        {
            using var client = new ImapClient();
            // Only this synthetic loopback server uses plaintext; production connections
            // continue to require SSL or STARTTLS and normal certificate validation.
            await client.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, SecureSocketOptions.None, stop.Token);
            await client.AuthenticateAsync("user", "synthetic-password", stop.Token);
            var folder = await MailFolders.OpenImapFolderAsync(client, folderId, stop.Token);
            Assert.Equal(folderId, folder.FullName);
            Assert.Equal(FolderAccess.ReadOnly, folder.Access);
            await client.DisconnectAsync(true, stop.Token);
            await server;
            Assert.Contains(commands, command => command.Contains(" EXAMINE ") && command.Contains(folderId));
            Assert.DoesNotContain(commands, command => command.Contains(" SELECT "));
        }
        finally
        {
            stop.Cancel();
            try
            {
                await server;
            }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task ServeAsync(TcpListener listener, List<string> commands, CancellationToken stop)
    {
        using var connection = await listener.AcceptTcpClientAsync(stop);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
        await writer.WriteLineAsync("* OK [CAPABILITY IMAP4rev1] Test server ready");
        while (await reader.ReadLineAsync(stop) is { } line)
        {
            commands.Add(line);
            var parts = line.Split(' ', 3);
            var tag = parts[0];
            switch (parts[1])
            {
                case "CAPABILITY":
                    await writer.WriteLineAsync("* CAPABILITY IMAP4rev1");
                    break;
                case "LOGIN":
                    break;
                case "LIST":
                    var folder = line.Contains("INBOX/Codes") ? "INBOX/Codes" : line.EndsWith("\"\" \"\"") ? "" : "INBOX";
                    await writer.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" \"" + folder + "\"");
                    break;
                case "EXAMINE":
                    await writer.WriteLineAsync("* 0 EXISTS\r\n* 0 RECENT\r\n* OK [UIDVALIDITY 1] Valid\r\n* OK [UIDNEXT 1] Next");
                    await writer.WriteLineAsync(tag + " OK [READ-ONLY] Opened");
                    continue;
                case "LOGOUT":
                    await writer.WriteLineAsync("* BYE Closing\r\n" + tag + " OK Logout");
                    return;
                default:
                    await writer.WriteLineAsync(tag + " BAD Unsupported command");
                    continue;
            }
            await writer.WriteLineAsync(tag + " OK Completed");
        }
    }
}
