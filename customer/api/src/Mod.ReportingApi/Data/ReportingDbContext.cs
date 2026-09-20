using Microsoft.EntityFrameworkCore;
using Mod.ReportingApi.Auth;
using Mod.ReportingApi.Data.Entities;

namespace Mod.ReportingApi.Data;

public sealed class ReportingDbContext(
    DbContextOptions<ReportingDbContext> opts,
    ITenantContext tenant) : DbContext(opts)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Line> Lines => Set<Line>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<RollupMinute> RollupMinutes => Set<RollupMinute>();
    public DbSet<CollectorFreshness> CollectorFreshnesses => Set<CollectorFreshness>();
    public DbSet<Reconciliation> Reconciliations => Set<Reconciliation>();
    public DbSet<SequenceGap> SequenceGaps => Set<SequenceGap>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Tenant>(e =>
        {
            e.ToTable("Tenant", "registry");
            e.HasKey(t => t.TenantId);
        });

        mb.Entity<Site>(e =>
        {
            e.ToTable("Site", "registry");
            e.HasKey(s => s.SiteId);
            e.HasQueryFilter(s => s.TenantId == tenant.TenantId);
            e.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.SiteId);
        });

        mb.Entity<Line>(e =>
        {
            e.ToTable("Line", "registry");
            e.HasKey(l => l.LineId);
            e.HasQueryFilter(l => l.TenantId == tenant.TenantId);
            e.HasMany(l => l.Assets).WithOne().HasForeignKey(a => a.LineId);
        });

        mb.Entity<Asset>(e =>
        {
            e.ToTable("Asset", "registry");
            e.HasKey(a => a.AssetId);
            e.HasQueryFilter(a => a.TenantId == tenant.TenantId);
        });

        mb.Entity<RollupMinute>(e =>
        {
            e.ToTable("RollupMinute", "telemetry");
            e.HasKey(r => new { r.AssetId, r.MinuteStart });
            e.HasQueryFilter(r => r.TenantId == tenant.TenantId);
            e.Property(r => r.MinuteStart).HasColumnType("datetime2(0)");
        });

        mb.Entity<CollectorFreshness>(e =>
        {
            e.ToTable("CollectorFreshness", "telemetry");
            e.HasKey(c => c.CollectorId);
            e.HasQueryFilter(c => c.TenantId == tenant.TenantId);
        });

        mb.Entity<Reconciliation>(e =>
        {
            e.ToTable("Reconciliation", "telemetry");
            e.HasKey(r => new { r.CollectorId, r.MinuteStart });
            e.HasQueryFilter(r => r.TenantId == tenant.TenantId);
            e.Property(r => r.MinuteStart).HasColumnType("datetime2(0)");
        });

        mb.Entity<SequenceGap>(e =>
        {
            e.ToTable("SequenceGap", "telemetry");
            e.HasKey(g => g.GapId);
            e.HasQueryFilter(g => g.TenantId == tenant.TenantId);
        });
    }
}
