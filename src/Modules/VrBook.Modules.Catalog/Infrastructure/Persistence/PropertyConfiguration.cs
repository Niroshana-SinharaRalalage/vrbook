using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Catalog.Domain;

namespace VrBook.Modules.Catalog.Infrastructure.Persistence;

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> b)
    {
        b.ToTable("properties", CatalogDbContext.SchemaName);
        b.HasKey(p => p.Id);

        b.Property(p => p.Slug).HasColumnName("slug").HasMaxLength(160).IsRequired();
        b.HasIndex(p => p.Slug).IsUnique();

        b.Property(p => p.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(p => p.Description).HasColumnName("description").HasColumnType("text").IsRequired();
        b.Property(p => p.Type).HasColumnName("property_type").HasConversion<string>().HasMaxLength(40).IsRequired();

        // Address (value object — flattened columns).
        b.OwnsOne(p => p.Address, addr =>
        {
            addr.Property(a => a.Street).HasColumnName("street").HasMaxLength(200).IsRequired();
            addr.Property(a => a.City).HasColumnName("city").HasMaxLength(120).IsRequired();
            addr.Property(a => a.State).HasColumnName("state").HasMaxLength(120);
            addr.Property(a => a.PostalCode).HasColumnName("postal_code").HasMaxLength(40);
            addr.Property(a => a.Country).HasColumnName("country").HasMaxLength(80).IsRequired();
            addr.Property(a => a.Latitude).HasColumnName("latitude").HasColumnType("numeric(9,6)");
            addr.Property(a => a.Longitude).HasColumnName("longitude").HasColumnType("numeric(9,6)");
        });

        b.OwnsOne(p => p.Capacity, cap =>
        {
            cap.Property(c => c.MaxGuests).HasColumnName("max_guests");
            cap.Property(c => c.Bedrooms).HasColumnName("bedrooms");
            cap.Property(c => c.Bathrooms).HasColumnName("bathrooms");
            cap.Property(c => c.Beds).HasColumnName("beds");
        });

        b.OwnsOne(p => p.CheckInWindow, cw =>
        {
            cw.Property(c => c.CheckinFrom).HasColumnName("checkin_from");
            cw.Property(c => c.CheckinTo).HasColumnName("checkin_to");
            cw.Property(c => c.CheckoutBy).HasColumnName("checkout_by");
        });

        b.Property(p => p.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        b.HasIndex(p => p.OwnerUserId);

        // OPS.M.3c — NOT NULL after Wave B backfill. Cross-schema FK to
        // identity.tenants("Id") is declared via raw SQL in the 3a migration
        // (EF doesn't model cross-DbContext FKs).
        b.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        b.HasIndex(p => p.TenantId);

        b.Property(p => p.IsActive).HasColumnName("is_active").HasDefaultValue(false);
        b.Property(p => p.ReviewsEnabled).HasColumnName("reviews_enabled").HasDefaultValue(true);
        b.Property(p => p.DynamicPricingEnabled).HasColumnName("dynamic_pricing_enabled").HasDefaultValue(false);
        b.Property(p => p.MessagingEnabled).HasColumnName("messaging_enabled").HasDefaultValue(true);

        b.Property(p => p.RatingAvg).HasColumnName("rating_avg").HasColumnType("numeric(3,2)");
        b.Property(p => p.RatingCount).HasColumnName("rating_count").HasDefaultValue(0);

        // Audit + concurrency from AggregateRoot.
        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.Property(p => p.CreatedBy).HasColumnName("created_by");
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        b.Property(p => p.UpdatedBy).HasColumnName("updated_by");
        // Override the BaseDbContext-applied IsConcurrencyToken on row_version.
        // The aggregate is single-actor (the owner) for now; we can re-enable
        // when collaborative editing actually exists. Keeping it on caused
        // single-request UPDATEs to throw DbUpdateConcurrencyException with
        // the auto-increment logic in BaseDbContext.ApplyAudit on staging
        // Postgres - to be re-investigated alongside the rest of optimistic
        // concurrency story in A6/A7.
        b.Property(p => p.RowVersion).HasColumnName("row_version").IsConcurrencyToken(false);
        b.Property(p => p.DeletedAt).HasColumnName("deleted_at");
        b.Property(p => p.DeletedBy).HasColumnName("deleted_by");

        // Owned collections.
        b.HasMany(p => p.Images)
            .WithOne()
            .HasForeignKey(i => i.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(p => p.HouseRules)
            .WithOne()
            .HasForeignKey(r => r.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Many-to-many to Amenity via the property_amenities join table.
        // We keep the join opaque (no entity) since the join carries no extra
        // columns; the Property aggregate exposes AmenityIds.
        b.HasMany<Amenity>()
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "property_amenities",
                a => a.HasOne<Amenity>().WithMany().HasForeignKey("amenity_id"),
                p => p.HasOne<Property>().WithMany().HasForeignKey("property_id"),
                join =>
                {
                    join.ToTable("property_amenities", CatalogDbContext.SchemaName);
                    join.HasKey("property_id", "amenity_id");
                });

        // Keep AmenityIds in sync with the join via an unmapped backing list +
        // an Owned shadow projection. We use a simple approach: ignore the
        // navigation in the model and let SearchHandlers query the join directly.
        b.Ignore(p => p.AmenityIds);

        // Filtered city index for the dominant search predicate.
        b.HasIndex(p => p.IsActive).HasDatabaseName("ix_properties_is_active")
            .HasFilter("deleted_at IS NULL");
    }
}
