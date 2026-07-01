using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Slice OPS.M.13 — EF configuration for <see cref="MigrationAuditEntry"/>.
/// Maps to <c>identity.migration_audit</c> per
/// <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §2.1.
///
/// <para>App-side is read-only. Rows are INSERTed by migration SQL
/// (raw <c>MigrationBuilder.Sql</c>) inside the same transaction as
/// the data-heal step.</para>
/// </summary>
internal sealed class MigrationAuditEntryConfiguration : IEntityTypeConfiguration<MigrationAuditEntry>
{
    public void Configure(EntityTypeBuilder<MigrationAuditEntry> b)
    {
        b.ToTable("migration_audit", "identity");
        b.HasKey(x => x.Id);

        b.Property(x => x.MigrationName)
            .HasColumnName("migration_name")
            .HasMaxLength(200)
            .IsRequired();

        b.Property(x => x.StepName)
            .HasColumnName("step_name")
            .HasMaxLength(120)
            .IsRequired();

        b.Property(x => x.AffectedCount).HasColumnName("affected_count").IsRequired();
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.ExecutedAt).HasColumnName("executed_at").IsRequired();

        b.HasIndex(x => new { x.MigrationName, x.ExecutedAt })
            .HasDatabaseName("ix_migration_audit_migration_executed")
            .IsDescending(false, true);
    }
}
