using Common.Domain.Aggregates;
using Common.Domain.Events;
using Common.Domain.ResultMonad;
using Communications.Domain.Broadcasts;
using Communications.Domain.Common;

namespace Communications.Domain.Templates;

/// <summary>A saved message staff start broadcasts from ("Gates open", "Sales open"). Archived, never deleted.</summary>
public sealed class MessageTemplate : AggregateRoot<MessageTemplateId>
{
    public const int NameMaxLength = 80;

    private MessageTemplate(MessageTemplateId id)
        : base(id)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public bool IsArchived { get; private set; }

    public static Result<MessageTemplate> Create(MessageTemplateId id, string? name, string? body)
    {
        var valid = Validate(name, body);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var template = new MessageTemplate(id) { Name = valid.Value.Name, Body = valid.Value.Body };
        template.RaiseEvent(new MessageTemplateSaved(id));
        return template;
    }

    public Result Update(string? name, string? body)
    {
        var valid = Validate(name, body);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        if (valid.Value.Name == Name && valid.Value.Body == Body)
        {
            return Result.Success;
        }

        var @event = new MessageTemplateSaved(Id);

        Name = valid.Value.Name;
        Body = valid.Value.Body;

        RaiseEvent(@event);
        return Result.Success;
    }

    public void Archive()
    {
        if (IsArchived)
        {
            return;
        }

        var @event = new MessageTemplateArchived(Id);
        IsArchived = true;
        RaiseEvent(@event);
    }

    private static Result<(string Name, string Body)> Validate(string? name, string? body)
    {
        var errors = new List<string>();
        var trimmedName = name?.Trim() ?? string.Empty;
        var trimmedBody = body?.Trim() ?? string.Empty;

        if (trimmedName.Length is 0 or > NameMaxLength)
        {
            errors.Add($"Name: Give it a name of up to {NameMaxLength} characters.");
        }

        if (trimmedBody.Length is 0 or > Broadcast.BodyMaxLength)
        {
            errors.Add($"Body: Write the message, up to {Broadcast.BodyMaxLength} characters.");
        }

        return errors.Count > 0
            ? CommunicationsErrors.Invalid("TemplateInvalid", errors)
            : (trimmedName, trimmedBody);
    }
}

public sealed record MessageTemplateSaved(MessageTemplateId TemplateId) : DomainEvent;

public sealed record MessageTemplateArchived(MessageTemplateId TemplateId) : DomainEvent;
