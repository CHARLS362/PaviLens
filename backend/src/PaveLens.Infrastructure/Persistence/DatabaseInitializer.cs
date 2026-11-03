using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Inspections;
using PaveLens.Domain.Inspections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaveLens.Infrastructure.Persistence;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Creates the demo inspections from <see cref="SamplesPath"/> when the database is empty.</summary>
    public bool Enabled { get; set; }

    /// <summary>Folder with manifest.json and the sample photos; relative to the content root.</summary>
    [Required] public string SamplesPath { get; set; } = "../../samples";

    /// <summary>Language of the seeded reports (Es or En).</summary>
    public ReportLanguage Language { get; set; } = ReportLanguage.Es;
}

/// <summary>Runs before the host starts serving: migrations first, then the optional demo seed.</summary>
public static partial class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, string contentRoot, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
        {
            var db = provider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
            LogMigrated(logger, db.Database.ProviderName);
        }

        var seed = provider.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (seed.Enabled)
        {
            await SeedDemoAsync(provider, Path.GetFullPath(Path.Combine(contentRoot, seed.SamplesPath)), seed.Language, logger, cancellationToken);
        }
    }

    // Demo inspections go through the real use case (validation, hashing, storage, queue), not raw inserts.
    private static async Task SeedDemoAsync(
        IServiceProvider provider, string samplesPath, ReportLanguage language, ILogger logger, CancellationToken cancellationToken)
    {
        var service = provider.GetRequiredService<InspectionService>();
        var existing = await service.ListAsync(1, 1, cancellationToken);
        if (existing.TotalCount > 0)
        {
            return;
        }

        var manifestPath = Path.Combine(samplesPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            LogManifestMissing(logger, manifestPath);
            return;
        }

        await using var manifestStream = File.OpenRead(manifestPath);
        var samples = await JsonSerializer.DeserializeAsync<List<SampleEntry>>(manifestStream, JsonSerializerOptions.Web, cancellationToken) ?? [];

        foreach (var sample in samples)
        {
            await using var image = File.OpenRead(Path.Combine(samplesPath, sample.File));
            await service.CreateAsync(
                new CreateInspectionCommand(
                    sample.Title, sample.Notes, sample.Location, sample.RoadSegmentId,
                    sample.ReferenceWidthM, sample.Calibration, image, sample.File, language),
                cancellationToken);
        }

        LogSeeded(logger, samples.Count);
    }

    private sealed record SampleEntry(
        string File, string Title, string? Notes, string? Location, string? RoadSegmentId,
        double? ReferenceWidthM, CalibrationInput? Calibration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrated ({Provider})")]
    private static partial void LogMigrated(ILogger logger, string? provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seed enabled but {Path} was not found")]
    private static partial void LogManifestMissing(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} demo inspections")]
    private static partial void LogSeeded(ILogger logger, int count);
}
