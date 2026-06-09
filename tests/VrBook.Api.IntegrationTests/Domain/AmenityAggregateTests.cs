using FluentAssertions;
using VrBook.Modules.Catalog.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Unit tests for the A2 Amenity aggregate after A2.2 extensions:
/// IsActive default-true, Update for name/icon/category, Disable/Enable
/// transitions. These tests are written FIRST (TDD red phase) so the
/// implementation must satisfy them.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AmenityAggregateTests
{
    private static Amenity New(string code = "wifi", string category = "Essentials") =>
        new(Guid.NewGuid(), code, "Wi-Fi", "wifi", category);

    [Fact]
    public void Constructor_normalizes_code_to_lowercase_and_trims()
    {
        var a = new Amenity(Guid.NewGuid(), "  WiFi  ", "Wi-Fi", "wifi", "Essentials");
        a.Code.Should().Be("wifi");
        a.Name.Should().Be("Wi-Fi");
    }

    [Fact]
    public void Constructor_blank_code_throws()
    {
        Action act = () => _ = new Amenity(Guid.NewGuid(), "  ", "n", null, "Essentials");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_treats_blank_icon_as_null()
    {
        var a = new Amenity(Guid.NewGuid(), "x", "name", "   ", "Essentials");
        a.Icon.Should().BeNull();
    }

    [Fact]
    public void Newly_constructed_amenity_is_active()
    {
        var a = New();
        a.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Update_overwrites_display_fields_and_keeps_code_stable()
    {
        var a = New(code: "wifi");

        a.Update(name: "Fast Wi-Fi", icon: "wifi-fast", category: "Internet & Office");

        a.Code.Should().Be("wifi");                          // immutable
        a.Name.Should().Be("Fast Wi-Fi");
        a.Icon.Should().Be("wifi-fast");
        a.Category.Should().Be("Internet & Office");
    }

    [Fact]
    public void Update_trims_inputs_and_treats_blank_icon_as_null()
    {
        var a = New();
        a.Update(name: "  X  ", icon: "  ", category: "  Cat  ");
        a.Name.Should().Be("X");
        a.Icon.Should().BeNull();
        a.Category.Should().Be("Cat");
    }

    [Fact]
    public void Update_blank_name_throws()
    {
        var a = New();
        Action act = () => a.Update("", null, "Essentials");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_blank_category_throws()
    {
        var a = New();
        Action act = () => a.Update("name", null, " ");
        act.Should().Throw<ArgumentException>();
    }

    // ---- Enable / Disable ----

    [Fact]
    public void Disable_marks_inactive()
    {
        var a = New();

        a.Disable();

        a.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Disable_on_already_inactive_is_idempotent()
    {
        var a = New();
        a.Disable();

        a.Disable(); // no throw

        a.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Enable_makes_inactive_amenity_active()
    {
        var a = New();
        a.Disable();

        a.Enable();

        a.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Enable_on_already_active_is_idempotent()
    {
        var a = New();

        a.Enable();

        a.IsActive.Should().BeTrue();
    }
}
