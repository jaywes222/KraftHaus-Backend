using System.Text.Json;
using Common.Domain.Aggregates;
using Common.Domain.Events;
using Common.Domain.ResultMonad;

namespace Content.Domain;

/// <summary>The editable parts of the public site in the Web Studio (UF-024). The client decides each block's fields.</summary>
public enum SiteBlockKey
{
    /// <summary>Landing headline, sub-headline, call to action.</summary>
    Hero = 1,

    /// <summary>A banner across the top ("Tickets go on sale Monday").</summary>
    Announcement = 2,

    About = 3,

    Faq = 4,

    /// <summary>Instagram, TikTok, X, WhatsApp links.</summary>
    Socials = 5,

    Footer = 6,

    /// <summary>Page title, description and share image for search engines and link previews.</summary>
    Seo = 7
}

/// <summary>
///     One Web Studio block: staff edit the draft (any JSON object, up to <see cref="MaxContentLength" /> characters)
///     and publish it; visitors only ever see the published copy.
/// </summary>
public sealed class SiteBlock : AggregateRoot<SiteBlockId>
{
    public const int MaxContentLength = 20_000;

    private SiteBlock(SiteBlockId id)
        : base(id)
    {
    }

    public SiteBlockKey Key { get; private set; }

    /// <summary>JSON object being edited.</summary>
    public string Draft { get; private set; } = "{}";

    /// <summary>JSON object visitors see; <c>null</c> until first published.</summary>
    public string? Published { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public bool HasUnpublishedChanges => Draft != Published;

    public static SiteBlock Create(SiteBlockId id, SiteBlockKey key) => new(id) { Key = key };

    public Result SaveDraft(string? json)
    {
        var normalised = Normalise(json);
        if (normalised.IsFailure)
        {
            return normalised.Error!;
        }

        if (normalised.Value == Draft)
        {
            return Result.Success;
        }

        var @event = new SiteBlockDraftSaved(Id, Key);
        Draft = normalised.Value!;
        RaiseEvent(@event);
        return Result.Success;
    }

    public Result Publish(DateTimeOffset now)
    {
        if (!HasUnpublishedChanges)
        {
            return ContentErrors.NothingToPublish;
        }

        var @event = new SiteBlockPublished(Id, Key);
        Published = Draft;
        PublishedAt = now;
        RaiseEvent(@event);
        return Result.Success;
    }

    /// <summary>A JSON object, compacted. Anything else (arrays, strings, invalid JSON, too long) is refused.</summary>
    public static Result<string> Normalise(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxContentLength * 2)
        {
            return ContentErrors.Invalid("BlockInvalid", ["Content: Send the block as a JSON object."]);
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ContentErrors.Invalid("BlockInvalid", ["Content: Send the block as a JSON object."]);
            }

            var compact = JsonSerializer.Serialize(document.RootElement);
            return compact.Length <= MaxContentLength
                ? compact
                : ContentErrors.Invalid("BlockInvalid", [$"Content: Keep the block under {MaxContentLength} characters."]);
        }
        catch (JsonException)
        {
            return ContentErrors.Invalid("BlockInvalid", ["Content: That isn't valid JSON."]);
        }
    }
}

public sealed record SiteBlockDraftSaved(SiteBlockId BlockId, SiteBlockKey Key) : DomainEvent;

public sealed record SiteBlockPublished(SiteBlockId BlockId, SiteBlockKey Key) : DomainEvent;
