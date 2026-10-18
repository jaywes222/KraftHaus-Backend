namespace Common.Application.Ticketing;

/// <summary>Event dates a phone actually attended (checked in). Implemented by Ticketing; Feedback uses it (UF-010).</summary>
public interface IAttendance
{
    /// <param name="phone">E.164.</param>
    Task<IReadOnlyList<AttendedDate>> ListAttendedAsync(string phone, Guid eventId, CancellationToken cancellationToken);
}

public sealed record AttendedDate(Guid TicketId, Guid EventDateId, DateTimeOffset StartsAt, DateTimeOffset CheckedInAt);
