using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Domain.Inspections;
using Microsoft.Extensions.Options;

namespace PaveLens.Infrastructure.Ai.Meta;

/// <summary>
/// Client for the SAM 3 segmentation service. Contract (docs/ai-contracts.md):
/// <c>POST {BaseUrl}/v1/segment</c>, multipart <c>image</c> + <c>concepts</c> (JSON array) + <c>model</c>,
/// returning masks as polygons in normalized coordinates.
/// </summary>
internal sealed class MetaSegmentationClient(HttpClient http, IOptions<AiOptions> options) : IDistressSegmenter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly EndpointOptions _endpoint = options.Value.Meta.Segmentation;

    public AnalysisEngine Engine => AnalysisEngine.Meta;

    public async Task<SegmentationResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var image = new ReadOnlyMemoryContent(request.Image.Bytes);
        image.Headers.ContentType = new MediaTypeHeaderValue(request.Image.ContentType);
        form.Add(image, "image", $"photo{Extension(request.Image.ContentType)}");
        form.Add(new StringContent(JsonSerializer.Serialize(request.Concepts, Json)), "concepts");
        form.Add(new StringContent(_endpoint.Model), "model");

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri("v1/segment", UriKind.Relative)) { Content = form };
        if (!string.IsNullOrWhiteSpace(_endpoint.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _endpoint.ApiKey);
        }

        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ModelUnavailableException($"El servicio de segmentación respondió {(int)response.StatusCode}.");
        }

        SegmentResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<SegmentResponse>(Json, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new ModelOutputException($"La respuesta de segmentación no es JSON válido: {ex.Message}");
        }

        if (body?.Segments is null)
        {
            throw new ModelOutputException("La respuesta de segmentación no contiene 'segments'.");
        }

        var segments = new List<Segment>();
        foreach (var raw in body.Segments)
        {
            if (raw.Polygon is not { Length: >= 3 } || raw.Polygon.Any(p => p.Length != 2))
            {
                continue; // Degenerate masks carry no usable outline.
            }

            var polygon = raw.Polygon.Select(p => new NormalizedPoint(Clamp(p[0]), Clamp(p[1]))).ToArray();
            segments.Add(new Segment(segments.Count, raw.Concept ?? "distress", Math.Clamp(raw.Score, 0, 1), polygon));
        }

        return new SegmentationResult(body.Model ?? _endpoint.Model, segments);
    }

    // Masks touching the border can come back a hair outside [0, 1] after resampling.
    private static double Clamp(double value) =>
        value is >= -0.01 and <= 1.01
            ? Math.Clamp(value, 0, 1)
            : throw new ModelOutputException($"Coordenada de máscara fuera de rango: {value}.");

    private static string Extension(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg"
    };

    private sealed record SegmentResponse(string? Model, List<RawSegment>? Segments);

    private sealed record RawSegment(string? Concept, double Score, double[][]? Polygon);
}
