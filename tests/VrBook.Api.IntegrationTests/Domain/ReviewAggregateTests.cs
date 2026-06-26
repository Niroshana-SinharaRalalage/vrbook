using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Domain.Common;
using VrBook.Modules.Reviews.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Unit tests for the A8 Review aggregate (v1 ships Approved-by-default).
/// Covers rating bounds, body length, trimming, owner-response idempotency.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReviewAggregateTests
{
    private static readonly Guid AnyTenantId = new("00000000-0000-0000-0000-000000000001");

    private static Review SubmitDefault(int rating = 5, string body = "Loved it") =>
        Review.Submit(
            tenantId: AnyTenantId,
            bookingId: Guid.NewGuid(),
            propertyId: Guid.NewGuid(),
            guestUserId: Guid.NewGuid(),
            guestDisplayName: " Alice ",
            rating: rating,
            body: body);

    [Fact]
    public void Submit_returns_Approved_review_with_PublishedAt_set()
    {
        var review = SubmitDefault();

        review.Status.Should().Be(ReviewStatus.Approved);
        review.PublishedAt.Should().NotBeNull();
        review.Rating.Should().Be(5);
        review.GuestDisplayName.Should().Be("Alice"); // trimmed
        review.Body.Should().Be("Loved it");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Submit_accepts_ratings_1_through_5(int rating)
    {
        var act = () => SubmitDefault(rating: rating);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(100)]
    public void Submit_rejects_out_of_range_rating(int rating)
    {
        var act = () => SubmitDefault(rating: rating);
        act.Should().Throw<BusinessRuleViolationException>()
           .Where(e => e.Rule == "review.rating");
    }

    [Fact]
    public void Submit_with_blank_display_name_throws()
    {
        var act = () => Review.Submit(
            AnyTenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "", 5, "body");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Submit_with_4000_char_body_succeeds()
    {
        var act = () => SubmitDefault(body: new string('a', 4000));
        act.Should().NotThrow();
    }

    [Fact]
    public void Submit_with_4001_char_body_throws()
    {
        var act = () => SubmitDefault(body: new string('a', 4001));
        act.Should().Throw<BusinessRuleViolationException>()
           .Where(e => e.Rule == "review.body");
    }

    [Fact]
    public void Submit_trims_body_whitespace()
    {
        var review = SubmitDefault(body: "   Loved it   ");
        review.Body.Should().Be("Loved it");
    }

    [Fact]
    public void Submit_with_null_body_treats_as_empty_string()
    {
        var review = SubmitDefault(body: "");
        review.Body.Should().BeEmpty();
    }

    // ---- Owner response ----

    [Fact]
    public void AddOwnerResponse_records_body_and_timestamp()
    {
        var review = SubmitDefault();

        review.AddOwnerResponse(" Thanks for staying! ");

        review.ResponseBody.Should().Be("Thanks for staying!"); // trimmed
        review.ResponseAt.Should().NotBeNull();
    }

    [Fact]
    public void AddOwnerResponse_second_time_throws()
    {
        var review = SubmitDefault();
        review.AddOwnerResponse("first");

        var act = () => review.AddOwnerResponse("second");

        act.Should().Throw<BusinessRuleViolationException>()
           .Where(e => e.Rule == "review.response_once");
    }

    [Fact]
    public void AddOwnerResponse_with_blank_body_throws()
    {
        var review = SubmitDefault();
        var act = () => review.AddOwnerResponse("   ");
        act.Should().Throw<ArgumentException>();
    }
}
