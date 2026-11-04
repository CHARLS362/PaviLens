using PaveLens.Application.Inspections;
using PaveLens.Domain.Images;
using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Abstractions;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Write-side access to the <see cref="Inspection"/> aggregate.</summary>
public interface IInspectionRepository
{
    Task<Inspection?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListUnfinishedIdsAsync(CancellationToken cancellationToken);
    Task<bool> IsImageReferencedAsync(Guid imageId, Guid excludingInspectionId, CancellationToken cancellationToken);
    void Add(Inspection inspection);
    void Remove(Inspection inspection);
}

public interface IImageRepository
{
    Task<StoredImage?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<StoredImage?> FindBySha256Async(string sha256, CancellationToken cancellationToken);
    void Add(StoredImage image);
    void Remove(StoredImage image);
}

public interface IConversationRepository
{
    /// <summary>Most recent messages, returned in chronological order.</summary>
    Task<IReadOnlyList<ConversationMessage>> ListRecentAsync(
        Guid inspectionId, Guid? distressId, int take, CancellationToken cancellationToken);

    void Add(ConversationMessage message);
}

/// <summary>Read-side queries that project straight to DTOs.</summary>
public interface IInspectionQueries
{
    Task<PagedResult<InspectionSummaryDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<InspectionDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Most recent inspections with what is needed to place them on a map.</summary>
    Task<IReadOnlyList<PaveLens.Application.Maintenance.InspectionLocationRow>> ListLocationsAsync(int max, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationMessageDto>> GetConversationAsync(
        Guid inspectionId, Guid? distressId, CancellationToken cancellationToken);
}
