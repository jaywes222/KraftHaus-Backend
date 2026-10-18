namespace Identity.Domain.Roles;

public enum IdentityRole
{
    SuperAdmin = 1,
    TenantAdmin = 2,
    Finance = 3,
    Support = 4,
    KraftOwner = 5,
    KraftStaff = 6,

    /// <summary>Plays the event: works the song-request queue in the DJ console. Nothing else.</summary>
    Dj = 7
}
