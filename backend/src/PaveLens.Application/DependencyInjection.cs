using PaveLens.Application.Assistant;
using PaveLens.Application.Inspections;
using PaveLens.Application.Pavement;
using PaveLens.Application.Traffic;
using PaveLens.Application.Orchestration;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PaveLens.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<UploadOptions>().Bind(configuration.GetSection(UploadOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AnalysisOptions>().Bind(configuration.GetSection(AnalysisOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<PavementOptions>().Bind(configuration.GetSection(PavementOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();

        services.AddOptions<TrafficOptions>().Bind(configuration.GetSection(TrafficOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();

        services.AddValidatorsFromAssemblyContaining<CreateInspectionValidator>(includeInternalTypes: true);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<QuantityEstimator>();
        services.AddSingleton<CostEstimator>();
        services.AddOptions<OrchestrationOptions>().Bind(configuration.GetSection(OrchestrationOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RegionalCatalogOptions>().Bind(configuration.GetSection("Regions"));
        services.AddSingleton<RegionalCatalog>();
        services.AddScoped<InspectionService>();
        services.AddScoped<InspectionProcessor>();
        services.AddScoped<AssistantService>();
        services.AddSingleton<TrafficService>();
        services.AddScoped<TrafficPlanService>();
        services.AddOptions<PaveLens.Application.Maintenance.GoogleMapsOptions>().Bind(configuration.GetSection(PaveLens.Application.Maintenance.GoogleMapsOptions.SectionName));
        services.AddOptions<PaveLens.Application.Maintenance.MapsOptions>().Bind(configuration.GetSection(PaveLens.Application.Maintenance.MapsOptions.SectionName));
        services.AddScoped<PaveLens.Application.Maintenance.MaintenanceService>();

        return services;
    }
}
