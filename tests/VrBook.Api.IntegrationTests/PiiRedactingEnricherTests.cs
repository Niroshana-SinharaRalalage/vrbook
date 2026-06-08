using FluentAssertions;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using VrBook.Api.Observability;
using Xunit;

namespace VrBook.Api.IntegrationTests;

/// <summary>
/// Unit tests for <see cref="PiiRedactingEnricher"/>. Category "Unit" so the
/// integration-test CI step still focuses on Testcontainers-backed flows;
/// these run in the unit-test step which has no DB dependency.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PiiRedactingEnricherTests
{
    private static LogEvent MakeEvent(params LogEventProperty[] properties)
    {
        var template = new MessageTemplateParser().Parse("test");
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            messageTemplate: template,
            properties: properties);
    }

    private static void Run(LogEvent evt)
    {
        new PiiRedactingEnricher().Enrich(evt, new LogEventPropertyFactoryStub());
    }

    [Theory]
    [InlineData("email", "alice@example.com")]
    [InlineData("Email", "alice@example.com")]
    [InlineData("EMAIL", "alice@example.com")]
    [InlineData("phone", "+1-555-1234")]
    [InlineData("displayName", "Alice Smith")]
    [InlineData("password", "hunter2")]
    [InlineData("cardLast4", "4242")]
    public void Redacts_scalar_property_with_sensitive_name(string key, string value)
    {
        var evt = MakeEvent(new LogEventProperty(key, new ScalarValue(value)));

        Run(evt);

        evt.Properties[key].Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("[REDACTED]");
    }

    [Fact]
    public void Leaves_neutral_property_untouched()
    {
        var evt = MakeEvent(
            new LogEventProperty("RequestName", new ScalarValue("PlaceBookingCommand")),
            new LogEventProperty("ElapsedMs", new ScalarValue(42)));

        Run(evt);

        ((ScalarValue)evt.Properties["RequestName"]).Value.Should().Be("PlaceBookingCommand");
        ((ScalarValue)evt.Properties["ElapsedMs"]).Value.Should().Be(42);
    }

    [Fact]
    public void Redacts_nested_email_inside_destructured_object()
    {
        // Mimic Log.Information("User {@User}", new { email = "...", isAdmin = true })
        var userValue = new StructureValue(
            new[]
            {
                new LogEventProperty("email", new ScalarValue("alice@example.com")),
                new LogEventProperty("isAdmin", new ScalarValue(true)),
            },
            typeTag: "User");
        var evt = MakeEvent(new LogEventProperty("User", userValue));

        Run(evt);

        var structure = (StructureValue)evt.Properties["User"];
        var email = (ScalarValue)structure.Properties.Single(p => p.Name == "email").Value;
        var isAdmin = (ScalarValue)structure.Properties.Single(p => p.Name == "isAdmin").Value;
        email.Value.Should().Be("[REDACTED]");
        isAdmin.Value.Should().Be(true);
    }

    [Fact]
    public void Redacts_email_inside_sequence_of_objects()
    {
        // Log.Information("Guests {@Guests}", guests) where each guest has email
        var guest1 = new StructureValue(
            new[] { new LogEventProperty("email", new ScalarValue("g1@example.com")) });
        var guest2 = new StructureValue(
            new[] { new LogEventProperty("email", new ScalarValue("g2@example.com")) });
        var guests = new SequenceValue(new LogEventPropertyValue[] { guest1, guest2 });
        var evt = MakeEvent(new LogEventProperty("Guests", guests));

        Run(evt);

        var seq = (SequenceValue)evt.Properties["Guests"];
        foreach (StructureValue s in seq.Elements.Cast<StructureValue>())
        {
            ((ScalarValue)s.Properties.Single(p => p.Name == "email").Value).Value
                .Should().Be("[REDACTED]");
        }
    }

    [Fact]
    public void Redacts_email_inside_dictionary()
    {
        // Log.Information("Map {@Map}", new Dictionary<string,string>{["email"]="..."})
        var dict = new DictionaryValue(new[]
        {
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                new ScalarValue("email"), new ScalarValue("dict@example.com")),
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                new ScalarValue("RequestName"), new ScalarValue("Test")),
        });
        var evt = MakeEvent(new LogEventProperty("Map", dict));

        Run(evt);

        var m = (DictionaryValue)evt.Properties["Map"];
        var emailEntry = m.Elements.Single(kv => kv.Key.Value!.ToString() == "email");
        ((ScalarValue)emailEntry.Value).Value.Should().Be("[REDACTED]");
        var rnEntry = m.Elements.Single(kv => kv.Key.Value!.ToString() == "RequestName");
        ((ScalarValue)rnEntry.Value).Value.Should().Be("Test");
    }

    private sealed class LogEventPropertyFactoryStub : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
