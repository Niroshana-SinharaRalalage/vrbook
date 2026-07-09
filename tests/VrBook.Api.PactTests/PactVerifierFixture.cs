using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Api.IntegrationTests.Multitenancy;

namespace VrBook.Api.PactTests;

/// <summary>
/// Slice OPS.1.3 — provider-side fixture for pact verification. Subclasses
/// <see cref="TwoTenantApiFixture"/> so pact tests inherit the Postgres
/// testcontainer + all-module migrations + TenantA/TenantB/OwnerA/OwnerB
/// /PlatformAdmin seed. Adds:
///
/// <list type="bullet">
///   <item>A <c>POST /pact-states</c> endpoint (via minimal APIs in
///   ConfigureWebHost) that PactNet's verifier calls BEFORE each
///   interaction with the state name. Dispatch to
///   <see cref="PactProviderStateHandler"/>.</item>
///   <item><see cref="PactProviderStateHandler"/> registered as
///   scoped so state seeds can resolve DbContexts per interaction.</item>
/// </list>
///
/// <para>The <c>PactVerifierTests</c> facts run under xUnit's collection
/// fixture pattern; one instance of this fixture per class. Fake auth
/// stays via the inherited <c>TestAuthHandler</c> overlay so admin
/// personas still satisfy the M.22.4 middleware admin-gate — a pact
/// request as "PlatformAdmin" gets the same Entra-shaped claims the
/// integration tests see.</para>
///
/// <para><b>WAF + Kestrel note (deferred):</b> PactNet's verifier calls
/// a REAL HTTP endpoint. <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{T}"/>
/// uses <see cref="TestServer"/> which is NOT bound to a real socket.
/// OPS.1.3 lands the surface (fixture + dispatch + skipped verifier
/// test with the OPS.1.5 promise). OPS.1.5 will resolve the WAF-vs-
/// Kestrel adapter — either by (a) forcing Kestrel via a Program-level
/// hook, (b) running a duplicate host on a real port, or (c) adopting
/// PactNet's HttpClient adapter path if v5 exposes one.</para>
/// </summary>
public class PactVerifierFixture : TwoTenantApiFixture
{
    /// <summary>
    /// Route the verifier POSTs to for provider-state setup. Kept as a
    /// public constant so the pact runner + fixture agree on the path.
    /// </summary>
    public const string PactStatesPath = "/pact-states";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            // Scoped so seeds resolve fresh DbContexts per interaction —
            // avoids state bleeding across the sequential replay.
            services.AddScoped<PactProviderStateHandler>();
        });
    }

    /// <summary>
    /// Registers the pact-states endpoint on the WAF's app pipeline. Called
    /// by <see cref="PactVerifierTests"/> when the host boots — kept out of
    /// <c>ConfigureWebHost</c> because minimal APIs require the
    /// <see cref="WebApplication"/>, not just the <see cref="IWebHostBuilder"/>.
    /// </summary>
    public static void MapPactStatesEndpoint(WebApplication app)
    {
        app.MapPost(PactStatesPath, async (HttpContext ctx, PactProviderStateHandler handler) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            // PactNet sends `{ "state": "..." }` or `{ "states": [...] }`
            // depending on spec version. Both shapes are handled — pick
            // whichever key is present.
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("state", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                await handler.ExecuteAsync(s.GetString()!);
            }
            else if (doc.RootElement.TryGetProperty("states", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var elem in arr.EnumerateArray())
                {
                    if (elem.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        await handler.ExecuteAsync(elem.GetString()!);
                    }
                }
            }
            return Results.Ok();
        }).AllowAnonymous();
    }
}
