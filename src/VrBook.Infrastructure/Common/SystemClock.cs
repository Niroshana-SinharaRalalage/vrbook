using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Common;

public sealed class SystemClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
