using PaveLens.Application.Abstractions;
using PaveLens.Application.Inspections;
using PaveLens.Domain.Images;
using PaveLens.Domain.Inspections;
using Microsoft.EntityFrameworkCore;

namespace PaveLens.Infrastructure.Persistence;

internal sealed class InspectionRepository(AppDbContext db) : IInspectionRepository
{
    public Task<Inspection?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Inspections
            .Include(i => i.Distresses)
            .Include(i => i.RepairSteps)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListUnfinishedIdsAsync(CancellationToken cancellationToken) =>
        await db.Inspections
            .Where(i => i.Status == InspectionStatus.Pending || i.Status == InspectionStatus.Processing)
            .OrderBy(i => i.CreatedAt)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> IsImageReferencedAsync(Guid imageId, Guid excludingInspectionId, CancellationToken cancellationToken) =>
        db.Inspections.AnyAsync(i => i.ImageId == imageId && i.Id != excludingInspectionId, cancellationToken);

    public void Add(Inspection inspection) => db.Inspections.Add(inspection);

    public void Remove(Inspection inspection) => db.Inspections.Remove(inspection);
}

internal sealed class ImageRepository(AppDbContext db) : IImageRepository
{
    public Task<StoredImage?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Images.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<StoredImage?> FindBySha256Async(string sha256, CancellationToken cancellationToken) =>
        db.Images.Local.FirstOrDefault(i => i.Sha256 == sha256)
        ?? await db.Images.FirstOrDefaultAsync(i => i.Sha256 == sha256, cancellationToken);

    public void Add(StoredImage image) => db.Images.Add(image);

    public void Remove(StoredImage image) => db.Images.Remove(image);
}

internal sealed class ConversationRepository(AppDbContext db) : IConversationRepository
{
    public async Task<IReadOnlyList<ConversationMessage>> ListRecentAsync(
        Guid inspectionId, Guid? distressId, int take, CancellationToken cancellationToken)
    {
        var recent = await db.ConversationMessages
            .AsNoTracking()
            .Where(m => m.InspectionId == inspectionId && m.DistressId == distressId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
        recent.Reverse();
        return recent;
    }

    public void Add(ConversationMessage message) => db.ConversationMessages.Add(message);
}

internal sealed class InspectionQueries(AppDbContext db) : IInspectionQueries
{
    public async Task<PagedResult<InspectionSummaryDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await db.Inspections.CountAsync(cancellationToken);
        var rows = await db.Inspections
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.Title,
                i.Location,
                i.Status,
                i.Engine,
                i.Language,
                i.ImageId,
                DistressCount = i.Distresses.Count,
                PotholeCount = i.Distresses.Count(d => d.Type == DistressType.Pothole),
                i.Pci,
                i.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new InspectionSummaryDto(
                r.Id, r.Title, r.Location, r.Status, r.Engine, r.Language, ApiRoutes.Image(r.ImageId),
                r.DistressCount, r.PotholeCount, r.Pci, r.CreatedAt))
            .ToList();
        return new PagedResult<InspectionSummaryDto>(items, page, pageSize, total);
    }

    public async Task<InspectionDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var inspection = await db.Inspections
            .AsNoTracking()
            .Include(i => i.Distresses)
            .Include(i => i.RepairSteps)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (inspection is null)
        {
            return null;
        }

        var image = await db.Images.AsNoTracking().FirstAsync(i => i.Id == inspection.ImageId, cancellationToken);
        return InspectionMapper.ToDetail(inspection, image);
    }

    public async Task<IReadOnlyList<ConversationMessageDto>> GetConversationAsync(
        Guid inspectionId, Guid? distressId, CancellationToken cancellationToken)
    {
        var query = db.ConversationMessages.AsNoTracking().Where(m => m.InspectionId == inspectionId);
        if (distressId is not null)
        {
            query = query.Where(m => m.DistressId == distressId);
        }

        var messages = await query.OrderBy(m => m.CreatedAt).ToListAsync(cancellationToken);
        return messages.Select(InspectionMapper.ToDto).ToList();
    }
}
