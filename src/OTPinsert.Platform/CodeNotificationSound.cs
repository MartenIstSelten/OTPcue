using System.Buffers.Binary;
using System.IO;
using System.Media;

namespace OTPinsert.Platform;

// Owns the bundled chime and its playback resources. Call on the UI thread.
internal sealed class CodeNotificationSound : IDisposable
{
    private Stream? stream;
    private SoundPlayer? player;
    private int loadedVolume = -1;

    internal void Play(int volumePercent)
    {
        if (volumePercent == 0)
        {
            return;
        }

        try
        {
            if (player is null || loadedVolume != volumePercent)
            {
                Dispose();
                using var resource = typeof(CodeNotificationSound).Assembly.GetManifestResourceStream(
                    "OTPinsert.Platform.Resources.NewCode.wav")
                    ?? throw new InvalidDataException("The notification sound is missing.");
                using var buffer = new MemoryStream();
                resource.CopyTo(buffer);
                stream = new MemoryStream(ApplyVolume(buffer.ToArray(), volumePercent), writable: false);
                player = new SoundPlayer(stream);
                player.Load();
                loadedVolume = volumePercent;
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
        player?.Stop();
        player?.Dispose();
        player = null;
        stream?.Dispose();
        stream = null;
        loadedVolume = -1;
    }

    internal static byte[] ApplyVolume(byte[] wave, int volumePercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(volumePercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volumePercent, 100);

        // The bundled generator writes a fixed 44-byte header and 16-bit PCM samples.
        // Scale the samples, never the shared Windows device or application volume.
        if (wave.Length < 44 || (wave.Length - 44) % 2 != 0 ||
            !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !wave.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(16, 4)) != 16 ||
            BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(20, 2)) != 1 ||
            BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(34, 2)) != 16 ||
            !wave.AsSpan(36, 4).SequenceEqual("data"u8))
        {
            throw new InvalidDataException("The bundled sound must be a 16-bit PCM WAV.");
        }

        var scaled = (byte[])wave.Clone();
        for (var offset = 44; offset < scaled.Length; offset += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(scaled.AsSpan(offset, 2));
            var adjusted = (short)Math.Round(sample * (volumePercent / 100.0));
            BinaryPrimitives.WriteInt16LittleEndian(scaled.AsSpan(offset, 2), adjusted);
        }
        return scaled;
    }
}
