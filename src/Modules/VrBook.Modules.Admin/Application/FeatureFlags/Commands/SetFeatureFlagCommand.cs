using FluentValidation;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using VrBook.Application.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Admin.Application.FeatureFlags.Commands;

/// <summary>
/// VRB-203 — set a global feature-flag override (PlatformAdmin only). Upserts the
/// <c>admin.feature_flags</c> row and invalidates the resolver cache so the change
/// takes effect without a redeploy. Platform-scoped (not <c>ITenantScoped</c>) — the
/// controller's <c>[Authorize(Roles="PlatformAdmin")]</c> is the gate.
/// </summary>
public sealed record SetFeatureFlagCommand(string Key, string Scope, bool Enabled)
    : IRequest<FeatureToggleDto>, IAuditable
{
    public string AuditAction => "feature-toggle.set";
    public string? AuditTargetType => "FeatureFlag";
    public string? AuditTargetId => Key;
}

internal sealed class SetFeatureFlagValidator : AbstractValidator<SetFeatureFlagCommand>
{
    public SetFeatureFlagValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .Must(k => k.StartsWith(FeatureFlagKeys.Section + ":", StringComparison.Ordinal))
            .WithMessage("Flag key must follow the Features:<Area>.<Capability> convention.");
        RuleFor(x => x.Scope)
            .Must(s => string.Equals(s, "global", StringComparison.Ordinal))
            .WithMessage("VRB-203 supports global flags only; scope must be 'global'.");
    }
}

internal sealed class SetFeatureFlagHandler : IRequestHandler<SetFeatureFlagCommand, FeatureToggleDto>
{
    private readonly IFeatureFlagStore _store;
    private readonly IMemoryCache _cache;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SetFeatureFlagHandler(
        IFeatureFlagStore store, IMemoryCache cache, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _store = store;
        _cache = cache;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<FeatureToggleDto> Handle(SetFeatureFlagCommand request, CancellationToken cancellationToken)
    {
        await _store.SetOverrideAsync(
            request.Key, request.Enabled, _currentUser.UserId ?? Guid.Empty, _clock.UtcNow, cancellationToken);

        _cache.Remove(FeatureFlagKeys.CacheKey(request.Key));

        return new FeatureToggleDto(request.Key, "global", null, request.Enabled);
    }
}
