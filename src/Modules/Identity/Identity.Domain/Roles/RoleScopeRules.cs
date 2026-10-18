using Common.Domain.ResultMonad;

namespace Identity.Domain.Roles;

public static class RoleScopeRules
{
    public static Result Validate(
        IdentityRole role,
        RoleScopeType scopeType,
        Guid? scopeId)
    {
        var valid = role switch
        {
            IdentityRole.SuperAdmin =>
                scopeType == RoleScopeType.Platform &&
                scopeId is null,

            IdentityRole.TenantAdmin or
            IdentityRole.Finance or
            IdentityRole.Support or
            IdentityRole.Dj =>
                scopeType == RoleScopeType.Tenant &&
                scopeId.HasValue,

            IdentityRole.KraftOwner or
            IdentityRole.KraftStaff =>
                scopeType == RoleScopeType.Kraft &&
                scopeId.HasValue,

            _ => false
        };

        return !valid
            ? (Result)new Error
            {
                Key = "Identity.RoleScopeInvalid",
                Description =
                    $"Role '{role}' cannot be assigned at scope '{scopeType}'."
            }
            : Result.Success;
    }
}
