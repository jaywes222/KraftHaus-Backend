namespace Identity.Domain.Otp;

public enum OtpChallengeStatus
{
    Pending = 1,
    Verified = 2,
    Expired = 3,
    Locked = 4,
    Superseded = 5
}
