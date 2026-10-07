namespace Identity.Domain.Sessions;

public enum SessionRevocationReason
{
    SignedOut = 1,
    ReuseDetected = 2,
    AdminRevoked = 3,
    UserDisabled = 4
}
