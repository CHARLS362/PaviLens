using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using Microsoft.Net.Http.Headers;

namespace PaveLens.Api.Endpoints;

internal static class ImageEndpoints
{
    public static RouteGroupBuilder MapImageEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/images/{id:guid}", GetAsync).WithTags("Images").WithSummary("Imagen original (inmutable, con ETag)");
        return api;
    }

    // Content-addressed: the bytes behind an id never change, so clients may cache them forever.
    private static async Task<IResult> GetAsync(
        Guid id, IImageRepository images, IImageStorage storage, HttpContext http, CancellationToken cancellationToken)
    {
        var image = await images.GetAsync(id, cancellationToken) ?? throw new NotFoundException("Imagen", id);
        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        var stream = await storage.OpenReadAsync(image.StorageKey, cancellationToken);
        return TypedResults.Stream(stream, image.ContentType,
            entityTag: new EntityTagHeaderValue($"\"{image.Sha256}\""),
            lastModified: image.CreatedAt);
    }
}
