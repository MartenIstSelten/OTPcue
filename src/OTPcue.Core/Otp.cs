namespace OTPcue.Core;

public sealed record Otp(Guid Id, string Code, string Source, DateTimeOffset ReceivedAt, DateTimeOffset ExpiresAt);
