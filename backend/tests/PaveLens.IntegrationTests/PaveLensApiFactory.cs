using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Inspections;
using PaveLens.Application.Orchestration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PaveLens.IntegrationTests;

/// <summary>Real API host with an isolated SQLite file and image folder per factory instance.</summary>
public class PaveLensApiFactory : WebApplicationFactory<Program>
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pavelens-tests", Guid.NewGuid().ToString("N"));

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Set before the first request to replace the segmentation model.</summary>
    public IDistressSegmenter? Segmenter { get; init; }
    public IDiagnosticReasoner? Reasoner { get; init; }
    public IInspectionCoordinator? Coordinator { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_root);
        builder.UseEnvironment("Testing");

        // UseSetting is visible to Program.cs while services are registered; ConfigureAppConfiguration is not.
        builder.UseSetting("ConnectionStrings:PaveLens", $"Data Source={Path.Combine(_root, "pavelens.db")}");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Storage:LocalRootPath", Path.Combine(_root, "images"));
        builder.UseSetting("Seed:Enabled", "false");
        builder.UseSetting("Ai:Provider", "Simulation");
        builder.UseSetting("RateLimiting:AiRequestsPerMinute", "1000");

        if (Segmenter is not null || Reasoner is not null || Coordinator is not null)
        {
            builder.ConfigureServices(services =>
            {
                if (Segmenter is { } segmenter) { services.RemoveAll<IDistressSegmenter>(); services.AddSingleton(segmenter); }
                if (Reasoner is { } reasoner) { services.RemoveAll<IDiagnosticReasoner>(); services.AddSingleton(reasoner); }
                if (Coordinator is { } coordinator) { services.RemoveAll<IInspectionCoordinator>(); services.AddSingleton(coordinator); }
            });
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the OS temp folder is cleaned eventually.
        }
    }

    public static MultipartFormDataContent Upload(
        string sample, string title = "Tramo de prueba", double? referenceWidthM = null, string? calibration = null, string? roadSegmentId = null,
        string? language = null)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", sample));
        var image = new ByteArrayContent(bytes);
        image.Headers.ContentType = new("image/jpeg");

        var form = new MultipartFormDataContent { { new StringContent(title), "title" }, { image, "image", sample } };
        if (referenceWidthM is { } width)
        {
            form.Add(new StringContent(width.ToString(System.Globalization.CultureInfo.InvariantCulture)), "referenceWidthM");
        }

        if (calibration is not null) form.Add(new StringContent(calibration), "calibration");
        if (roadSegmentId is not null) form.Add(new StringContent(roadSegmentId), "roadSegmentId");
        if (language is not null) form.Add(new StringContent(language), "language");
        return form;
    }

    public static async Task<InspectionDetailDto> WaitForAnalysisAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 100; i++)
        {
            var detail = await client.GetFromJsonAsync<InspectionDetailDto>($"/api/v1/inspections/{id}", Json, cancellationToken);
            if (detail!.Status is PaveLens.Domain.Inspections.InspectionStatus.Completed or PaveLens.Domain.Inspections.InspectionStatus.Failed)
            {
                return detail;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException($"Inspection {id} did not finish.");
    }
}
