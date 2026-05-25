namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Resolves feature toggles in priority order: user → property → global → built-in default.
/// Cached in Redis for 60s; cache busted on <c>FeatureToggleChanged</c> via Service Bus.
/// See proposal §11.4.
/// </summary>
public interface IFeatureToggle
{
    Task<bool> IsEnabledAsync(
        string key,
        Guid? propertyId = null,
        Guid? userId = null,
        bool defaultValue = false,
        CancellationToken ct = default);
}
