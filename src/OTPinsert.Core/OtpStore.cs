namespace OTPinsert.Core;

public sealed class OtpStore
{
    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(1);

    private readonly object gate = new();
    private readonly TimeProvider time;
    private Otp? current;
    private TimeSpan lifetime = Lifetime;
    private bool consumed;

    public OtpStore(TimeProvider? time = null) => this.time = time ?? TimeProvider.System;

    public event Action? CodeAdded;

    public TimeSpan Countdown
    {
        get
        {
            lock (gate)
            {
                return lifetime;
            }
        }
        set
        {
            if (value < TimeSpan.FromSeconds(15) || value > TimeSpan.FromMinutes(10))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            lock (gate)
            {
                lifetime = value;
            }
        }
    }

    public void Add(string code, string source, DateTimeOffset receivedAt)
    {
        lock (gate)
        {
            Prune();
            // Polling or duplicate deliveries must not restart the same code's lifetime.
            if (current is not null && current.Code == code &&
                string.Equals(current.Source, source, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = new(Guid.NewGuid(), code, source, receivedAt, time.GetUtcNow() + lifetime);
            consumed = false;
        }
        // Notify outside the lock so subscribers cannot block access to the store.
        CodeAdded?.Invoke();
    }

    public Otp? Latest()
    {
        lock (gate)
        {
            Prune();
            return consumed ? null : current;
        }
    }

    public bool Contains(Guid id)
    {
        lock (gate)
        {
            Prune();
            return !consumed && current?.Id == id;
        }
    }

    public void Discard(Guid id)
    {
        lock (gate)
        {
            if (current?.Id == id)
            {
                ClearCurrent();
            }
        }
    }

    public void Remove(Guid id)
    {
        lock (gate)
        {
            if (current?.Id == id)
            {
                consumed = true;
            }
        }
    }

    // Only explicit input interaction can reactivate a used code, with its original expiry.
    public Otp? RetryLatest()
    {
        lock (gate)
        {
            Prune();
            consumed = false;
            return current;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            ClearCurrent();
        }
    }

    // Both helpers require the caller to hold gate.
    private void Prune()
    {
        if (current?.ExpiresAt <= time.GetUtcNow())
        {
            ClearCurrent();
        }
    }

    private void ClearCurrent()
    {
        current = null;
        consumed = false;
    }
}
