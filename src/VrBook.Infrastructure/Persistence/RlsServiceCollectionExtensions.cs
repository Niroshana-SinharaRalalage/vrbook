using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Infrastructure.Outbox;

namespace VrBook.Infrastructure.Persistence;

/// <summary>
/// Slice OPS.M.9 §4.3 + §4.4 Step 4 — DI helpers that wire the RLS
/// interceptor onto a module's DbContext AND register the matching
/// <see cref="IRlsBypassDbContextFactory{TContext}"/> impl.
///
/// <para>One call per module's <c>AddXxxModule</c> extension. Centralizes
/// the per-module duplication of "add interceptor + add factory + register
/// bypass".</para>
/// </summary>
public static class RlsServiceCollectionExtensions
{
    /// <summary>
    /// Register the <see cref="TenantGucCommandInterceptor"/> as a scoped
    /// service. Single registration for the whole process; per-module DI
    /// hooks share it via service-provider resolution at DbContext build time.
    /// </summary>
    public static IServiceCollection AddTenantGucInterceptor(this IServiceCollection services)
    {
        services.AddScoped<TenantGucCommandInterceptor>();
        return services;
    }

    /// <summary>
    /// Per-module helper: register the bypass factory for
    /// <typeparamref name="TContext"/>. Must run AFTER the module's
    /// <c>AddDbContextFactory&lt;TContext&gt;()</c> is configured.
    /// </summary>
    public static IServiceCollection AddRlsBypassFactory<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IRlsBypassDbContextFactory<TContext>, RlsBypassDbContextFactory<TContext>>();
        return services;
    }

    /// <summary>
    /// One-call helper for a tenant-scoped module's DbContext. Replaces the
    /// repeated 5-line <c>AddDbContext + UseNpgsql + UseOutbox</c> block in
    /// every module's <c>AddXxxModule</c>. Registers:
    /// <list type="number">
    ///   <item><see cref="TenantGucCommandInterceptor"/> as scoped (idempotent).</item>
    ///   <item><c>AddDbContext&lt;TContext&gt;</c> with Npgsql + Outbox + interceptor.</item>
    ///   <item><c>AddDbContextFactory&lt;TContext&gt;</c> with the same options
    ///         (for the bypass factory).</item>
    ///   <item><see cref="IRlsBypassDbContextFactory{TContext}"/> impl
    ///         registration.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddTenantScopedDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string schemaName)
        where TContext : DbContext
    {
        services.AddTenantGucInterceptor();
        var connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;
        void Configure(IServiceProvider sp, DbContextOptionsBuilder opts) =>
            opts.UseNpgsql(connectionString,
                    npg => npg.MigrationsHistoryTable("__ef_migrations_history", schemaName))
                .UseOutbox(sp)
                .AddInterceptors(sp.GetRequiredService<TenantGucCommandInterceptor>());
        services.AddDbContext<TContext>(Configure);
        services.AddDbContextFactory<TContext>(Configure);
        services.AddRlsBypassFactory<TContext>();
        return services;
    }
}
