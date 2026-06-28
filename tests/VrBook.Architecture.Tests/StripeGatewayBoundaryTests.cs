using System.Reflection;
using FluentAssertions;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.5 §10 #1 + #5 + Step 6e — pins the Stripe SDK boundary.
///
/// <para>The Stripe namespace is allowed ONLY inside
/// <c>VrBook.Modules.Payment.Infrastructure.Stripe</c>. Application handlers
/// must go through <see cref="IStripeGateway"/> / <c>IStripeConnectGateway</c>;
/// otherwise the retry pipeline and structured logging silently get bypassed.</para>
///
/// <para>The webhook dispatch table is enumerated separately to confirm every
/// new event type is registered.</para>
/// </summary>
public sealed class StripeGatewayBoundaryTests
{
    [Fact]
    public void Only_Stripe_namespace_files_reference_the_Stripe_SDK_namespace()
    {
        // Reach Payment assembly via a public type (IStripeGateway is internal —
        // use the StripeFeeCalculator helper which is public on the same DLL).
        var paymentAssembly = typeof(StripeFeeCalculator).Assembly;
        var stripeRefTypes = paymentAssembly.GetTypes()
            .Where(t => t.Namespace is not null
                && !t.Namespace.StartsWith("VrBook.Modules.Payment.Infrastructure.Stripe", StringComparison.Ordinal))
            .Where(ReferencesStripeNamespace)
            .Select(t => t.FullName)
            .ToList();
        stripeRefTypes.Should().BeEmpty(
            because: "OPS.M.5 §10 #1 — Stripe SDK usage is confined to Payment.Infrastructure.Stripe.");
    }

    [Fact]
    public void Webhook_dispatch_table_enumerates_at_least_the_OPS_M_5_baseline_event_types()
    {
        // OPS.M.5 §3.7 (D7) — these are the seven event types the slice locks in.
        var baseline = new[]
        {
            "payment_intent.amount_capturable_updated",
            "payment_intent.succeeded",
            "payment_intent.payment_failed",
            "payment_intent.canceled",
            "charge.refunded",
            "charge.dispute.created",
            "account.updated",
        };
        var supported = ReadDispatchKeys();
        foreach (var et in baseline)
        {
            supported.Should().Contain(et);
        }
    }

    private static bool ReferencesStripeNamespace(Type t)
    {
        // A type "references" the Stripe SDK if any of its declared members
        // (constructors, methods, fields, properties) use a type whose
        // namespace starts with "Stripe" (excluding "Stripe.net" subnamespace
        // helpers we author ourselves under VrBook.*).
        bool IsStripeSdk(Type? candidate)
        {
            if (candidate is null)
            {
                return false;
            }

            var ns = candidate.Namespace ?? string.Empty;
            return ns == "Stripe" || ns.StartsWith("Stripe.", StringComparison.Ordinal);
        }

        foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (ctor.GetParameters().Any(p => IsStripeSdk(p.ParameterType)))
            {
                return true;
            }
        }
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (IsStripeSdk(m.ReturnType))
            {
                return true;
            }

            if (m.GetParameters().Any(p => IsStripeSdk(p.ParameterType)))
            {
                return true;
            }
        }
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (IsStripeSdk(f.FieldType))
            {
                return true;
            }
        }
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (IsStripeSdk(p.PropertyType))
            {
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyCollection<string> ReadDispatchKeys()
    {
        var prop = typeof(VrBook.Modules.Payment.Application.Commands.HandleStripeWebhookHandler)
            .GetProperty(
                "SupportedEventTypes",
                BindingFlags.Static | BindingFlags.NonPublic)!;
        return (IReadOnlyCollection<string>)prop.GetValue(null)!;
    }
}
