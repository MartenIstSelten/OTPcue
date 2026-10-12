using System.IO;
using System.Diagnostics.Tracing;
using OTPinsert.Platform;
using Xunit;

public sealed class DiagnosticsTests
{
    [Fact]
    public void DiagnosticsExcludeExceptionMessagesDataAndInnerExceptions()
    {
        using var listener = new DiagnosticListener();
        var exception = new IOException("PRIVATE-mail-123456", new Exception("PRIVATE-token"));
        exception.Data["account"] = "PRIVATE-user@example.test";
        AppDiagnostics.ReportFailure("FocusTracking.ReadTarget", exception);

        var entry = Assert.Single(listener.Events);
        Assert.Equal(1, entry.EventId);
        Assert.Equal(EventLevel.Warning, entry.Level);
        Assert.Equal(new object[] { "FocusTracking.ReadTarget", nameof(IOException), exception.HResult }, entry.Payload!.ToArray());
    }

    [Fact]
    public async Task UpdateFailuresEmitSafeDiagnosticsAndKeepTheServiceRetryable()
    {
        using var listener = new DiagnosticListener();
        var backend = new FailingUpdateBackend();
        var service = new UpdateService(backend);
        await service.CheckAsync(CancellationToken.None);
        Assert.False(service.State.Busy);
        Assert.False(await service.DownloadAsync(CancellationToken.None));
        Assert.False(service.State.Busy);
        Assert.False(service.State.Ready);

        Assert.Equal(2, listener.Events.Count);
        Assert.Equal("Updates.Check", listener.Events[0].Payload![0]);
        Assert.Equal("Updates.Download", listener.Events[1].Payload![0]);
        foreach (var entry in listener.Events)
        {
            Assert.Equal(1, entry.EventId);
            Assert.Equal(3, entry.Payload!.Count);
            Assert.Equal(nameof(IOException), entry.Payload[1]);
            Assert.DoesNotContain("PRIVATE", string.Join(" ", entry.Payload));
        }
        Assert.DoesNotContain("PRIVATE", service.State.Message!.ToString());

        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckAsync(stop.Token));
        Assert.Equal(2, listener.Events.Count);
    }

    private sealed class FailingUpdateBackend : IUpdateBackend
    {
        public bool IsInstalled => true;
        public string CurrentVersion => "0.1.0";
        public string? AvailableVersion => "0.2.0";
        public bool IsDownloaded => false;
        public Task CheckAsync() => Task.FromException(new IOException("PRIVATE-token"));
        public Task DownloadAsync(Action<int> progress, CancellationToken stop)
            => Task.FromException(new IOException("PRIVATE-mail"));
        public void ScheduleRestart() => throw new InvalidOperationException("Unexpected restart");
    }

    private sealed class DiagnosticListener : EventListener
    {
        internal List<EventWrittenEventArgs> Events { get; } = new();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "OTPinsert-Diagnostics")
            {
                EnableEvents(eventSource, EventLevel.Warning);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData) => Events.Add(eventData);
    }
}
