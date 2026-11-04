using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaveLens.Application.Maintenance;

namespace PaveLens.Infrastructure.Maps;

/// <summary>Recent OSRM routes and the pacing gate, shared by every request.</summary>
public sealed class OsrmCache(TimeProvider clock)
{
    internal ConcurrentDictionary<string, (DateTimeOffset Expires, RoadRoute Route)> Routes { get; } = new(StringComparer.Ordinal);
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset LastCall { get; set; } = DateTimeOffset.MinValue;
    internal DateTimeOffset Now => clock.GetUtcNow();
}

/// <summary>
/// Driving routes over OpenStreetMap with OSRM: no key, no account. The public demo server asks for at most one
/// request per second and light use, so calls are paced and cached; set Maps:OsrmBaseUrl to your own server
/// (osrm-backend with the Peru extract) for production.
/// </summary>
public sealed partial class OsrmRouteProvider(HttpClient http, IOptions<MapsOptions> options, OsrmCache cache, ILogger<OsrmRouteProvider> logger)
{
    public async Task<RoadRoute?> RouteAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken cancellationToken)
    {
        if (waypoints.Count < 2) return null;
        var coordinates = string.Join(';', waypoints.Select(p => FormattableString.Invariant($"{p.Longitude:F6},{p.Latitude:F6}")));
        if (cache.Routes.TryGetValue(coordinates, out var hit) && hit.Expires > cache.Now) return hit.Route;

        await cache.Gate.WaitAsync(cancellationToken);
        try
        {
            var wait = cache.LastCall.AddSeconds(1) - cache.Now;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
            cache.LastCall = cache.Now;

            var uri = new Uri($"route/v1/driving/{coordinates}?overview=full&geometries=polyline&steps=false", UriKind.Relative);
            using var response = await http.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogStatus(logger, (int)response.StatusCode);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (json.Length > 4 * 1024 * 1024) return null;
            var route = Parse(json, waypoints.Count - 1);
            if (route is not null)
                cache.Routes[coordinates] = (cache.Now.AddMinutes(Math.Clamp(options.Value.CacheMinutes, 1, 60)), route);
            return route;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            LogFailure(logger, ex.GetType().Name);
            return null;
        }
        finally
        {
            cache.Gate.Release();
        }
    }

    /// <summary>OSRM answer: <c>code</c> "Ok", metres and seconds as numbers, geometry as a precision-5 polyline.</summary>
    internal static RoadRoute? Parse(string json, int expectedLegs)
    {
        var root = JsonNode.Parse(json);
        if (root?["code"]?.GetValue<string>() != "Ok") return null;
        var route = root["routes"]?[0];
        if (route is null) return null;
        var legs = route["legs"]?.AsArray()
            .Select(l => (DistanceM: l?["distance"]?.GetValue<double>() ?? 0, DurationS: l?["duration"]?.GetValue<double>() ?? 0)).ToList() ?? [];
        if (legs.Count != expectedLegs) return null;
        return new RoadRoute(route["distance"]?.GetValue<double>() ?? legs.Sum(l => l.DistanceM),
            route["duration"]?.GetValue<double>() ?? legs.Sum(l => l.DurationS),
            route["geometry"]?.GetValue<string>(), legs, "osm");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OSRM respondió {Status}; se usa la estimación")]
    private static partial void LogStatus(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OSRM no disponible ({Error}); se usa la estimación")]
    private static partial void LogFailure(ILogger logger, string error);
}

/// <summary>
/// Picks the routing engine for the map the route is drawn on. OpenStreetMap map: OSRM only. Google map: Google
/// Routes, falling back to OSRM (open data can be drawn on any map). Null when neither answers.
/// </summary>
public sealed class RoadRouteRouter(OsrmRouteProvider osrm, GoogleRoutesProvider google) : IRoadRouteProvider
{
    public async Task<RoadRoute?> RouteAsync(IReadOnlyList<GeoPoint> waypoints, MapProvider preferred, CancellationToken cancellationToken)
    {
        if (preferred == MapProvider.Google && google.Enabled)
        {
            var route = await google.RouteAsync(waypoints, cancellationToken);
            if (route is not null) return route;
        }
        return await osrm.RouteAsync(waypoints, cancellationToken);
    }
}
