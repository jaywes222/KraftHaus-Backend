using Common.Domain.StronglyTypedIds;

namespace Common.Application.Auth;

public interface ICurrentUser
{
    ApplicationUserId Id { get; }

    string? IdAsString { get; }

    /// <summary>Realm roles from the <c>roles</c> claim.</summary>
    ICollection<string> Roles { get; }

    /// <summary>Keycloak session id (<c>sid</c> claim). Null for anonymous callers and service accounts.</summary>
    string? SessionId { get; }
}
