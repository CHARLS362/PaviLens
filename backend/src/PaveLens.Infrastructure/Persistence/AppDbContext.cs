using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Domain.Images;
using PaveLens.Domain.Inspections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PaveLens.Infrastructure.Persistence;

/// <summary>
/// Provider-neutral model. Each database provider has its own subclass so it can keep its own
/// migration history (see <see cref="SqliteAppDbContext"/> and <see cref="PostgresAppDbContext"/>).
/// </summary>
public abstract class AppDbContext(DbContextOptions options) : DbContext(options), IUnitOfWork
{
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<Distress> Distresses => Set<Distress>();
    public DbSet<RepairStep> RepairSteps => Set<RepairStep>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<StoredImage> Images => Set<StoredImage>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Ids are UUIDv7 assigned by the domain. Telling EF they are never store-generated makes it
        // INSERT children added to a tracked aggregate instead of assuming they exist and issuing UPDATEs.
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            if (entity.FindPrimaryKey()?.Properties is [{ ClrType: var type } key] && type == typeof(Guid))
            {
                key.ValueGenerated = ValueGenerated.Never;
            }
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // DistressQuantities is intentionally null for cracks, which are sealed rather than patched.
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.OptionalDependentWithoutIdentifyingPropertyWarning));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every timestamp is UTC; SQLite drops DateTimeKind, so restore it on read.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();

        // Enums are stored as text: readable in SQL and safe to reorder in code.
        configurationBuilder.Properties<InspectionStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<AnalysisStage>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<DistressType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Severity>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<DepthSource>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<RiskLevel>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<AnalysisEngine>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ReportLanguage>().HaveConversion<string>().HaveMaxLength(8);
        configurationBuilder.Properties<MessageRole>().HaveConversion<string>().HaveMaxLength(20);
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}

public sealed class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : AppDbContext(options);

public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : AppDbContext(options);
