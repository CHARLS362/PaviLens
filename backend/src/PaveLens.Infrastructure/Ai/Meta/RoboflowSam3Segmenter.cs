using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Domain.Inspections;
using Microsoft.Extensions.Options;

namespace PaveLens.Infrastructure.Ai.Meta;

/// <summary>
/// Meta SAM 3 through Roboflow's serverless API: <c>POST {BaseUrl}sam3/concept_segment</c> with one text prompt per
/// concept. Every instance comes back as polygons in image pixels; they are normalized, split into connected parts and
/// de-duplicated across concepts before entering the pipeline.
/// </summary>
internal sealed class RoboflowSam3Segmenter(HttpClient http, IOptions<AiOptions> options) : IDistressSegmenter
{
    // The endpoint accepts at most 16 prompts per request.
    private const int MaxPromptsPerRequest = 16;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly EndpointOptions _endpoint = options.Value.Meta.Segmentation;
    private readonly double _threshold = options.Value.Meta.SegmentationScoreThreshold;

    public AnalysisEngine Engine => AnalysisEngine.Meta;

    public async Task<SegmentationResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken)
    {
        var image = Convert.ToBase64String(request.Image.Bytes.Span);
        var instances = new List<Sam3Instances.Instance>();

        foreach (var chunk in request.Concepts.Chunk(MaxPromptsPerRequest))
        {
            var payload = new JsonObject
            {
                ["image"] = new JsonObject { ["type"] = "base64", ["value"] = image },
                ["prompts"] = new JsonArray(chunk.Select(c => (JsonNode)new JsonObject { ["type"] = "text", ["text"] = c }).ToArray()),
                ["output_prob_thresh"] = _threshold,
                ["format"] = "polygon"
            };

            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri("sam3/concept_segment", UriKind.Relative))
            {
                Content = JsonContent.Create(payload, options: Json)
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _endpoint.ApiKey);

            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ModelUnavailableException($"SAM 3 (Roboflow) respondió {(int)response.StatusCode}.");
            }

            JsonNode? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<JsonNode>(Json, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new ModelOutputException($"La respuesta de SAM 3 no es JSON válido: {ex.Message}");
            }

            instances.AddRange(Sam3Instances.Parse(body, chunk, request.Image.Width, request.Image.Height));
        }

        var segments = Sam3Instances.Deduplicate(instances)
            .Select((s, i) => new Segment(i, s.Concept, s.Score, s.Polygon, s.Holes))
            .ToList();
        return new SegmentationResult(_endpoint.Model, segments);
    }
}

/// <summary>Parsing and clean-up of SAM 3 concept-segmentation output. Pure, so it is unit-tested directly.</summary>
internal static class Sam3Instances
{
    internal sealed record Instance(string Concept, double Score, NormalizedPoint[] Polygon, NormalizedPoint[][]? Holes = null);

    /// <summary>Parts smaller than this many square pixels are speckle, not distresses.</summary>
    internal const double MinAreaPx = 16;

    /// <summary>Two masks of different concepts overlapping more than this are the same object labelled twice.</summary>
    internal const double DuplicateIou = 0.7;

    /// <summary>
    /// A mask covering more of the frame than this is the pavement itself ("cracked asphalt" on a worn street), not one
    /// distress: it would count every pothole and crack inside it twice. Those are segmented on their own.
    /// </summary>
    internal const double MaxFrameFraction = 0.6;

    public static IEnumerable<Instance> Parse(JsonNode? body, IReadOnlyList<string> concepts, int width, int height)
    {
        if (body?["prompt_results"] is not JsonArray results)
        {
            throw new ModelOutputException("La respuesta de SAM 3 no contiene 'prompt_results'.");
        }

        for (var p = 0; p < results.Count; p++)
        {
            var result = results[p];
            var concept = result?["echo"]?["text"]?.GetValue<string>() ?? (p < concepts.Count ? concepts[p] : "distress");
            if (result?["predictions"] is not JsonArray predictions)
            {
                continue;
            }

            foreach (var prediction in predictions)
            {
                var score = Math.Clamp(prediction?["confidence"]?.GetValue<double>() ?? 0, 0, 1);
                var polygons = ReadPolygons(prediction?["masks"]);

                // A prediction may hold several rings: separate parts, and holes whose area is subtracted when measured.
                var rings = polygons.Where(r => r.Length >= 3 && Math.Abs(SignedArea(r)) >= MinAreaPx);
                foreach (var (outer, holes) in Nest(rings))
                {
                    yield return new Instance(concept, score, NormalizeRing(outer, width, height),
                        holes.Count > 0 ? holes.Select(h => NormalizeRing(h, width, height)).ToArray() : null);
                }
            }
        }
    }

    /// <summary>
    /// Groups the pixel rings of one mask by nesting depth, largest first: a ring inside an even number of others is
    /// an outline, inside an odd number it is a hole of its innermost container. An island inside a hole is a new part.
    /// </summary>
    public static List<((double X, double Y)[] Outer, List<(double X, double Y)[]> Holes)> Nest(IEnumerable<(double X, double Y)[]> rings)
    {
        var parts = new List<((double X, double Y)[] Outer, List<(double X, double Y)[]> Holes)>();
        var placed = new List<((double X, double Y)[] Ring, int Depth, int Part)>();
        foreach (var ring in rings.OrderByDescending(r => Math.Abs(SignedArea(r))))
        {
            // Placed rings are larger, so the last one containing this ring is its innermost container.
            var parent = placed.FindLastIndex(p => Contains(p.Ring, ring[0].X, ring[0].Y));
            var depth = parent < 0 ? 0 : placed[parent].Depth + 1;
            if (depth % 2 == 0)
            {
                parts.Add((ring, []));
                placed.Add((ring, depth, parts.Count - 1));
            }
            else
            {
                parts[placed[parent].Part].Holes.Add(ring);
                placed.Add((ring, depth, placed[parent].Part));
            }
        }

        return parts;
    }

    /// <summary>
    /// SAM 3 answers each prompt independently, so one pothole can come back as "pothole" and as "cracked asphalt".
    /// Drops whole-surface masks (<see cref="MaxFrameFraction"/>), then keeps the higher-scoring mask when two masks of
    /// different concepts have IoU above <see cref="DuplicateIou"/>.
    /// Containment alone is not a duplicate: a pothole inside an alligator-cracked area is two distresses.
    /// </summary>
    public static IReadOnlyList<Instance> Deduplicate(IReadOnlyList<Instance> instances)
    {
        instances = instances.Where(i => NetArea(i) <= MaxFrameFraction).ToList();
        var masks = instances.Select(i => Raster.Of(i.Polygon, i.Holes)).ToArray();
        var kept = new List<int>();
        foreach (var i in Enumerable.Range(0, instances.Count).OrderByDescending(i => instances[i].Score))
        {
            var duplicate = kept.Any(k => instances[k].Concept != instances[i].Concept && Raster.Iou(masks[k], masks[i]) > DuplicateIou);
            if (!duplicate)
            {
                kept.Add(i);
            }
        }

        // Keep the original order so codes follow the prompt order.
        return kept.Order().Select(i => instances[i]).ToList();
    }

    private static List<(double X, double Y)[]> ReadPolygons(JsonNode? masks)
    {
        var polygons = new List<(double X, double Y)[]>();
        if (masks is not JsonArray array || array.Count == 0)
        {
            return polygons;
        }

        // Either a single ring [[x,y],…] or a list of rings [[[x,y],…],…].
        if (IsPoint(array[0]))
        {
            polygons.Add(ReadRing(array));
            return polygons;
        }

        foreach (var ring in array)
        {
            if (ring is JsonArray points)
            {
                polygons.Add(ReadRing(points));
            }
        }

        return polygons;
    }

    private static bool IsPoint(JsonNode? node) =>
        node is JsonObject || (node is JsonArray a && a.Count == 2 && a[0] is JsonValue);

    private static (double X, double Y)[] ReadRing(JsonArray points)
    {
        var ring = new (double X, double Y)[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            ring[i] = points[i] switch
            {
                JsonArray a when a.Count >= 2 => (a[0]!.GetValue<double>(), a[1]!.GetValue<double>()),
                JsonObject o => (o["x"]!.GetValue<double>(), o["y"]!.GetValue<double>()),
                _ => throw new ModelOutputException("Vértice de máscara con formato inesperado.")
            };
        }

        return ring;
    }

    private static NormalizedPoint[] NormalizeRing((double X, double Y)[] ring, int width, int height) =>
        ring.Select(v => new NormalizedPoint(Normalize(v.X, width), Normalize(v.Y, height))).ToArray();

    // Polygon vertices are pixel coordinates along pixel edges, so dividing by the size maps them onto [0, 1].
    private static double Normalize(double value, int size)
    {
        var n = value / size;
        return n is >= -0.01 and <= 1.01
            ? Math.Clamp(n, 0, 1)
            : throw new ModelOutputException($"Coordenada de máscara fuera de la imagen: {value} de {size} px.");
    }

    /// <summary>Area in normalized image units (1 = the whole frame), holes subtracted.</summary>
    internal static double NetArea(Instance instance)
    {
        static double Area(NormalizedPoint[] ring) => Math.Abs(SignedArea(ring.Select(p => (p.X, p.Y)).ToArray()));
        return Area(instance.Polygon) - (instance.Holes?.Sum(Area) ?? 0);
    }

    internal static double SignedArea((double X, double Y)[] ring)
    {
        var sum = 0.0;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            sum += (ring[j].X * ring[i].Y) - (ring[i].X * ring[j].Y);
        }

        return sum / 2;
    }

    private static bool Contains((double X, double Y)[] ring, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            if ((ring[i].Y > y) != (ring[j].Y > y) &&
                x < ((ring[j].X - ring[i].X) * (y - ring[i].Y) / (ring[j].Y - ring[i].Y)) + ring[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Coarse scanline raster in normalized space, enough to compare masks by IoU.</summary>
    private static class Raster
    {
        private const int Size = 256;

        // Even-odd fill over the outline and its holes, so a hole is left empty.
        public static bool[] Of(NormalizedPoint[] polygon, NormalizedPoint[][]? holes)
        {
            var cells = new bool[Size * Size];
            var xs = new List<double>();
            NormalizedPoint[][] rings = [polygon, .. holes ?? []];
            for (var row = 0; row < Size; row++)
            {
                var y = (row + 0.5) / Size;
                xs.Clear();
                foreach (var ring in rings)
                {
                    for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                    {
                        var (a, b) = (ring[i], ring[j]);
                        if ((a.Y > y) != (b.Y > y))
                        {
                            xs.Add(a.X + ((y - a.Y) * (b.X - a.X) / (b.Y - a.Y)));
                        }
                    }
                }

                xs.Sort();
                for (var k = 0; k + 1 < xs.Count; k += 2)
                {
                    var from = Math.Max(0, (int)Math.Ceiling((xs[k] * Size) - 0.5));
                    var to = Math.Min(Size - 1, (int)Math.Floor((xs[k + 1] * Size) - 0.5));
                    for (var col = from; col <= to; col++)
                    {
                        cells[(row * Size) + col] = true;
                    }
                }
            }

            return cells;
        }

        public static double Iou(bool[] a, bool[] b)
        {
            int both = 0, either = 0;
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] && b[i]) both++;
                if (a[i] || b[i]) either++;
            }

            return either == 0 ? 0 : (double)both / either;
        }
    }
}
