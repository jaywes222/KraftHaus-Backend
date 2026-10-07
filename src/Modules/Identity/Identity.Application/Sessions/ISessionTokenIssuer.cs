using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;

namespace Identity.Application.Sessions;

public interface ISessionTokenIssuer
{
    Task<string> IssueAccessTokenAsync(
        ApplicationUserId userId,
        AuthSessionId sessionId,
        CancellationToken cancellationToken);
}
