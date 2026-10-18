using Common.Domain.ResultMonad;
using Communications.Application.Abstractions;
using Communications.Domain.Common;
using Communications.Domain.Templates;

namespace Communications.Application.Templates;

/// <summary>Saved messages to start broadcasts from.</summary>
public sealed class MessageTemplateService(
    IMessageTemplateRepository templates,
    ICommunicationsUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task<Result<IReadOnlyList<MessageTemplateView>>> ListAsync(CancellationToken cancellationToken)
    {
        var list = await templates.ListAsync(cancellationToken);
        return Result<IReadOnlyList<MessageTemplateView>>.Success([.. list.Select(MessageTemplateView.From)]);
    }

    public async Task<Result<MessageTemplateView>> CreateAsync(SaveTemplateRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var created = MessageTemplate.Create(new MessageTemplateId(Guid.CreateVersion7(time.GetUtcNow())), input.Name, input.Body);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        templates.Add(created.Value!);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : MessageTemplateView.From(created.Value!);
    }

    public async Task<Result<MessageTemplateView>> UpdateAsync(Guid id, SaveTemplateRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var template = await templates.FindAsync(new MessageTemplateId(id), cancellationToken);
        if (template is null || template.IsArchived)
        {
            return CommunicationsErrors.TemplateNotFound;
        }

        var updated = template.Update(input.Name, input.Body);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : MessageTemplateView.From(template);
    }

    public async Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await templates.FindAsync(new MessageTemplateId(id), cancellationToken);
        if (template is null || template.IsArchived)
        {
            return CommunicationsErrors.TemplateNotFound;
        }

        template.Archive();
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
