using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Outbox;

/// <summary>
/// Wires up A0.3 — domain event publisher + the EF SaveChanges interceptor that
/// writes outbox rows. Call <see cref="AddOutbox"/> once on the root service
/// collection, then add <see cref="UseOutbox"/> in every module's DbContext
/// options builder.
/// </summary>
public static class OutboxRegistration
{
    public static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        // Scoped (not Singleton) so MediatR resolves INotificationHandler<T>
        // instances from the same DI scope as the calling DbContext/handler.
        // A singleton publisher would force MediatR to create a fresh scope per
        // Publish call, which gives the handler a different BookingDbContext
        // whose ChangeTracker is empty — so cross-module lookups in the
        // notification handlers (e.g. IBookingEmailLookup checking Local for
        // an in-transaction booking) silently return null and queue blank
        // emails.
        services.AddScoped<IDomainEventPublisher, MediatRDomainEventPublisher>();
        // Scoped so each DbContext instance gets its own interceptor with its own
        // per-SaveChanges-call state buffer.
        services.AddScoped<DomainEventOutboxInterceptor>();
        return services;
    }

    /// <summary>
    /// Adds the outbox interceptor to a DbContext options builder. Must be called
    /// alongside <c>UseNpgsql</c> inside every module's <c>AddDbContext</c>.
    /// Usage:
    /// <code>
    /// services.AddDbContext&lt;BookingDbContext&gt;((sp, opts) =&gt;
    ///     opts.UseNpgsql(connStr).UseOutbox(sp));
    /// </code>
    /// </summary>
    public static DbContextOptionsBuilder UseOutbox(
        this DbContextOptionsBuilder builder, IServiceProvider serviceProvider)
    {
        builder.AddInterceptors(serviceProvider.GetRequiredService<DomainEventOutboxInterceptor>());
        return builder;
    }
}
