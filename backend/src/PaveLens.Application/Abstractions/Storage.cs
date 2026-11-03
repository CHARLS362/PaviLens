namespace PaveLens.Application.Abstractions;

/// <summary>Binary storage for uploaded images (local disk, S3, Azure Blob…).</summary>
public interface IImageStorage
{
    /// <summary>Stores the bytes and returns the key used to read them back. Idempotent per hash.</summary>
    Task<string> SaveAsync(ReadOnlyMemory<byte> content, string sha256, string extension, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task<byte[]> ReadAllBytesAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Hands inspection ids to the background analysis worker.</summary>
public interface IInspectionQueue
{
    ValueTask EnqueueAsync(Guid inspectionId, CancellationToken cancellationToken);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}
