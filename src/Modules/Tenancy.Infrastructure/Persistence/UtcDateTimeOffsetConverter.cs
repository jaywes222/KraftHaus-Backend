using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tenancy.Infrastructure.Persistence;

internal sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    value => value.ToUniversalTime(),
    value => value.ToUniversalTime());
