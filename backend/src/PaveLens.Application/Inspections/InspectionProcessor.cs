using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Application.Geography;
using PaveLens.Application.Geometry;
using PaveLens.Application.Orchestration;
using PaveLens.Application.Pavement;
using PaveLens.Application.Traffic;
using PaveLens.Domain.Common;
using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Inspections;

public sealed partial class InspectionProcessor(
    IInspectionRepository inspections, IImageRepository images, IImageStorage storage,
    IDistressSegmenter segmenter, IDiagnosticReasoner reasoner, IUnitOfWork unitOfWork,
    IOptions<AnalysisOptions> options, IInspectionCoordinator coordinator,
    IInspectionAuditor auditor, RegionalCatalog regions, TrafficPlanService mobility,
    CostEstimator costs, IOptions<OrchestrationOptions> orchestrationOptions,
    TimeProvider clock, ILogger<InspectionProcessor> logger, IGeographicNetworkProvider geography)
{
    public async Task ProcessAsync(Guid inspectionId, CancellationToken cancellationToken)
    {
        var inspection = await inspections.GetAsync(inspectionId, cancellationToken);
        if (inspection is null || inspection.Status != InspectionStatus.Pending) return;
        inspection.BeginProcessing(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        try
        {
            var image = await images.GetAsync(inspection.ImageId, cancellationToken)
                ?? throw new DomainException("La imagen de la inspección no existe.");
            var bytes = await storage.ReadAllBytesAsync(image.StorageKey, cancellationToken);
            var payload = new ImagePayload(bytes, image.ContentType, image.Width, image.Height, image.Sha256);
            var profile = inspection.Trace?.Orchestration is { } selection
                ? RegionalCatalog.Restore(selection.RegionalProfileJson) : regions.Get(null);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(orchestrationOptions.Value.TimeoutSeconds));
            var session = new InspectionToolSession(inspection, image, payload, segmenter, reasoner, auditor,
                mobility, costs, profile, options.Value, unitOfWork, coordinator, clock, geography, orchestrationOptions.Value);
            try
            {
                var task = JsonSerializer.Serialize(new
                {
                    inspection.Title, inspection.Location, inspection.Notes,
                    language = inspection.Language.ToString(), profileId = profile.Id,
                    requiredTools = InspectionTools.Order,
                    objective = "Produce a measured pavement report, assess available mobility evidence, review it and finish."
                });
                var usage = await coordinator.RunAsync(task, InspectionTools.Definitions, session.ExecuteAsync, deadline.Token);
                await session.CompleteAsync(usage, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("La orquestación superó el tiempo máximo configurado.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed item must produce visible failure without stopping the worker.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(ex, inspectionId);
            if (inspection.Trace is { Orchestration: { } run } trace)
                inspection.RecordTrace(new AnalysisTrace(trace.Concepts, trace.SegmentationMs, trace.ReasoningMs,
                    trace.ReasoningAttempts, trace.SkippedBeyondHorizon, run with { Status = "Failed" }));
            var reason = Describe(ex);
            inspection.Fail(reason.Length > 1000 ? reason[..1000] : reason, clock.GetUtcNow().UtcDateTime);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        GroundProjectionException => $"Calibración no válida: {ex.Message}",
        ModelOutputException or DomainException => $"Respuesta del modelo no válida: {ex.Message}",
        ModelUnavailableException or HttpRequestException or TimeoutException => $"Servicio de IA no disponible: {ex.Message}",
        IOException => $"Error de almacenamiento: {ex.Message}",
        _ => "Error interno durante el análisis."
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inspection {InspectionId} failed")]
    private partial void LogFailed(Exception exception, Guid inspectionId);
}
