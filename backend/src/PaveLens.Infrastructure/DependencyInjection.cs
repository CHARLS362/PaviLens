using PaveLens.Application.Abstractions;
using PaveLens.Infrastructure.Ai;
using PaveLens.Infrastructure.Ai.Meta;
using PaveLens.Infrastructure.Ai.Simulation;
using PaveLens.Infrastructure.Persistence;
using PaveLens.Infrastructure.Processing;
using PaveLens.Infrastructure.Storage;
using PaveLens.Application.Orchestration;
using PaveLens.Application.Geography;
using PaveLens.Infrastructure.Geography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PaveLens.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        AddPersistence(services, configuration, contentRoot);

        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IImageStorage>(sp =>
            new LocalImageStorage(sp.GetRequiredService<IOptions<StorageOptions>>(), contentRoot));

        // Before the worker: hosted services start in order, and the model selector must finish first.
        AddAi(services, configuration, contentRoot);
        services.AddSingleton<IInspectionQueue, InMemoryInspectionQueue>();
        services.AddHostedService<InspectionWorker>();

        services.AddSingleton<GeographicNetworkCache>();
        services.AddHttpClient<IGeographicNetworkProvider, OverpassNetworkProvider>(http =>
        {
            http.BaseAddress = new Uri("https://overpass-api.de/");
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PaveLens/1.0");
        });

        // Routing: OSRM over OpenStreetMap by default (no key); Google Routes only when its map is used.
        services.AddSingleton<PaveLens.Infrastructure.Maps.OsrmCache>();
        services.AddHttpClient<PaveLens.Infrastructure.Maps.OsrmRouteProvider>((sp, http) =>
        {
            var url = sp.GetRequiredService<IOptions<PaveLens.Application.Maintenance.MapsOptions>>().Value.OsrmBaseUrl;
            http.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PaveLens/1.0 (road maintenance planner)");
        });
        // Google Maps (optional): server key only in configuration (.env); calls capped per day and cached.
        services.AddSingleton<PaveLens.Infrastructure.Maps.GoogleMapsUsage>();
        services.AddHttpClient<PaveLens.Infrastructure.Maps.GoogleRoutesProvider>(http =>
        {
            http.BaseAddress = new Uri("https://routes.googleapis.com/");
            http.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddTransient<PaveLens.Application.Maintenance.IRoadRouteProvider, PaveLens.Infrastructure.Maps.RoadRouteRouter>();
        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();

        var provider = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()?.Provider ?? DatabaseProvider.Sqlite;
        var connectionString = configuration.GetConnectionString("PaveLens")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:PaveLens.");

        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                services.AddDbContext<SqliteAppDbContext>(o => o.UseSqlite(ResolveSqlitePath(connectionString, contentRoot)));
                services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<SqliteAppDbContext>());
                break;
            case DatabaseProvider.Postgres:
                services.AddDbContext<PostgresAppDbContext>(o => o.UseNpgsql(connectionString));
                services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<PostgresAppDbContext>());
                break;
            default:
                throw new InvalidOperationException($"Proveedor de base de datos no soportado: {provider}");
        }

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IInspectionRepository, InspectionRepository>();
        services.AddScoped<IImageRepository, ImageRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IInspectionQueries, InspectionQueries>();

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);
    }

    // A relative SQLite file is anchored to the content root (not the process working directory).
    private static string ResolveSqlitePath(string connectionString, string contentRoot)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
        if (builder.DataSource != ":memory:" && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(contentRoot, builder.DataSource));
            Directory.CreateDirectory(Path.GetDirectoryName(builder.DataSource)!);
        }

        return builder.ToString();
    }

    private static void AddAi(IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName))
            .PostConfigure(o => o.Resolve()).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();

        var options = (configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions()).Resolve();
        if (options.Provider == AiProvider.Simulation)
        {
            services.AddSingleton<SimulationCatalog>();
            services.AddSingleton<IDistressSegmenter, SimulatedSegmenter>();
            services.AddSingleton<IDiagnosticReasoner, SimulatedReasoner>();
            services.AddSingleton<IMaintenanceAssistant, SimulatedMaintenanceAssistant>();
            services.AddSingleton<ITrafficPlanner, SimulatedTrafficPlanner>();
            services.AddSingleton<PaveLens.Application.Maintenance.ICrewDayPlanner, SimulatedCrewPlanner>();
            services.AddSingleton<IInspectionCoordinator, SimulatedInspectionCoordinator>();
            services.AddSingleton<IInspectionAuditor, SimulatedInspectionAuditor>();
            return;
        }

        services.AddSingleton<ISam31Decoder>(_ => new NodeSam31Decoder(Path.GetFullPath(options.Meta.Sam31ParserScript, contentRoot), options.Meta.NodeExecutable));
        var segmentation = options.Meta.SegmentationService == SegmentationService.Meta
            ? services.AddHttpClient<IDistressSegmenter, MetaSam31Segmenter>((sp, http) => Configure(http, sp.GetRequiredService<IOptions<AiOptions>>().Value.Meta.Sam31))
            : options.Meta.SegmentationService == SegmentationService.Roboflow
            ? services.AddHttpClient<IDistressSegmenter, RoboflowSam3Segmenter>((sp, http) =>
                Configure(http, sp.GetRequiredService<IOptions<AiOptions>>().Value.Meta.Segmentation))
            : services.AddHttpClient<IDistressSegmenter, MetaSegmentationClient>((sp, http) =>
                Configure(http, sp.GetRequiredService<IOptions<AiOptions>>().Value.Meta.Segmentation));
        segmentation.AddStandardResilienceHandler(o => ConfigureResilience(o, options.Meta.SegmentationService == SegmentationService.Meta ? options.Meta.Sam31 : options.Meta.Segmentation));

        services.AddHttpClient<MetaChatClient>((sp, http) =>
                Configure(http, sp.GetRequiredService<IOptions<AiOptions>>().Value.Meta.Reasoning))
            .AddStandardResilienceHandler(o => ConfigureResilience(o, options.Meta.Reasoning));

        services.AddHttpClient(MetaModelSelector.ClientName);
        services.AddHostedService<MetaModelSelector>();

        services.AddTransient<IDiagnosticReasoner, MetaReasoner>();
        services.AddTransient<IMaintenanceAssistant, MetaMaintenanceAssistant>();
        services.AddTransient<ITrafficPlanner, MetaTrafficPlanner>();
        services.AddTransient<PaveLens.Application.Maintenance.ICrewDayPlanner, MetaCrewPlanner>();
        services.AddTransient<IInspectionCoordinator, MetaInspectionCoordinator>();
        services.AddTransient<IInspectionAuditor, MetaInspectionAuditor>();
    }

    private static void Configure(HttpClient http, EndpointOptions endpoint)
    {
        // Relative request URIs ("v1/segment", "chat/completions") need a trailing slash on the base.
        var baseUrl = endpoint.BaseUrl!.ToString();
        http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        // The resilience pipeline owns timeouts; the client-level one is only a backstop.
        http.Timeout = Timeout.InfiniteTimeSpan;
    }

    private static void ConfigureResilience(Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions o, EndpointOptions endpoint)
    {
        var attempt = TimeSpan.FromSeconds(endpoint.TimeoutSeconds);
        o.AttemptTimeout.Timeout = attempt;
        o.TotalRequestTimeout.Timeout = attempt * 3;
        o.CircuitBreaker.SamplingDuration = attempt * 2;
        o.Retry.MaxRetryAttempts = 2;
    }
}
