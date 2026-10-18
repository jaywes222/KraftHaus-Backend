using Common.IntegrationEvents;
using Microsoft.EntityFrameworkCore;

namespace Common.Infrastructure.Messaging;

public static class MessagingModelBuilderExtensions
{
    /// <summary>Maps <c>outbox_message</c> into the module's default schema.</summary>
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable(OutboxMessage.TableName);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Type).HasMaxLength(OutboxMessage.TypeMaxLength).IsRequired();
            builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.TraceParent).HasMaxLength(128);
            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.AvailableAt).IsRequired();
            builder.Property(x => x.Attempts).IsRequired();
            builder.Property(x => x.LastError).HasMaxLength(OutboxMessage.ErrorMaxLength);

            builder.HasIndex(x => x.AvailableAt)
                .HasDatabaseName("ix_outbox_message_pending")
                .HasFilter("\"ProcessedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");
        });

        return modelBuilder;
    }

    /// <summary>Maps <c>processed_message</c> (consumer idempotency) into the module's default schema.</summary>
    public static ModelBuilder AddProcessedMessages(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ProcessedMessage>(builder =>
        {
            builder.ToTable(ProcessedMessage.TableName);
            builder.HasKey(x => new { x.MessageId, x.Handler });
            builder.Property(x => x.Handler).HasMaxLength(ProcessedMessage.HandlerMaxLength);
            builder.Property(x => x.ProcessedAt).IsRequired();
        });

        return modelBuilder;
    }

    /// <summary>Queues <paramref name="event" /> in this context's outbox; it is written by the next <c>SaveChanges</c>.</summary>
    public static void AddIntegrationEvent(this DbContext db, IntegrationEvent @event, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);

        db.Set<OutboxMessage>().Add(OutboxMessage.From(@event, now));
    }
}
