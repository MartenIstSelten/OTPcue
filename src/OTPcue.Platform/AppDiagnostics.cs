using System.Diagnostics.Tracing;

namespace OTPcue.Platform;

// Opt-in EventSource diagnostics. Never pass messages, stack traces, account data,
// mail contents or credentials: provider exceptions can contain all of these.
[EventSource(Name = "OTPcue-Diagnostics")]
internal sealed class AppDiagnostics : EventSource
{
    internal static AppDiagnostics Log { get; } = new();

    private AppDiagnostics()
    {
    }

    [NonEvent]
    internal static void ReportFailure(string operation, Exception exception)
    {
        if (Log.IsEnabled())
        {
            Log.Failure(operation, exception.GetType().Name, exception.HResult);
        }
    }

    [Event(1, Level = EventLevel.Warning)]
    public void Failure(string operation, string exceptionType, int errorCode)
        => WriteEvent(1, operation, exceptionType, errorCode);
}
