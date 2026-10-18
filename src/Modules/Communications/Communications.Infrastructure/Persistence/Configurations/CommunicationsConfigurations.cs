using Communications.Domain.Broadcasts;
using Communications.Domain.Common;
using Communications.Domain.Messages;
using Communications.Domain.OptOuts;
using Communications.Domain.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Communications.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("message");
        CommunicationsEf.ConfigureKey<Message, MessageId>(builder);

        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.BroadcastId).HasConversion(new ValueConverter<BroadcastId?, Guid?>(
            id => id.HasValue ? id.Value.Value : null,
            value => value.HasValue ? new BroadcastId(value.Value) : null));
        builder.Property(x => x.Recipient).HasMaxLength(Message.RecipientMaxLength).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(Message.SubjectMaxLength);
        builder.Property(x => x.Body).HasMaxLength(MessageBody.StoredMaxLength).IsRequired();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(Message.ProviderIdMaxLength);
        builder.Property(x => x.ProviderStatus).HasMaxLength(Message.ProviderStatusMaxLength);
        builder.Property(x => x.Error).HasMaxLength(Message.ErrorMaxLength);
        builder.Property(x => x.QueuedAt).IsRequired();

        builder.HasIndex(x => x.QueuedAt).IsDescending();
        builder.HasIndex(x => new { x.BroadcastId, x.Status, x.QueuedAt });
        builder.HasIndex(x => new { x.Recipient, x.QueuedAt }).IsDescending(false, true);
        builder.HasIndex(x => x.ProviderMessageId).HasFilter("\"ProviderMessageId\" IS NOT NULL");

        CommunicationsEf.ConfigureAudit(builder);
    }
}

public sealed class BroadcastConfiguration : IEntityTypeConfiguration<Broadcast>
{
    public void Configure(EntityTypeBuilder<Broadcast> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("broadcast");
        builder.Ignore(x => x.Events);
        builder.Ignore(x => x.IsMarketing);
        CommunicationsEf.ConfigureKey<Broadcast, BroadcastId>(builder);

        builder.Property(x => x.Name).HasMaxLength(Broadcast.NameMaxLength).IsRequired();
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Audience).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(Broadcast.BodyMaxLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasIndex(x => new { x.Status, x.ScheduledFor });
        builder.HasIndex(x => new { x.EventId, x.CreatedOn });

        CommunicationsEf.ConfigureAudit(builder);
        CommunicationsEf.ConfigureAggregateVersion(builder);
    }
}

public sealed class OptOutConfiguration : IEntityTypeConfiguration<OptOut>
{
    public void Configure(EntityTypeBuilder<OptOut> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("opt_out");
        builder.Ignore(x => x.Events);
        CommunicationsEf.ConfigureKey<OptOut, OptOutId>(builder);

        builder.Property(x => x.Phone).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.OptedOutAt).IsRequired();

        builder.HasIndex(x => x.Phone).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.OptedOutAt });

        CommunicationsEf.ConfigureAudit(builder);
        CommunicationsEf.ConfigureAggregateVersion(builder);
    }
}

public sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("message_template");
        builder.Ignore(x => x.Events);
        CommunicationsEf.ConfigureKey<MessageTemplate, MessageTemplateId>(builder);

        builder.Property(x => x.Name).HasMaxLength(MessageTemplate.NameMaxLength).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(Broadcast.BodyMaxLength).IsRequired();

        CommunicationsEf.ConfigureAudit(builder);
        CommunicationsEf.ConfigureAggregateVersion(builder);
    }
}
