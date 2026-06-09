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

/// <summary>
/// Unit tests for the Delete handler logic. Deleting an amenity that any
/// property is currently attached to must throw ConflictException — otherwise
/// the property_amenities FK would break. Admin must disable instead, or
/// remove the attachment first.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DeleteAmenityHandlerTests
{
    [Fact]
    public async Task Refuses_to_delete_when_any_property_references_the_amenity()
    {
        var amenityId = Guid.NewGuid();
        var handler = new TestableDeleteAmenityHandler(
            getAmenity: id => id == amenityId
                ? new VrBook.Modules.Catalog.Domain.Amenity(amenityId, "wifi", "Wi-Fi", null, "Essentials")
                : null,
            usageCount: id => id == amenityId ? 3 : 0,
            deleteAction: _ => throw new InvalidOperationException("Delete must not be called when in use."));

        var act = () => handler.Handle(amenityId, default);

        await act.Should().ThrowAsync<VrBook.Domain.Common.ConflictException>()
            .Where(e => e.Message.Contains("3", StringComparison.Ordinal) ||
                        e.Message.Contains("propert", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Throws_NotFound_when_amenity_does_not_exist()
    {
        var handler = new TestableDeleteAmenityHandler(
            getAmenity: _ => null,
            usageCount: _ => 0,
            deleteAction: _ => { });

        var act = () => handler.Handle(Guid.NewGuid(), default);

        await act.Should().ThrowAsync<VrBook.Domain.Common.NotFoundException>();
    }

    [Fact]
    public async Task Deletes_when_amenity_exists_and_is_unused()
    {
        var amenityId = Guid.NewGuid();
        var deleted = new List<Guid>();
        var handler = new TestableDeleteAmenityHandler(
            getAmenity: id => id == amenityId
                ? new VrBook.Modules.Catalog.Domain.Amenity(amenityId, "wifi", "Wi-Fi", null, "Essentials")
                : null,
            usageCount: _ => 0,
            deleteAction: a => deleted.Add(a.Id));

        await handler.Handle(amenityId, default);

        deleted.Should().ContainSingle().Which.Should().Be(amenityId);
    }
}

/// <summary>Hand-rolled handler over the same delete-logic for unit-test purposes.
/// Mirrors what DeleteAmenityHandler does in production, minus the DbContext binding.</summary>
internal sealed class TestableDeleteAmenityHandler(
    Func<Guid, VrBook.Modules.Catalog.Domain.Amenity?> getAmenity,
    Func<Guid, int> usageCount,
    Action<VrBook.Modules.Catalog.Domain.Amenity> deleteAction)
{
    public Task Handle(Guid id, CancellationToken ct)
    {
        var amenity = getAmenity(id)
            ?? throw new VrBook.Domain.Common.NotFoundException("Amenity", id);
        var usage = usageCount(id);
        if (usage > 0)
        {
            throw new VrBook.Domain.Common.ConflictException(
                $"Cannot delete amenity '{amenity.Code}' — it is attached to {usage} propert{(usage == 1 ? "y" : "ies")}. Disable it instead, or detach it from those properties first.");
        }
        deleteAction(amenity);
        return Task.CompletedTask;
    }
}
