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
        services.AddSingleton<IDomainEventPublisher, MediatRDomainEventPublisher>();
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
