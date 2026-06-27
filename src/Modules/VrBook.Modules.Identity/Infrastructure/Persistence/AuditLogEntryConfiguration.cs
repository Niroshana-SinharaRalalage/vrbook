using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        // Append-only — no audit/soft-delete columns. We deliberately keep this in the
        // identity schema; the proposal §14.5 anchors audit writes to the Identity
        // module's pipeline behavior, so co-locating the table is the simplest choice.
        b.ToTable("audit_log", IdentityDbContext.SchemaName);
        b.HasKey(a => a.Id);

        b.Property(a => a.OccurredAt).HasColumnName("occurred_at").IsRequired();

        // OPS.M.3a — tenant_id; nullable forever per OPS_M_3_PLAN §1.7 (Super
        // Admin actions and anonymous login-flow requests have no tenant).
        // No 3c flip; no factory empty-guard.
        b.Property(a => a.TenantId).HasColumnName("tenant_id").IsRequired(false);
        b.HasIndex(a => a.TenantId);

        b.Property(a => a.ActorUserId).HasColumnName("actor_user_id");
        b.Property(a => a.ActorRole).HasColumnName("actor_role").HasMaxLength(64);
        b.Property(a => a.Action).HasColumnName("action").HasMaxLength(200).IsRequired();
        b.Property(a => a.TargetType).HasColumnName("target_type").HasMaxLength(200);
        b.Property(a => a.TargetId).HasColumnName("target_id").HasMaxLength(64);
        b.Property(a => a.Before).HasColumnName("before").HasColumnType("jsonb");
        b.Property(a => a.After).HasColumnName("after").HasColumnType("jsonb");
        b.Property(a => a.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
        b.Property(a => a.UserAgent).HasColumnName("user_agent").HasMaxLength(1024);
        b.Property(a => a.TraceId).HasColumnName("trace_id").HasMaxLength(64);

        b.HasIndex(a => new { a.OccurredAt, a.ActorUserId });
        b.HasIndex(a => a.Action);
    }
}
