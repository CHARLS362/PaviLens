using System.Text.RegularExpressions;
using PaveLens.Domain.Common;

namespace PaveLens.Domain.Images;

/// <summary>
/// Metadata of an uploaded photo. The bytes live in object storage, addressed by content hash,
/// so identical uploads are stored once.
/// </summary>
public sealed partial class StoredImage : Entity
{
    private StoredImage()
    {
        // EF Core
    }

    /// <summary>Lower-case hex SHA-256 of the file contents.</summary>
    public string Sha256 { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Opaque key understood by the storage provider.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public string? OriginalFileName { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static StoredImage Create(
        string sha256,
        string contentType,
        long sizeBytes,
        int width,
        int height,
        string storageKey,
        string? originalFileName,
        DateTime nowUtc)
    {
        if (!Sha256Pattern().IsMatch(sha256))
        {
            throw new DomainException("El hash SHA-256 no es válido.");
        }

        if (sizeBytes <= 0 || width <= 0 || height <= 0)
        {
            throw new DomainException("Las dimensiones y el tamaño de la imagen deben ser positivos.");
        }

        return new StoredImage
        {
            Sha256 = sha256,
            ContentType = Guard.NotBlank(contentType, "ContentType", 100),
            SizeBytes = sizeBytes,
            Width = width,
            Height = height,
            StorageKey = Guard.NotBlank(storageKey, "StorageKey", 300),
            OriginalFileName = Guard.Optional(Path.GetFileName(originalFileName), "OriginalFileName", 255),
            CreatedAt = nowUtc
        };
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();
}
