using OTPcue.Core;
using Xunit;

public sealed class OtpStoreTests
{
    [Fact]
    public void DuplicateDeliveryKeepsOriginalIdentityAndExpiry()
    {
        var clock = new FakeClock();
        var store = new OtpStore(clock);
        store.Add("123456", "Demo", clock.Now);
        var original = Assert.IsType<Otp>(store.Latest());
        clock.Now = clock.Now.AddSeconds(3);
        store.Add("123456", "demo", clock.Now);
        Assert.Equal(original, store.Latest());
    }

    [Fact]
    public void CodeExpiresAtExactCountdownBoundary()
    {
        var clock = new FakeClock();
        var store = new OtpStore(clock);
        store.Add("123456", "Demo", clock.Now);
        var original = Assert.IsType<Otp>(store.Latest());
        Assert.Equal(OtpStore.Lifetime, original.ExpiresAt - clock.Now);
        clock.Now = original.ExpiresAt.AddTicks(-1);
        Assert.True(store.Contains(original.Id));
        clock.Now = original.ExpiresAt;
        Assert.Null(store.Latest());
        Assert.False(store.Contains(original.Id));
        Assert.Null(store.RetryLatest());
    }

    [Fact]
    public void OnlyExplicitRetryReactivatesConsumedCodeWithOriginalExpiry()
    {
        var clock = new FakeClock();
        var store = new OtpStore(clock);
        store.Add("111111", "Demo", clock.Now);
        var original = Assert.IsType<Otp>(store.Latest());
        store.Remove(original.Id);
        store.Add("111111", "Demo", clock.Now);
        Assert.Null(store.Latest());
        Assert.False(store.Contains(original.Id));
        clock.Now = clock.Now.AddSeconds(10);
        Assert.Equal(original, store.RetryLatest());
        clock.Now = original.ExpiresAt;
        Assert.Null(store.RetryLatest());
    }

    [Fact]
    public void ConsumingNewestCodeCannotFallBackToAnOlderCode()
    {
        var store = new OtpStore();
        store.Add("111111", "First", DateTimeOffset.UtcNow);
        store.Add("222222", "Second", DateTimeOffset.UtcNow);
        store.Remove(Assert.IsType<Otp>(store.Latest()).Id);
        Assert.Null(store.Latest());
    }

    [Fact]
    public void CountdownChangesAffectOnlyFutureCodes()
    {
        var clock = new FakeClock();
        var store = new OtpStore(clock) { Countdown = TimeSpan.FromSeconds(120) };
        store.Add("333333", "Settings", clock.Now);
        var original = Assert.IsType<Otp>(store.Latest());
        Assert.Equal(TimeSpan.FromSeconds(120), original.ExpiresAt - clock.Now);
        store.Countdown = TimeSpan.FromSeconds(30);
        Assert.Equal(original, store.Latest());
        store.Add("444444", "Settings", clock.Now);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.IsType<Otp>(store.Latest()).ExpiresAt - clock.Now);
    }

    [Fact]
    public void DiscardChecksIdentityAndPermanentlyRemovesCode()
    {
        var store = new OtpStore();
        store.Add("123456", "Demo", DateTimeOffset.UtcNow);
        var original = Assert.IsType<Otp>(store.Latest());
        store.Discard(Guid.NewGuid());
        Assert.True(store.Contains(original.Id));
        store.Discard(original.Id);
        Assert.Null(store.RetryLatest());
        store.Add("654321", "Demo", DateTimeOffset.UtcNow);
        store.Clear();
        Assert.Null(store.RetryLatest());
    }
}

internal sealed class FakeClock : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
