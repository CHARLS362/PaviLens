using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaveLens.Application.Maintenance;

namespace PaveLens.Infrastructure.Maps;

/// <summary>
/// Counts Google calls per UTC day and keeps recent routes, shared by every request.
/// The daily cap protects the key's quota even if the endpoint is abused.
/// </summary>
public sealed class GoogleMapsUsage(TimeProvider clock)
{
    private readonly object _gate = new();
    private DateOnly _day;
    private int _count;

    internal ConcurrentDictionary<string, (DateTimeOffset Expires, RoadRoute Route)> Cache { get; } = new(StringComparer.Ordinal);

    public int UsedToday
    {
        get { lock (_gate) { Roll(); return _count; } }
    }

    internal bool TryConsume(int limit)
    {
        lock (_gate)
        {
            Roll();
            if (_count >= limit) return false;
            _count++;
            return true;
        }
    }

    internal DateTimeOffset Now => clock.GetUtcNow();

    private void Roll()
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (today == _day) return;
        _day = today;
        _count = 0;
    }
}

/// <summary>
/// Google Routes API (computeRoutes). The key travels only in the <c>X-Goog-Api-Key</c> header, so it never
/// appears in URLs, HTTP logs or error messages. Any failure returns null and the caller falls back to an estimate.
/// </summary>
public sealed partial class GoogleRoutesProvider(
    HttpClient http, IOptions<GoogleMapsOptions> options, GoogleMapsUsage usage, ILogger<GoogleRoutesProvider> logger)
{
    private const string FieldMask = "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline,routes.legs.distanceMeters,routes.legs.duration";
    private readonly GoogleMapsOptions _options = options.Value;

    public bool Enabled => _options.ServerEnabled;

    public async Task<RoadRoute?> RouteAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken cancellationToken)
    {
        if (!_options.ServerEnabled || waypoints.Count < 2) return null;

        var key = string.Join('|', waypoints.Select(p => FormattableString.Invariant($"{p.Latitude:F5},{p.Longitude:F5}")));
        if (usage.Cache.TryGetValue(key, out var hit) && hit.Expires > usage.Now) return hit.Route;

        if (!usage.TryConsume(_options.DailyRequestLimit))
        {
            LogDailyLimit(logger, _options.DailyRequestLimit);
            return null;
        }

        static JsonObject Waypoint(GeoPoint p) => new()
        {
            ["location"] = new JsonObject { ["latLng"] = new JsonObject { ["latitude"] = p.Latitude, ["longitude"] = p.Longitude } }
        };
        var body = new JsonObject
        {
            ["origin"] = Waypoint(waypoints[0]),
            ["destination"] = Waypoint(waypoints[^1]),
            ["intermediates"] = new JsonArray(waypoints.Skip(1).Take(waypoints.Count - 2).Select(p => (JsonNode)Waypoint(p)).ToArray()),
            ["travelMode"] = "DRIVE",
            ["routingPreference"] = "TRAFFIC_UNAWARE",
            ["languageCode"] = "es",
            ["units"] = "METRIC"
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("directions/v2:computeRoutes", UriKind.Relative))
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
            request.Headers.Add("X-Goog-FieldMask", FieldMask);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The status alone: Google's error body may echo request details.
                LogGoogleStatus(logger, (int)response.StatusCode, response.StatusCode == HttpStatusCode.TooManyRequests ? "cuota agotada" : "rechazada");
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (json.Length > 2 * 1024 * 1024) return null;
            var route = Parse(json, waypoints.Count - 1);
            if (route is not null)
                usage.Cache[key] = (usage.Now.AddMinutes(Math.Clamp(_options.CacheMinutes, 1, 30)), route);
            return route;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            LogGoogleFailure(logger, ex.GetType().Name);
            return null;
        }
    }

    internal static RoadRoute? Parse(string json, int expectedLegs)
    {
        var root = JsonNode.Parse(json);
        var route = root?["routes"]?[0];
        if (route is null) return null;
        var legs = route["legs"]?.AsArray().Select(l => (DistanceM: (double)(l?["distanceMeters"]?.GetValue<int>() ?? 0), DurationS: Seconds(l?["duration"]))).ToList() ?? [];
        if (legs.Count != expectedLegs) return null;
        return new RoadRoute(route["distanceMeters"]?.GetValue<int>() ?? legs.Sum(l => l.DistanceM), Seconds(route["duration"]),
            route["polyline"]?["encodedPolyline"]?.GetValue<string>(), legs, "google");
    }

    /// <summary>Routes API durations are strings such as "754s".</summary>
    private static double Seconds(JsonNode? node)
    {
        var text = node?.GetValue<string>();
        return text is not null && text.EndsWith('s') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google Maps: límite diario propio alcanzado ({Limit} llamadas); se usa la estimación")]
    private static partial void LogDailyLimit(ILogger logger, int limit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google Routes respondió {Status} ({Reason}); se usa la estimación")]
    private static partial void LogGoogleStatus(ILogger logger, int status, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google Routes no disponible ({Error}); se usa la estimación")]
    private static partial void LogGoogleFailure(ILogger logger, string error);
}
