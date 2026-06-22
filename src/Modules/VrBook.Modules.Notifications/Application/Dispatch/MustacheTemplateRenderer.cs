using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Stubble.Core.Builders;
using Stubble.Core.Interfaces;
using Stubble.Core.Settings;
using VrBook.Modules.Notifications.Domain;

namespace VrBook.Modules.Notifications.Application.Dispatch;

/// <summary>
/// Slice 4 C3: renders the per-<see cref="NotificationKind"/> Mustache template
/// against the deserialized payload JSON, wrapped in the shared layout
/// (<c>_layout.mustache</c> + <c>_header.mustache</c> + <c>_footer.mustache</c>).
///
/// <para>
/// Templates are embedded resources in <c>VrBook.Modules.Notifications.dll</c>.
/// The CI render test (<c>NotificationTemplatesRenderTests</c>) exercises every
/// kind against a <c>Samples/*.json</c> fixture and asserts a non-empty body.
/// </para>
///
/// <para>
/// Plain text is derived from the rendered HTML by a coarse tag-stripper
/// (Phase 1 acceptable). Phase 2 can add a per-template <c>.txt.mustache</c> sibling.
/// </para>
/// </summary>
public interface ITemplateRenderer
{
    RenderedEmail Render(NotificationKind kind, string payloadJson);
}

public sealed record RenderedEmail(string Html, string PlainText);

public sealed class MustacheTemplateRenderer : ITemplateRenderer
{
    private static readonly Assembly Asm = typeof(MustacheTemplateRenderer).Assembly;
    private static readonly Stubble.Core.StubbleVisitorRenderer Engine =
        new StubbleBuilder()
            .Configure(c => c.SetPartialTemplateLoader(new EmbeddedPartialLoader()))
            .Build();
    private static readonly RenderSettings Settings = new()
    {
        SkipHtmlEncoding = false,
    };

    private readonly Dictionary<string, object> _globals;

    public MustacheTemplateRenderer(IConfiguration configuration)
    {
        _globals = new Dictionary<string, object>
        {
            ["AppName"] = configuration["App:Name"] ?? "VrBook",
            ["SupportEmail"] = configuration["App:SupportEmail"] ?? "support@vrbook.example.com",
            ["Year"] = DateTimeOffset.UtcNow.Year.ToString(),
        };
    }

    public RenderedEmail Render(NotificationKind kind, string payloadJson)
    {
        var templateName = TemplateNameFor(kind);
        var body = RenderTemplate(templateName, payloadJson);

        var layoutModel = new Dictionary<string, object>(_globals)
        {
            ["Subject"] = templateName,
            ["Body"] = body,
        };
        var html = Engine.Render(LoadTemplate("_layout"), layoutModel, Settings);
        var plain = StripHtml(html);
        return new RenderedEmail(html, plain);
    }

    private string RenderTemplate(string name, string payloadJson)
    {
        var template = LoadTemplate(name);
        var model = ParsePayload(payloadJson);
        foreach (var (k, v) in _globals)
        {
            // Per-template render: globals available alongside the payload.
            model[k] = v;
        }
        return Engine.Render(template, model, Settings);
    }

    private static string LoadTemplate(string name)
    {
        // Embedded resource name follows the project's default namespace:
        //   VrBook.Modules.Notifications.Templates.<name>.mustache
        var resource = $"VrBook.Modules.Notifications.Templates.{name}.mustache";
        using var stream = Asm.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException(
                $"Template embedded resource not found: {resource}. Did you add it to the .csproj <EmbeddedResource> glob?");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Mustache wants a flat dictionary. We deserialize the payload JSON and
    /// flatten primitive properties; nested objects are stringified (good enough
    /// for Phase 1 — Phase 2 can recurse).
    /// </summary>
    private static Dictionary<string, object> ParsePayload(string payloadJson)
    {
        var dict = new Dictionary<string, object>();
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return dict;
        }

        using var doc = JsonDocument.Parse(payloadJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return dict;
        }
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => prop.Value.ToString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => string.Empty,
                _ => prop.Value.GetRawText(),
            };
        }
        return dict;
    }

    public static string TemplateNameFor(NotificationKind kind) => kind switch
    {
        NotificationKind.BookingPlaced => "booking.received",
        NotificationKind.BookingConfirmed => "booking.confirmed",
        NotificationKind.BookingRejected => "booking.rejected",
        NotificationKind.BookingCancelled => "booking.cancelled.guest",
        // Phase 1 stand-ins: kinds without a dedicated template reuse the
        // closest guest-side one. Real templates land in Slice 5 / 6.
        NotificationKind.BookingCheckedIn => "booking.confirmed",
        NotificationKind.BookingCheckedOut => "booking.confirmed",
        NotificationKind.BookingCompleted => "booking.completed",
        NotificationKind.MessageDeliveryDeferred => "booking.received",
        NotificationKind.PaymentCaptured => "booking.confirmed",
        NotificationKind.RefundIssued => "booking.cancelled.guest",
        NotificationKind.ReviewSubmitted => "booking.confirmed",

        // Slice 5
        NotificationKind.ReviewRequest => "review.request",
        NotificationKind.LoyaltyTierPromotion => "loyalty.tier_promotion",

        // Slice 4 C4: owner-side templates.
        NotificationKind.OwnerTentativeReceived => "owner.tentative_received",
        NotificationKind.OwnerActionRequiredReminder => "owner.action_required_24h_reminder",
        NotificationKind.OwnerAutoConfirmed => "owner.auto_confirmed",
        NotificationKind.OwnerCancellationAlert => "owner.cancellation_alert",
        NotificationKind.OwnerSyncConflict => "owner.sync_conflict",

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No template for kind."),
    };

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    /// <summary>
    /// Stubble partial loader that resolves <c>{{> name}}</c> against embedded
    /// templates by the same resource-name convention.
    /// </summary>
    private sealed class EmbeddedPartialLoader : IStubbleLoader
    {
        public string Load(string name) => LoadTemplate(name);
        public ValueTask<string> LoadAsync(string name) => ValueTask.FromResult(LoadTemplate(name));
        public IStubbleLoader Clone() => new EmbeddedPartialLoader();
    }
}
