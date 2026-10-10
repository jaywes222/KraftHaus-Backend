using Microsoft.AspNetCore.Authorization;

namespace Identity.Infrastructure.Authorization;

public sealed record PermissionRequirement(
    string Permission)
    : IAuthorizationRequirement;
