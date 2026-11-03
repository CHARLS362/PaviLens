using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaveLens.Infrastructure.Persistence;

public enum DatabaseProvider
{
    Sqlite,
    Postgres
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Chosen explicitly; there is no silent fallback between providers.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Applies pending migrations at startup. Production deployments should run them as a release step.</summary>
    public bool MigrateOnStartup { get; set; }
}

// Design-time factories used by `dotnet ef migrations add`; they never open a connection.
internal sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

internal sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql("Host=localhost;Database=pavelens").Options);
}
