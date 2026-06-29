using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;

namespace VrBook.Modules.Booking.Application.Holds.Commands;

/// <summary>Slice 0.1 — 15-minute Redis-backed checkout hold per §9.3.</summary>
public sealed record CreateHoldCommand(
    Guid PropertyId,
    DateOnly Checkin,
    DateOnly Checkout,
    int Guests) : IRequest<HoldDto>;

public sealed record ReleaseHoldCommand(Guid HoldId) : IRequest;

internal sealed class CreateHoldHandler(IHoldStore holds, ICurrentUser currentUser)
    : IRequestHandler<CreateHoldCommand, HoldDto>
{
    private static readonly TimeSpan HoldTtl = TimeSpan.FromMinutes(15);

    public Task<HoldDto> Handle(CreateHoldCommand cmd, CancellationToken cancellationToken) =>
        holds.CreateAsync(
            cmd.PropertyId,
            cmd.Checkin,
            cmd.Checkout,
            cmd.Guests,
            sessionId: currentUser.UserId, // best-effort session attribution; refine when web session is real
            ttl: HoldTtl,
            ct: cancellationToken);
}

internal sealed class ReleaseHoldHandler(IHoldStore holds, ICurrentUser currentUser)
    : IRequestHandler<ReleaseHoldCommand>
{
    // Slice OPS.M.10.2 F9 (audit #22) — pass the caller's UserId as the
    // expected hold owner. The hold store no-ops on mismatch, defending
    // against a guess of another guest's HoldId.
    public Task Handle(ReleaseHoldCommand cmd, CancellationToken cancellationToken) =>
        holds.ReleaseAsync(cmd.HoldId, currentUser.UserId, cancellationToken);
}
