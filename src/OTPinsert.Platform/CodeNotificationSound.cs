using System.IO;
using System.Media;

namespace OTPinsert.Platform;

// Owns the bundled chime and its playback resources. Call on the UI thread.
internal sealed class CodeNotificationSound : IDisposable
{
    private Stream? stream;
    private SoundPlayer? player;

    internal void Play()
    {
        try
        {
            if (player is null)
            {
                stream = typeof(CodeNotificationSound).Assembly.GetManifestResourceStream(
                    "OTPinsert.Platform.Resources.NewCode.wav")
                    ?? throw new InvalidDataException("The notification sound is missing.");
                player = new SoundPlayer(stream);
                player.Load();
            }

            // Playback is asynchronous; mail monitoring and the UI stay responsive.
            player.Play();
        }
        catch (Exception exception)
        {
            Dispose();
            AppDiagnostics.ReportFailure("NotificationSound.Play", exception);
        }
    }

    public void Dispose()
    {
        player?.Dispose();
        player = null;
        stream?.Dispose();
        stream = null;
    }
}
