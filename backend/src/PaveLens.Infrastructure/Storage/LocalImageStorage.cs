using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PaveLens.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace PaveLens.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Root folder for image blobs; relative paths resolve against the app content root.</summary>
    [Required] public string LocalRootPath { get; set; } = "App_Data/images";
}

/// <summary>
/// Content-addressed blob store on local disk: <c>ab/cd/abcd…ef.jpg</c>.
/// Writes go to a temporary file first and are moved into place, so readers never see partial files.
/// Swap for an S3 / Azure Blob implementation of <see cref="IImageStorage"/> in production.
/// </summary>
internal sealed partial class LocalImageStorage : IImageStorage
{
    private readonly string _root;

    public LocalImageStorage(IOptions<StorageOptions> options, string contentRoot)
    {
        _root = Path.GetFullPath(Path.Combine(contentRoot, options.Value.LocalRootPath));
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(ReadOnlyMemory<byte> content, string sha256, string extension, CancellationToken cancellationToken)
    {
        var key = $"{sha256[..2]}/{sha256[2..4]}/{sha256}{extension}";
        var path = Resolve(key);
        if (File.Exists(path))
        {
            return key;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await stream.WriteAsync(content, cancellationToken);
            }

            File.Move(temp, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // A concurrent upload of the same content won the race; the bytes are identical.
        }
        finally
        {
            File.Delete(temp);
        }

        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new FileStream(Resolve(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));

    public Task<byte[]> ReadAllBytesAsync(string key, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(Resolve(key), cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        File.Delete(Resolve(key));
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        // Keys are generated here, but validate anyway so a tampered row cannot escape the root.
        if (!KeyPattern().IsMatch(key))
        {
            throw new ArgumentException($"Clave de almacenamiento no válida: {key}", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La clave sale del directorio de almacenamiento.", nameof(key));
        }

        return path;
    }

    [GeneratedRegex(@"^[0-9a-f]{2}/[0-9a-f]{2}/[0-9a-f]{64}\.(jpg|png|webp)$")]
    private static partial Regex KeyPattern();
}
