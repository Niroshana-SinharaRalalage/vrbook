using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VrBook.Api.IntegrationTests.Multitenancy;
using Xunit;

namespace VrBook.Api.IntegrationTests.Contract.Catalog;

/// <summary>
/// VRB-101 — HTTP contract for property image upload/reorder/delete. The
/// RouteMatrix already asserts the anonymous(401) dimension; this covers the
/// owner happy-path, per-resource cross-tenant isolation, validation (422), and
/// the no-orphan-blob delete contract (asserted against the in-memory blob fake).
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class PropertyImagesContractTests(TwoTenantApiFixture fixture)
{
    private static MultipartFormDataContent Png(string? caption = null, int bytes = 8, string contentType = "image/png")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[bytes]);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "photo.png");
        if (caption is not null)
        {
            form.Add(new StringContent(caption), "caption");
        }
        return form;
    }

    private static async Task<string> UploadAsync(HttpClient client, Guid propertyId, string? caption = null)
    {
        var resp = await client.PostAsync($"/api/v1/properties/{propertyId}/images", Png(caption));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Owner_uploads_to_own_property_returns_201_with_dto_and_writes_a_blob()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var before = fixture.Blobs.Count;

        var resp = await client.PostAsync(
            $"/api/v1/properties/{fixture.TenantAPropertyId}/images", Png(caption: "Ocean view"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("url").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("caption").GetString().Should().Be("Ocean view");
        fixture.Blobs.Count.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task Uploaded_image_appears_in_the_property_detail()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var imageId = await UploadAsync(client, fixture.TenantAPropertyId);

        var detail = await client.GetStringAsync($"/api/v1/admin/properties/{fixture.TenantAPropertyId}");

        detail.Should().Contain(imageId);
    }

    [Fact]
    public async Task Cross_tenant_upload_is_refused()
    {
        var client = fixture.CreateClientAs("OwnerB");

        var resp = await client.PostAsync(
            $"/api/v1/properties/{fixture.TenantAPropertyId}/images", Png());

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Oversize_upload_is_422()
    {
        var client = fixture.CreateClientAs("OwnerA");

        // 11 MB > the 10 MB default limit.
        var resp = await client.PostAsync(
            $"/api/v1/properties/{fixture.TenantAPropertyId}/images", Png(bytes: 11 * 1024 * 1024));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Disallowed_content_type_is_422()
    {
        var client = fixture.CreateClientAs("OwnerA");

        var resp = await client.PostAsync(
            $"/api/v1/properties/{fixture.TenantAPropertyId}/images",
            Png(contentType: "text/plain"));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Delete_removes_the_row_and_the_blob()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var imageId = await UploadAsync(client, fixture.TenantAPropertyId);
        var blobsAfterUpload = fixture.Blobs.Count;

        var del = await client.DeleteAsync($"/api/v1/properties/{fixture.TenantAPropertyId}/images/{imageId}");

        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        fixture.Blobs.Count.Should().BeLessThan(blobsAfterUpload);
        var detail = await client.GetStringAsync($"/api/v1/admin/properties/{fixture.TenantAPropertyId}");
        detail.Should().NotContain(imageId);
    }

    [Fact]
    public async Task Reorder_persists_order_and_makes_the_first_primary()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var justUploaded = await UploadAsync(client, fixture.TenantAPropertyId);

        // The fixture property accumulates images across the shared collection,
        // so reorder the FULL current set (reversed) — our just-uploaded image
        // lands first and must become the primary/cover.
        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/admin/properties/{fixture.TenantAPropertyId}");
        var ids = detail.GetProperty("images").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()!).ToList();
        ids.Should().Contain(justUploaded);
        var reversed = ids.AsEnumerable().Reverse().ToArray();

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/properties/{fixture.TenantAPropertyId}/images/order",
            new { orderedImageIds = reversed });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var images = doc.RootElement.EnumerateArray().ToList();
        images[0].GetProperty("id").GetString().Should().Be(reversed[0]);
        images[0].GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        images[0].GetProperty("sortOrder").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Cross_tenant_delete_is_refused()
    {
        var owner = fixture.CreateClientAs("OwnerA");
        var imageId = await UploadAsync(owner, fixture.TenantAPropertyId);

        var attacker = fixture.CreateClientAs("OwnerB");
        var del = await attacker.DeleteAsync($"/api/v1/properties/{fixture.TenantAPropertyId}/images/{imageId}");

        del.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }
}
