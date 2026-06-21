using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using VrBook.Modules.Notifications.Application.Dispatch;
using VrBook.Modules.Notifications.Domain;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice 4 C3: every embedded Mustache template must render with its
/// <c>Templates/Samples/&lt;name&gt;.json</c> fixture and produce a non-empty body.
/// Catches "you added a template but forgot to register it / supply fixture data /
/// the partial loader broke" regressions in CI.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NotificationTemplatesRenderTests
{
    private static readonly Assembly NotificationsAsm =
        typeof(VrBook.Modules.Notifications.NotificationsModule).Assembly;

    public static IEnumerable<object[]> AllKinds() => Enum.GetValues<NotificationKind>()
        .Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_kind_renders_with_its_sample(NotificationKind kind)
    {
        var renderer = new MustacheTemplateRenderer(EmptyConfig());
        var sample = LoadSample(MustacheTemplateRenderer.TemplateNameFor(kind));

        var act = () => renderer.Render(kind, sample);

        var rendered = act.Should().NotThrow().Subject;
        rendered.Html.Should().NotBeNullOrWhiteSpace($"HTML for {kind} must be non-empty");
        rendered.Html.Should().Contain("<body", "the shared layout wraps every template");
        rendered.PlainText.Should().NotBeNullOrWhiteSpace($"plain text for {kind} must be non-empty");
    }

    [Fact]
    public void Layout_and_partials_resolve()
    {
        // Manifest must include the three partials, otherwise the partial-loader
        // throws at runtime under whatever NotificationKind first hits production.
        var names = NotificationsAsm.GetManifestResourceNames();
        names.Should().Contain(n => n.EndsWith("Templates._layout.mustache", StringComparison.Ordinal));
        names.Should().Contain(n => n.EndsWith("Templates._header.mustache", StringComparison.Ordinal));
        names.Should().Contain(n => n.EndsWith("Templates._footer.mustache", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_named_template_has_a_matching_sample()
    {
        var manifest = NotificationsAsm.GetManifestResourceNames();
        var templates = manifest
            .Where(n => n.EndsWith(".mustache", StringComparison.Ordinal))
            .Where(n => !n.Contains("._", StringComparison.Ordinal)) // exclude partials
            .Select(n => n.Replace("VrBook.Modules.Notifications.Templates.", "", StringComparison.Ordinal))
            .Select(n => n.Replace(".mustache", "", StringComparison.Ordinal))
            .ToArray();

        templates.Should().NotBeEmpty("if the manifest is empty the csproj glob is misconfigured");

        foreach (var name in templates)
        {
            var sampleResource = $"VrBook.Modules.Notifications.Templates.Samples.{name}.json";
            manifest.Should().Contain(sampleResource,
                $"every template needs a sample fixture under Templates/Samples/{name}.json");
        }
    }

    private static string LoadSample(string templateName)
    {
        var resource = $"VrBook.Modules.Notifications.Templates.Samples.{templateName}.json";
        using var stream = NotificationsAsm.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"Sample fixture not found: {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
}
