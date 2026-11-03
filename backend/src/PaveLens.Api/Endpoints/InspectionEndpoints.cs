using PaveLens.Domain.Inspections;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using PaveLens.Application.Inspections;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace PaveLens.Api.Endpoints;

internal static class RateLimitPolicies
{
    public const string Ai = "ai";
}

internal static class InspectionEndpoints
{
    public static RouteGroupBuilder MapInspectionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/inspections").WithTags("Inspections");

        group.MapPost("/", CreateAsync)
            .DisableAntiforgery() // Stateless API without cookie auth; CSRF tokens do not apply.
            .RequireRateLimiting(RateLimitPolicies.Ai)
            .WithSummary("Sube una foto de pavimento y encola su análisis");

        group.MapGet("/", ListAsync).WithSummary("Lista paginada de inspecciones");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetInspection").WithSummary("Detalle con deterioros, medidas, cubicaje, PCI y pasos");
        group.MapDelete("/{id:guid}", DeleteAsync).WithSummary("Elimina la inspección y su imagen si queda huérfana");
        group.MapPost("/{id:guid}/retry", RetryAsync)
            .RequireRateLimiting(RateLimitPolicies.Ai)
            .WithSummary("Reintenta una inspección fallida");
        group.MapGet("/{id:guid}/budget", GetBudgetAsync).WithSummary("Presupuesto (APU) del bacheo y sellado");
        group.MapPut("/{id:guid}/distresses/{distressId:guid}/depth", SetDepthAsync)
            .WithSummary("Registra la profundidad medida en campo y recalcula cubicaje, severidad y PCI");
        group.MapPut("/{id:guid}/steps/{stepId:guid}/completion", SetStepCompletionAsync)
            .WithSummary("Marca o desmarca un paso de mantenimiento");

        return api;
    }

    private static async Task<CreatedAtRoute<InspectionDetailDto>> CreateAsync(
        IFormFile image,
        [FromForm] string title,
        [FromForm] string? notes,
        [FromForm] string? location,
        [FromForm] string? roadSegmentId,
        [FromForm] double? referenceWidthM,
        [FromForm] string? calibration,
        [FromForm] string? language,
        InspectionService service,
        CancellationToken cancellationToken)
    {
        await using var stream = image.OpenReadStream();
        var created = await service.CreateAsync(
            new CreateInspectionCommand(
                title, notes, location, roadSegmentId, referenceWidthM, ParseCalibration(calibration), stream, image.FileName,
                ParseLanguage(language)),
            cancellationToken);
        return TypedResults.CreatedAtRoute(created, "GetInspection", new { id = created.Id });
    }

    private static async Task<Ok<PagedResult<InspectionSummaryDto>>> ListAsync(
        InspectionService service, CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
        TypedResults.Ok(await service.ListAsync(Math.Max(1, page), Math.Clamp(pageSize, 1, 100), cancellationToken));

    private static async Task<Ok<InspectionDetailDto>> GetAsync(Guid id, InspectionService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAsync(id, cancellationToken));

    private static async Task<NoContent> DeleteAsync(Guid id, InspectionService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Accepted<InspectionDetailDto>> RetryAsync(Guid id, InspectionService service, CancellationToken cancellationToken) =>
        TypedResults.Accepted((string?)null, await service.RetryAsync(id, cancellationToken));

    private static async Task<Ok<BudgetDto>> GetBudgetAsync(Guid id, InspectionService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetBudgetAsync(id, cancellationToken));

    private static async Task<Ok<DistressDto>> SetDepthAsync(
        Guid id, Guid distressId, DepthRequest request, InspectionService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.SetDepthAsync(new SetDepthCommand(id, distressId, request.DepthM), cancellationToken));

    private static async Task<Ok<RepairStepDto>> SetStepCompletionAsync(
        Guid id, Guid stepId, StepCompletionRequest request, InspectionService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.SetStepCompletionAsync(id, stepId, request.Completed, cancellationToken));

    /// <summary>Multipart forms carry the calibration as one JSON field: {"corners":[[x,y]×4],"widthM":…,"lengthM":…}.</summary>
    private static CalibrationInput? ParseCalibration(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CalibrationInput>(json, JsonSerializerOptions.Web)
                ?? throw new JsonException("vacío");
        }
        catch (JsonException)
        {
            throw new ValidationException(
                [new ValidationFailure("calibration", "La calibración debe ser JSON: {\"corners\":[[x,y],…×4],\"widthM\":n,\"lengthM\":n}.")]);
        }
    }

    /// <summary>"es" (default) or "en": the language the models write the report in.</summary>
    private static ReportLanguage ParseLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? ReportLanguage.Es
        : Enum.TryParse<ReportLanguage>(language, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed
        : throw new ValidationException([new ValidationFailure("language", "El idioma debe ser 'es' o 'en'.")]);

    internal sealed record StepCompletionRequest(bool Completed);

    internal sealed record DepthRequest(double DepthM);
}
