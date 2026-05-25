namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Indirection around <see cref="DateTimeOffset.UtcNow"/> so tests can time-travel
/// (esp. for SLA worker tests and tentative-window logic).
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today { get; }
}
