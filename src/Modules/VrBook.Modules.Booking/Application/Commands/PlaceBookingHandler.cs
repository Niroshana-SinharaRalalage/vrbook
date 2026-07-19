using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking.Application.Common;
using VrBook.Modules.Booking.Domain;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Application.Properties.Queries;
using VrBook.Modules.Payment.Application.Commands;
using VrBook.Modules.Pricing.Application.Quotes.Commands;
using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Modules.Booking.Application.Commands;

internal sealed class PlaceBookingHandler(
    ICurrentUser currentUser,
    IGuestTenantResolver guestTenant,
    IMediator mediator,
    IBookingRepository bookings,
    IHoldStore holds,
    IPropertyOwnerLookup propertyOwners,
    BookingDbContext db,
    IOptions<BookingSlaOptions> slaOptions,
    // VRB-102 Phase B — resolved as IEnumerable so the handler stays DI-safe before
    // Agent 2's ICancellationPolicyResolver / ICancellationTierProvider impls register:
    // empty ⇒ no snapshot stamped (bookings keep null policy → refund falls back to flat).
    IEnumerable<ICancellationPolicyResolver> policyResolvers,
    IEnumerable<ICancellationTierProvider> tierProviders,
    Microsoft.Extensions.Logging.ILogger<PlaceBookingHandler> logger) : IRequestHandler<PlaceBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(PlaceBookingCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required to place a booking.");
        }
        var r = request.Request ?? throw new ArgumentException("Request body required.", nameof(request));
        if (!r.AgreedToHouseRules)
        {
            throw new BusinessRuleViolationException(
                "booking.house_rules",
                "You must agree to the property's house rules before booking.");
        }

        // Slice OPS.M.9.1 F6d — closes audit #11 (Place sub-path). Guests
        // have no ICurrentUser.TenantId; without a tenant scope, M.9 RLS
        // denies the GetPropertyByIdQuery handler's read (covered by the
        // F6b carve-out for active properties — but Place ALSO touches
        // booking.bookings + availability_blocks via the FOR UPDATE
        // probes + the INSERT). Resolve tenant from property id and open
        // a BackgroundTenantScope around the ENTIRE handler so the
        // serializable transaction's SET LOCAL fires per-statement with
        // the right tenant.
        //
        // Per OPS.M.9.1 §6.1 risk #4: per-statement GUC binding inside
        // an explicit transaction works correctly. The using scope opens
        // BEFORE BeginTransactionAsync so every command in the txn
        // (including the FOR UPDATE probes + INSERT + cross-module
        // CreatePaymentIntentForBookingCommand at the end) gets the
        // stamp.
        var tenantId = await guestTenant.ResolveFromPropertyIdAsync(r.PropertyId, cancellationToken)
            ?? throw new NotFoundException("Property", r.PropertyId);
        using var tenantScope = BackgroundTenantScope.Enter(tenantId);

        // Cross-module read into Catalog for property basics.
        var property = await mediator.Send(new GetPropertyByIdQuery(r.PropertyId), cancellationToken)
            ?? throw new NotFoundException("Property", r.PropertyId);
        if (!property.IsActive)
        {
            throw new BusinessRuleViolationException(
                "booking.property_inactive",
                "This property is not currently accepting bookings.");
        }
        if (property.OwnerUserId == currentUser.UserId.Value)
        {
            throw new BusinessRuleViolationException(
                "booking.self",
                "You can't book your own property.");
        }

        // Compute the quote via Pricing (in-process MediatR; not Service Bus).
        // Quote computation is read-only so we keep it OUTSIDE the serializable txn
        // to keep the locked window as short as possible.
        var quoteReq = new QuoteRequest(r.CheckinDate, r.CheckoutDate, r.GuestCount, r.ApplyLoyaltyDiscount);
        var quote = await mediator.Send(new ComputeQuoteCommand(r.PropertyId, quoteReq), cancellationToken);

        var stay = new Stay(r.CheckinDate, r.CheckoutDate);
        var guestName = currentUser.Email ?? currentUser.ExternalObjectId ?? "Guest";

        var lineItems = new List<(string kind, string label, int qty, decimal unit, decimal lineTotal)>();
        foreach (var n in quote.Nightly)
        {
            lineItems.Add(("Nightly", $"Night of {n.Date:yyyy-MM-dd}", 1, n.Amount.Amount, n.Amount.Amount));
        }
        foreach (var f in quote.Fees)
        {
            lineItems.Add((f.Kind.ToString(), f.Label, 1, f.Amount.Amount, f.Amount.Amount));
        }

        // OPS.M.3 — booking inherits tenancy from the property (guests are
        // tenant-less per MTOP §1). PropertyDto doesn't carry TenantId; look
        // up via the cross-module IPropertyOwnerLookup contract.
        var ownerSnapshot = await propertyOwners.GetAsync(property.Id, cancellationToken);
        var bookingTenantId = ownerSnapshot!.TenantId;
        var booking = DomainBooking.Place(
            tenantId: bookingTenantId,
            propertyId: property.Id,
            propertyTitle: property.Title,
            guestUserId: currentUser.UserId.Value,
            guestDisplayName: guestName,
            stay: stay,
            guestCount: r.GuestCount,
            currency: quote.Total.Currency,
            subtotal: quote.Subtotal.Amount,
            fees: quote.Fees.Sum(x => x.Amount.Amount),
            taxes: quote.Taxes.Amount,
            total: quote.Total.Amount,
            lineItems: lineItems,
            guests: (r.Guests ?? Array.Empty<BookingGuestDto>())
                .Select(g => (g.FullName, g.IsPrimary)),
            specialRequests: r.SpecialRequests,
            tentativeSla: slaOptions.Value.TentativeSla); // VRB-207 (G2) — 48h locked, config-driven

        // VRB-102 Phase B — stamp the effective cancellation policy onto the booking
        // so later global-tier / per-property changes never alter this in-flight
        // booking. Inert until Agent 2's resolver registers (see ctor). For the
        // RefundableUpgrade model the concrete price is computed here at Place from
        // the global UpgradePricePct × subtotal (the guest opt-in wires with checkout).
        var policyResolver = policyResolvers.FirstOrDefault();
        if (policyResolver is not null)
        {
            var snapshot = await policyResolver.ResolveAsync(property.Id, bookingTenantId, cancellationToken);
            if (snapshot.Model == CancellationModel.RefundableUpgrade)
            {
                decimal? upgradePrice = null;
                if (tierProviders.FirstOrDefault() is { } tp)
                {
                    var active = await tp.GetActiveAsync(cancellationToken);
                    upgradePrice = decimal.Round(
                        quote.Subtotal.Amount * active.UpgradePricePct / 100m, 2, MidpointRounding.AwayFromZero);
                }
                booking.StampCancellationPolicy(
                    snapshot.Model, null, null, null, null, null,
                    refundableUpgradePurchased: false, // guest opt-in lands with the checkout UI
                    refundableUpgradePriceAmount: upgradePrice,
                    refundableUpgradePriceCurrency: quote.Total.Currency);
            }
            else
            {
                booking.StampCancellationPolicy(
                    snapshot.Model,
                    snapshot.FirstTierDays, snapshot.SecondTierDays, snapshot.MiddleTierRefundPct,
                    snapshot.FinalCutoffHours, snapshot.TierVersion,
                    refundableUpgradePurchased: false, refundableUpgradePriceAmount: null,
                    refundableUpgradePriceCurrency: null);
            }
        }

        // Slice 0.2: serializable transaction + SELECT FOR UPDATE row lock + hold
        // consumption all in one atomic step. Closes the race per proposal §7.3.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            // (1) Consume the Redis hold. If the hold has expired or someone else
            //     consumed it, fail before doing any DB work.
            var consumed = await holds.TryConsumeAsync(r.HoldId, property.Id, r.CheckinDate, r.CheckoutDate, cancellationToken);
            if (!consumed)
            {
                throw new ConflictException(
                    "Your hold has expired or is invalid. Please restart the booking and try again.");
            }

            // (2) SELECT id ... FOR UPDATE — row lock on any existing overlapping bookings.
            //     Combined with the serializable isolation level, this closes both the
            //     "modify-existing concurrent" race and the "insert-insert gap" race.
            //     BookingConfiguration maps Status with HasConversion<string>() so the
            //     column is character varying — compare to the enum NAME, not the int
            //     value (Postgres 42883 otherwise).
            //     Also: SELECT COUNT(*) ... FOR UPDATE is rejected (0A000); select the
            //     ids and check HasRows. "Id" is quoted because EF preserves PascalCase
            //     for the PK (Postgres folds unquoted identifiers to lowercase, 42703).
            // Slice OPS.M.16 — same conditional-on-CheckedOut turnover-day
            // block as BookingRepository.FindOverlapsAsync. This SQL path is
            // the race-safe primary check (serializable-tx + FOR UPDATE);
            // the repository query is the async duplicate. Both must agree.
            const string overlapSql = """
                SELECT "Id" FROM booking.bookings
                WHERE property_id = @p0
                  AND status NOT IN ('Cancelled', 'Rejected', 'Refunded')
                  AND deleted_at IS NULL
                  AND checkin_date < @p2
                  AND (
                        (status = 'CheckedOut' AND @p1 <= checkout_date)
                     OR (status <> 'CheckedOut' AND @p1 < checkout_date)
                  )
                FOR UPDATE
                """;
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            cmd.CommandText = overlapSql;
            AddParam(cmd, "@p0", property.Id);
            AddParam(cmd, "@p1", r.CheckinDate);
            AddParam(cmd, "@p2", r.CheckoutDate);
            bool anyOverlap;
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                anyOverlap = await reader.ReadAsync(cancellationToken);
            }
            if (anyOverlap)
            {
                throw new BusinessRuleViolationException(
                    "booking.dates_unavailable",
                    "These dates are already booked. Please choose different dates.");
            }

            // (2b) Slice 3: same FOR UPDATE check against owner-created blocks.
            //      Locking in the same serializable txn closes the race where a block
            //      is inserted concurrently with a booking on the same dates.
            const string blockSql = """
                SELECT "Id" FROM booking.availability_blocks
                WHERE property_id = @p0
                  AND deleted_at IS NULL
                  AND start_date < @p2
                  AND @p1 < end_date
                FOR UPDATE
                """;
            await using var blockCmd = db.Database.GetDbConnection().CreateCommand();
            blockCmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            blockCmd.CommandText = blockSql;
            AddParam(blockCmd, "@p0", property.Id);
            AddParam(blockCmd, "@p1", r.CheckinDate);
            AddParam(blockCmd, "@p2", r.CheckoutDate);
            bool anyBlock;
            await using (var blockReader = await blockCmd.ExecuteReaderAsync(cancellationToken))
            {
                anyBlock = await blockReader.ReadAsync(cancellationToken);
            }
            if (anyBlock)
            {
                throw new BusinessRuleViolationException(
                    "booking.dates_blocked",
                    "These dates are blocked by the host. Please choose different dates.");
            }

            // (3) Insert booking inside the locked txn.
            await bookings.AddAsync(booking, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg && pg.SqlState == "40001")
        {
            // Serialization failure — another transaction committed an overlapping
            // booking between our SELECT FOR UPDATE and our COMMIT. Map to 409 with
            // a guest-friendly message.
            throw new ConflictException(
                "Another guest just booked these dates. Please choose different dates and try again.");
        }

        // Create the Stripe PaymentIntent OUTSIDE the booking transaction. Manual
        // capture (default in StripeGateway): we authorize now, capture on Confirm,
        // cancel on Reject. If the Stripe call fails the booking is in Tentative
        // with no payment intent; guest can complete from /bookings/[id].
        //
        // Slice OPS.M.10.2 F11.7.2 — actually honor that comment. Pre-fix
        // any Stripe error (e.g. tenant.StripeAccountId is a staging stub
        // that Stripe doesn't recognize, sandbox flake, account capability
        // not yet granted) propagated up as a 500 and ruined the booking
        // confirmation UX even though the booking itself is durably in
        // Tentative. Catch any non-cancellation exception, log, return
        // the Tentative DTO — the guest can retry payment from the
        // detail page.
        try
        {
            await mediator.Send(
                new CreatePaymentIntentForBookingCommand(
                    booking.Id,
                    new Money(booking.Total, booking.Currency),
                    booking.TenantId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Honor the cancellation token — caller is bailing out.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "PaymentIntent creation failed for booking {BookingId} (tenant {TenantId}); leaving booking in Tentative without a PI. Guest can retry from /bookings/<id>.",
                booking.Id, booking.TenantId);
        }

        return booking.ToDto();
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
