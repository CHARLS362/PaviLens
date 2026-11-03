using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using PaveLens.Api.Endpoints;
using PaveLens.Api.Infrastructure;
using PaveLens.Application;
using PaveLens.Application.Inspections;
using PaveLens.Infrastructure;
using PaveLens.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var dotEnvPath = DotEnvFile.AddTo(configuration, builder.Environment);

builder.Services.AddApplication(configuration);
builder.Services.AddInfrastructure(configuration, builder.Environment.ContentRootPath);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

// The multipart limit sits just above the image limit; the image itself is checked again in the use case.
var maxImageBytes = configuration.GetSection(UploadOptions.SectionName).Get<UploadOptions>()?.MaxImageBytes ?? 10 * 1024 * 1024;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxImageBytes + 64 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxImageBytes + 128 * 1024);

builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Model calls are the expensive part: limit them per client address.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitPolicies.Ai, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = configuration.GetValue("RateLimiting:AiRequestsPerMinute", 20),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

StartupLog.Configuration(app.Logger, dotEnvPath ?? "(no .env file)", configuration["Ai:Provider"] ?? "Simulation");

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

var api = app.MapGroup(ApiRoutes.Prefix);
api.MapInspectionEndpoints();
api.MapAssistantEndpoints();
api.MapImageEndpoints();
api.MapTrafficEndpoints();
api.MapAiStatusEndpoints();
api.MapOrchestrationEndpoints();

await DatabaseInitializer.InitializeAsync(app.Services, app.Environment.ContentRootPath);
await app.RunAsync();

public partial class Program;
