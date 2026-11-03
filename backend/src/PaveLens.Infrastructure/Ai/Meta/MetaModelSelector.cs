using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaveLens.Infrastructure.Ai.Meta;

/// <summary>
/// Replaces <c>auto</c> model names, once at startup, by the best models the configured key can use:
/// the newest version of the first available family (Muse Wonder before Muse Spark, SAM 3.1 before SAM 3).
/// Explicit model names are kept. <c>-contributor</c> variants are never chosen: they share data for training.
/// </summary>
internal sealed partial class MetaModelSelector(IHttpClientFactory httpFactory, IOptions<AiOptions> options, ILogger<MetaModelSelector> logger) : IHostedService
{
    public const string ClientName = "meta-models";
    public const string FallbackReasoning = "muse-wonder-1.0";
    public const string FallbackSegmentation = "sam-3.1";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var meta = options.Value.Meta;
        var reasoningAuto = IsAuto(meta.Reasoning.Model);
        var segmentationAuto = meta.SegmentationService == SegmentationService.Meta && IsAuto(meta.Sam31.Model);
        if (!reasoningAuto && !segmentationAuto) return;

        var available = await ListAsync(meta, cancellationToken);
        // Options are singletons read by every Meta client, so the chosen names reach all of them.
        if (reasoningAuto) meta.Reasoning.Model = Pick(available, meta.ReasoningFamilies) ?? FallbackReasoning;
        if (segmentationAuto) meta.Sam31.Model = Pick(available, meta.SegmentationFamilies) ?? FallbackSegmentation;
        if (IsAuto(meta.Sam31.Model)) meta.Sam31.Model = FallbackSegmentation;
        Log.Selected(logger, meta.Reasoning.Model, meta.SegmentationService == SegmentationService.Meta ? meta.Sam31.Model : meta.Segmentation.Model, available.Count);
    }

    /// <summary>The model to send: never <c>auto</c>, even if a client runs before or without this selector.</summary>
    internal static string Effective(string? model, string fallback) => IsAuto(model) ? fallback : model!;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static bool IsAuto(string? model) => string.IsNullOrWhiteSpace(model) || model.Equals(AiOptions.AutoModel, StringComparison.OrdinalIgnoreCase);

    /// <summary>Newest <c>family-1.2</c> of the first family present; ids with any other suffix are ignored.</summary>
    internal static string? Pick(IEnumerable<string> ids, IEnumerable<string> families)
    {
        var list = ids.ToList();
        foreach (var family in families)
        {
            var best = list
                .Select(id => (Id: id, Match: Regex.Match(id, $"^{Regex.Escape(family)}-(\\d+(?:\\.\\d+)*)$", RegexOptions.IgnoreCase)))
                .Where(x => x.Match.Success)
                .Select(x => (x.Id, Version: x.Match.Groups[1].Value.Split('.').Select(int.Parse).ToArray()))
                .OrderByDescending(x => x.Version, VersionComparer.Instance)
                .Select(x => x.Id)
                .FirstOrDefault();
            if (best is not null) return best;
        }
        return null;
    }

    private async Task<IReadOnlyList<string>> ListAsync(MetaOptions meta, CancellationToken cancellationToken)
    {
        try
        {
            using var http = httpFactory.CreateClient(ClientName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var baseUrl = meta.Reasoning.BaseUrl!.ToString();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), "models"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", meta.Reasoning.ApiKey);
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                Log.ListFailed(logger, $"HTTP {(int)response.StatusCode}");
                return [];
            }
            var body = await response.Content.ReadFromJsonAsync<JsonObject>(timeout.Token);
            return (body?["data"] as JsonArray ?? [])
                .Select(m => m?["id"]?.GetValue<string>())
                .OfType<string>()
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Log.ListFailed(logger, ex.GetType().Name);
            return [];
        }
    }

    private sealed class VersionComparer : IComparer<int[]>
    {
        public static readonly VersionComparer Instance = new();

        public int Compare(int[]? x, int[]? y)
        {
            for (var i = 0; i < Math.Max(x!.Length, y!.Length); i++)
            {
                var c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
                if (c != 0) return c;
            }
            return 0;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Meta models: reasoning {Reasoning}, segmentation {Segmentation} ({Count} models available to this key)")]
        public static partial void Selected(ILogger logger, string reasoning, string segmentation, int count);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not list Meta models ({Reason}); using the default models")]
        public static partial void ListFailed(ILogger logger, string reason);
    }
}
