using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace VrBook.Api.PactTests;

/// <summary>
/// Slice OPS.1.3 — provider-side verification of the pact file at
/// <c>contracts/pacts/vrbook-web-vrbook-api.json</c>. Replays each
/// interaction against a <see cref="PactVerifierFixture"/> host with the
/// M.14 <c>TestAuthHandler</c> overlay so admin / owner / guest personas
/// materialise identically to integration tests.
///
/// <para><b>Current state (OPS.1.3):</b> the sole fact is
/// <see cref="VerifyPacts"/> — skipped pending the WAF-vs-Kestrel adapter
/// investigation. PactNet's verifier requires a real HTTP endpoint;
/// <c>WebApplicationFactory</c> uses <c>TestServer</c> which is in-process
/// only. OPS.1.5 will resolve the wiring (either by binding Kestrel to a
/// fixed port inside the fixture, running a duplicate host, or adopting
/// PactNet's HttpClient adapter if v5 exposes one).</para>
///
/// <para>Category=Pact so this fact never runs under the standard
/// <c>Category!=Integration</c> CI filter. The <c>pact-verifier</c>
/// dedicated CI step (added to <c>cd-staging-api.yml</c> in this commit,
/// blocking-off per plan §5-Q1) runs it explicitly with the Category
/// filter.</para>
/// </summary>
[Trait("Category", "Pact")]
public sealed class PactVerifierTests : IClassFixture<PactVerifierFixture>
{
    private readonly PactVerifierFixture _fixture;

    public PactVerifierTests(PactVerifierFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Replay every interaction in
    /// <c>contracts/pacts/vrbook-web-vrbook-api.json</c> against the
    /// fixture's host. State setup goes via
    /// <c>POST /pact-states</c> → <see cref="PactProviderStateHandler"/>.
    /// </summary>
    [Fact(Skip = "OPS.1.3 lands shape only. Verifier call resumes at OPS.1.5 after WAF+Kestrel adapter lands.")]
    public void VerifyPacts()
    {
        // OPS.1.5 target code shape (for reference — activated when the
        // WAF+Kestrel adapter is in place):
        //
        // var pactFile = Path.Combine(
        //     Directory.GetCurrentDirectory(),
        //     "..", "..", "..", "..", "..",
        //     "contracts", "pacts", "vrbook-web-vrbook-api.json");
        //
        // var verifier = new PactVerifier(new PactVerifierConfig
        // {
        //     LogLevel = PactLogLevel.Warn,
        // });
        //
        // verifier
        //     .ServiceProvider("vrbook-api", _fixture.HostUri)
        //     .WithFileSource(new FileInfo(pactFile))
        //     .WithProviderStateUrl(new Uri(_fixture.HostUri,
        //         PactVerifierFixture.PactStatesPath))
        //     .Verify();

        // Placeholder assertion so xUnit doesn't complain about an
        // effectively-empty fact when the Skip is lifted.
        Assert.NotNull(_fixture);
    }

    /// <summary>
    /// Sanity check that the fixture actually has the dispatch handler
    /// registered — catches DI misconfiguration before the verifier work
    /// activates in OPS.1.5.
    /// </summary>
    [Fact]
    public void Fixture_registers_PactProviderStateHandler()
    {
        using var scope = _fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetService<PactProviderStateHandler>();
        Assert.NotNull(handler);
    }
}
