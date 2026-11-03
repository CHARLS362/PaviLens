using System.Security.Cryptography;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Application.Images;
using PaveLens.Application.Pavement;
using PaveLens.Domain.Common;
using PaveLens.Domain.Images;
using PaveLens.Domain.Inspections;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaveLens.Application.Inspections;

/// <summary>Write-side use cases for inspections.</summary>
public sealed partial class InspectionService(
    IInspectionRepository inspections,
    IImageRepository images,
    IImageStorage storage,
    IInspectionQueue queue,
    IInspectionQueries queries,
    IUnitOfWork unitOfWork,
    IValidator<CreateInspectionCommand> validator,
    IValidator<SetDepthCommand> depthValidator,
    QuantityEstimator quantityEstimator,
    CostEstimator costEstimator,
    IOptions<UploadOptions> uploadOptions,
    TimeProvider clock,
    ILogger<InspectionService> logger)
{
    private readonly UploadOptions _upload = uploadOptions.Value;

    public async Task<InspectionDetailDto> CreateAsync(CreateInspectionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var bytes = await ReadLimitedAsync(command.Image, cancellationToken);
        var info = ImageInspector.Inspect(bytes)
            ?? throw Invalid("image", "El archivo no es una imagen JPEG, PNG o WebP válida.");
        if (Math.Min(info.Width, info.Height) < _upload.MinImageDimension ||
            Math.Max(info.Width, info.Height) > _upload.MaxImageDimension)
        {
            throw Invalid("image",
                $"La imagen debe medir entre {_upload.MinImageDimension} y {_upload.MaxImageDimension} píxeles por lado.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

        // Content-addressed: identical photos share one stored file and one metadata row.
        var image = await images.FindBySha256Async(sha256, cancellationToken);
        if (image is null)
        {
            // Written before the database commit; if the commit fails the file is an
            // unreferenced blob with a deterministic name, reused on the next upload.
            var key = await storage.SaveAsync(bytes, sha256, info.Extension, cancellationToken);
            image = StoredImage.Create(sha256, info.ContentType, bytes.Length, info.Width, info.Height, key, command.FileName, now);
            images.Add(image);
        }

        var calibration = command.Calibration is { } c
            ? new GroundCalibration(c.Corners.Select(p => new NormalizedPoint(p[0], p[1])).ToArray(), c.WidthM, c.LengthM)
            : null;
        var inspection = Inspection.Create(
            command.Title, command.Notes, command.Location, command.RoadSegmentId,
            image.Id, command.ReferenceWidthM, calibration, command.Language, now);
        inspections.Add(inspection);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await queue.EnqueueAsync(inspection.Id, cancellationToken);
        LogQueued(inspection.Id, sha256[..12]);

        return InspectionMapper.ToDetail(inspection, image);
    }

    public Task<PagedResult<InspectionSummaryDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        queries.ListAsync(Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), cancellationToken);

    public async Task<InspectionDetailDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await queries.GetAsync(id, cancellationToken) ?? throw new NotFoundException("Inspección", id);

    public async Task<InspectionDetailDto> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var inspection = await LoadAsync(id, cancellationToken);
        inspection.Retry();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<RepairStepDto> SetStepCompletionAsync(Guid id, Guid stepId, bool completed, CancellationToken cancellationToken)
    {
        var inspection = await LoadAsync(id, cancellationToken);
        if (inspection.RepairSteps.All(s => s.Id != stepId))
        {
            throw new NotFoundException("Paso", stepId);
        }

        inspection.SetStepCompletion(stepId, completed, clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return InspectionMapper.ToDto(inspection.RepairSteps.First(s => s.Id == stepId));
    }

    /// <summary>
    /// Stores a field-measured depth: recomputes the patch quantities, the ASTM pothole severity
    /// and the PCI that depends on it.
    /// </summary>
    public async Task<DistressDto> SetDepthAsync(SetDepthCommand command, CancellationToken cancellationToken)
    {
        await depthValidator.ValidateAndThrowAsync(command, cancellationToken);
        var inspection = await LoadAsync(command.InspectionId, cancellationToken);
        var distress = inspection.Distresses.FirstOrDefault(d => d.Id == command.DistressId)
            ?? throw new NotFoundException("Deterioro", command.DistressId);

        var severity = distress.Type == DistressType.Pothole
            ? QuantityEstimator.PotholeSeverity(command.DepthM, (distress.Measurement.LengthM + distress.Measurement.WidthM) / 2)
            : distress.Severity;
        var quantities = quantityEstimator.Estimate(distress.Measurement, command.DepthM, DepthSource.Measured);

        // The new severity must count in the PCI before the aggregate accepts it.
        var inputs = inspection.Distresses.Select(d => new PciInput(
            d.Type, d.Id == distress.Id ? severity : d.Severity, d.Measurement.AreaM2, d.Measurement.LengthM));
        var pci = PciCalculator.Compute(inputs, inspection.SurveyedAreaM2 ?? 1).Pci;

        inspection.UpdateDistressDepth(distress.Id, severity, quantities, pci);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return InspectionMapper.ToDto(distress);
    }

    public async Task<BudgetDto> GetBudgetAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await GetAsync(id, cancellationToken);
        if (detail.Status != InspectionStatus.Completed)
        {
            throw new DomainException("El presupuesto se calcula cuando la inspección termina.");
        }

        return costEstimator.Estimate(detail);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var inspection = await LoadAsync(id, cancellationToken);
        if (inspection.Status == InspectionStatus.Processing)
        {
            throw new DomainException("No se puede borrar una inspección mientras se analiza.");
        }

        inspections.Remove(inspection);

        StoredImage? orphan = null;
        if (!await inspections.IsImageReferencedAsync(inspection.ImageId, inspection.Id, cancellationToken))
        {
            orphan = await images.GetAsync(inspection.ImageId, cancellationToken);
            if (orphan is not null)
            {
                images.Remove(orphan);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (orphan is not null)
        {
            try
            {
                await storage.DeleteAsync(orphan.StorageKey, cancellationToken);
            }
            catch (IOException ex)
            {
                // The row is gone; a leftover file is harmless and can be swept later.
                LogBlobDeleteFailed(ex, orphan.StorageKey);
            }
        }
    }

    private async Task<Inspection> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await inspections.GetAsync(id, cancellationToken) ?? throw new NotFoundException("Inspección", id);

    private async Task<byte[]> ReadLimitedAsync(Stream source, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > _upload.MaxImageBytes)
            {
                throw Invalid("image", $"La imagen supera el máximo de {_upload.MaxImageBytes / (1024 * 1024)} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw Invalid("image", "La imagen está vacía.");
        }

        return buffer.ToArray();
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspection {InspectionId} queued (image {ImageHash})")]
    private partial void LogQueued(Guid inspectionId, string imageHash);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete stored image {StorageKey}")]
    private partial void LogBlobDeleteFailed(Exception exception, string storageKey);
}
