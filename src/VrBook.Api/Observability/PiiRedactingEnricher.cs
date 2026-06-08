using Serilog.Core;
using Serilog.Events;

namespace VrBook.Api.Observability;

/// <summary>
/// Walks every <see cref="LogEvent"/> property and redacts values whose key matches a
/// known PII field name. Catches both destructured complex objects
/// (<c>Log.Information("User {@User}", user)</c>) and scalar fields
/// (<c>Log.Information("Email {Email}", email)</c>). Applies recursively to
/// <see cref="StructureValue"/>, <see cref="SequenceValue"/>, and
/// <see cref="DictionaryValue"/>.
/// </summary>
public sealed class PiiRedactingEnricher : ILogEventEnricher
{
    // Case-insensitive. Order is irrelevant; HashSet is fine.
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // Identity / contact
        "email", "emails", "phone", "phoneNumber", "fullName", "displayName",
        "guestDisplayName", "guestEmail", "ownerEmail",
        // Address
        "address", "streetAddress", "city", "postalCode", "zip",
        // Payment
        "cardNumber", "cardLast4", "cvv", "stripeCustomerId",
        // Auth
        "password", "secret", "token", "accessToken", "refreshToken", "clientSecret",
        // Stripe sensitive bits
        "billingDetails", "shipping",
    };

    private const string RedactedMarker = "[REDACTED]";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var keys = logEvent.Properties.Keys.ToArray();
        foreach (var key in keys)
        {
            var value = logEvent.Properties[key];
            var newValue = Redact(key, value);
            if (!ReferenceEquals(newValue, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(key, newValue));
            }
        }
    }

    private static LogEventPropertyValue Redact(string key, LogEventPropertyValue value)
    {
        if (SensitiveKeys.Contains(key))
        {
            return new ScalarValue(RedactedMarker);
        }
        return value switch
        {
            StructureValue s => RedactStructure(s),
            DictionaryValue d => RedactDictionary(d),
            SequenceValue seq => RedactSequence(seq),
            _ => value,
        };
    }

    private static StructureValue RedactStructure(StructureValue s)
    {
        var props = s.Properties.Select(p =>
            new LogEventProperty(p.Name, Redact(p.Name, p.Value))).ToArray();
        return new StructureValue(props, s.TypeTag);
    }

    private static DictionaryValue RedactDictionary(DictionaryValue d)
    {
        var elements = d.Elements.Select(kv =>
        {
            var keyText = kv.Key.Value?.ToString() ?? string.Empty;
            return new KeyValuePair<ScalarValue, LogEventPropertyValue>(kv.Key, Redact(keyText, kv.Value));
        });
        return new DictionaryValue(elements);
    }

    private static SequenceValue RedactSequence(SequenceValue seq)
    {
        // Sequences don't carry keys; only recurse into nested structures.
        var elements = seq.Elements.Select(e => e switch
        {
            StructureValue ss => (LogEventPropertyValue)RedactStructure(ss),
            DictionaryValue dd => RedactDictionary(dd),
            SequenceValue ssq => RedactSequence(ssq),
            _ => e,
        });
        return new SequenceValue(elements);
    }
}
