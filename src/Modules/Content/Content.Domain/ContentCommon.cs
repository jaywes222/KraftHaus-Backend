using System.Net;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;

namespace Content.Domain;

public readonly record struct ArticleId(Guid Value) : IStronglyTypedId;

public readonly record struct SiteBlockId(Guid Value) : IStronglyTypedId;

/// <summary>Permissions the Content endpoints require; must match Identity's catalog (an API test keeps them in step).</summary>
public static class ContentPermission
{
    /// <summary>Write and publish articles, edit and publish the Web Studio blocks (Tenant Admin).</summary>
    public const string Manage = "content:manage";

    public static IReadOnlyList<string> All => [Manage];
}

public static class ContentErrors
{
    public static Error Invalid(string key, IReadOnlyCollection<string> fieldErrors) =>
        new()
        {
            Key = $"Content.{key}",
            Description = "One or more fields are invalid.",
            StatusCode = HttpStatusCode.BadRequest,
            SubErrors = [.. fieldErrors]
        };

    public static Error NotFound(string key, string description) =>
        new() { Key = $"Content.{key}", Description = description, StatusCode = HttpStatusCode.NotFound };

    public static Error Conflict(string key, string description) =>
        new() { Key = $"Content.{key}", Description = description, StatusCode = HttpStatusCode.Conflict };

    public const string ConcurrentUpdateKey = "Content.ConcurrentUpdate";

    public static Error ConcurrentUpdate => Conflict("ConcurrentUpdate", "This was changed by someone else at the same moment. Refresh and try again.");

    public static Error ArticleNotFound => NotFound("ArticleNotFound", "The article was not found.");

    public static Error SlugTaken => Conflict("SlugTaken", "Another article already uses that address. Choose a different slug.");

    public static Error Archived => Conflict("Archived", "An archived article can't be changed.");

    public static Error NothingToPublish => Conflict("NothingToPublish", "There are no draft changes to publish.");
}
