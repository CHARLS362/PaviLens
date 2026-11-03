using System.Diagnostics;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Domain.Inspections;

namespace PaveLens.Infrastructure.Ai.Meta;

internal sealed record Sam31Object(double Confidence, double[][][] Rings, bool Holes);
internal sealed record Sam31Geometry(IReadOnlyList<Sam31Object> Objects);
internal interface ISam31Decoder
{
    Task<Sam31Geometry> DecodeAsync(string response, CancellationToken cancellationToken);
}

/// <summary>Uses Meta's published parser rather than approximating its binary mask encoding.</summary>
internal sealed class NodeSam31Decoder(string scriptPath, string executable) : ISam31Decoder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<Sam31Geometry> DecodeAsync(string response, CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath)) throw new ModelUnavailableException("No se encontró el runtime SAM 3.1. Consulta backend/docs/ORCHESTRATION.md.");
        var start = new ProcessStartInfo(executable) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(scriptPath);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Win32Exception) { throw new ModelUnavailableException("No se pudo iniciar Node.js para SAM 3.1. Configura Ai:Meta:NodeExecutable."); }
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* Already exited. */ } });
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteAsync(response.AsMemory(), cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdout;
        if (process.ExitCode != 0)
        {
            var error = await stderr;
            throw new ModelOutputException($"No se pudo decodificar la máscara SAM 3.1: {error[..Math.Min(error.Length, 300)]}");
        }
        return JsonSerializer.Deserialize<Sam31Geometry>(output, Json) ?? throw new ModelOutputException("El decodificador SAM no devolvió geometría.");
    }
}

internal sealed class MetaSam31Segmenter(HttpClient http, IOptions<AiOptions> options, ISam31Decoder decoder) : IDistressSegmenter
{
    private readonly MetaOptions _meta = options.Value.Meta;
    public AnalysisEngine Engine => AnalysisEngine.Meta;

    public async Task<SegmentationResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken)
    {
        var instances = new List<Sam3Instances.Instance>();
        foreach (var concept in request.Concepts)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri("responses", UriKind.Relative))
            {
                Content = JsonContent.Create(new
                {
                    model = _meta.Sam31.Model, stream = false,
                    metadata = new { mask_encoding = "one_bit", include_confidence = "true" },
                    input = new[] { new { type = "message", role = "user", content = new object[]
                    {
                        new { type = "input_text", text = concept },
                        new { type = "input_image", image_url = $"data:{request.Image.ContentType};base64,{Convert.ToBase64String(request.Image.Bytes.Span)}" }
                    } } }
                })
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", string.IsNullOrWhiteSpace(_meta.Sam31.ApiKey) ? _meta.Reasoning.ApiKey : _meta.Sam31.ApiKey);
            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new ModelUnavailableException($"Meta SAM 3.1 respondió {(int)response.StatusCode}.");
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 16 * 1024 * 1024) throw new ModelOutputException("La respuesta SAM supera el tamaño permitido.");
            var geometry = await decoder.DecodeAsync(body, cancellationToken);
            foreach (var item in geometry.Objects)
            {
                if (!double.IsFinite(item.Confidence) || item.Confidence is < 0 or > 1) throw new ModelOutputException("Confianza SAM inválida.");
                if (item.Confidence < _meta.SegmentationScoreThreshold) continue;
                if (item.Holes) throw new ModelOutputException("La máscara SAM contiene huecos: requiere revisión antes de medirla con el modelo de polígono actual.");
                foreach (var ring in item.Rings.Where(r => r.Length >= 3))
                {
                    if (ring.Any(p => p.Length != 2 || !double.IsFinite(p[0]) || !double.IsFinite(p[1]) || p[0] < 0 || p[1] < 0 || p[0] > request.Image.Width || p[1] > request.Image.Height))
                        throw new ModelOutputException("SAM devolvió una máscara fuera de las dimensiones de la imagen.");
                    var polygon = ring.Select(p => new NormalizedPoint(p[0] / request.Image.Width, p[1] / request.Image.Height)).ToArray();
                    instances.Add(new Sam3Instances.Instance(concept, item.Confidence, polygon));
                }
            }
        }
        var segments = Sam3Instances.Deduplicate(instances).Select((s, i) => new Segment(i, s.Concept, s.Score, s.Polygon)).ToArray();
        return new SegmentationResult(_meta.Sam31.Model, segments);
    }
}
