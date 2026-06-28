using FluentAssertions;
using VrBook.Contracts.Dtos;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.7 §4.1 + Step 1 — pins the read-side DTO contract surfaced
/// by the onboarding wizard. Web client decodes by positional record shape;
/// this test prevents an accidental field reorder from breaking
/// deserialization.
/// </summary>
public sealed class MeTenantDtoShapeTests
{
    [Fact]
    public void MeTenantDto_exists_in_VrBook_Contracts_Dtos()
    {
        var t = typeof(MeTenantDto);
        t.Namespace.Should().Be("VrBook.Contracts.Dtos");
        t.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void OnboardingProgressDto_exists_in_VrBook_Contracts_Dtos()
    {
        var t = typeof(OnboardingProgressDto);
        t.Namespace.Should().Be("VrBook.Contracts.Dtos");
        t.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void MeTenantDto_has_Onboarding_property_of_type_OnboardingProgressDto()
    {
        var prop = typeof(MeTenantDto).GetProperty("Onboarding");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(OnboardingProgressDto));
    }

    [Fact]
    public void MeTenantDto_positional_ctor_matches_plan_4_1_shape()
    {
        // OPS.M.7 §4.1 — leading positional parameters in this order.
        // A field reorder breaks JSON deserialization on the web client.
        var ctor = typeof(MeTenantDto).GetConstructors().Single();
        var parameterNames = ctor.GetParameters().Select(p => p.Name).ToArray();
        parameterNames.Should().Equal(
            "Id", "Slug", "DisplayName", "Status",
            "DefaultCurrency", "PlatformFeeBps",
            "StripeAccountStatus", "ChargesEnabled", "PayoutsEnabled",
            "HasStripeAccount", "PropertyCount", "Onboarding");
    }

    [Fact]
    public void OnboardingProgressDto_positional_ctor_matches_plan_4_1_shape()
    {
        var ctor = typeof(OnboardingProgressDto).GetConstructors().Single();
        var parameterNames = ctor.GetParameters().Select(p => p.Name).ToArray();
        parameterNames.Should().Equal("IsComplete", "NextStep");
    }
}
