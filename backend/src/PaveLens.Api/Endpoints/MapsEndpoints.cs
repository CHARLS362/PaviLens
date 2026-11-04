using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using PaveLens.Application.Maintenance;
using PaveLens.Infrastructure.Maps;

namespace PaveLens.Api.Endpoints;

internal static class MapsEndpoints
{
    public static RouteGroupBuilder MapMapsEndpoints(this RouteGroupBuilder api, IConfiguration configuration)
    {
        var allowedOrigins = (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .Select(o => o.TrimEnd('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);

        api.MapGet("/maps/config", (HttpContext http, IOptions<MapsOptions> maps, IOptions<GoogleMapsOptions> google, GoogleMapsUsage usage) =>
            {
                // Never cached by the browser or a proxy, never indexed.
                http.Response.Headers.CacheControl = "no-store, private";
                http.Response.Headers["X-Robots-Tag"] = "noindex";
                var m = maps.Value;
                var g = google.Value;
                var trusted = IsOwnPage(http.Request, allowedOrigins);
                var googleMap = trusted && !string.IsNullOrWhiteSpace(g.EffectiveBrowserKey);
                return TypedResults.Ok(new MapsConfigDto(
                    MapProvider: m.Provider == MapProvider.Google && googleMap ? "google" : "osm",
                    GoogleAvailable: googleMap,
                    BrowserKey: googleMap ? g.EffectiveBrowserKey : null,
                    TileUrl: m.TileUrl,
                    TileAttribution: m.TileAttribution,
                    GoogleRouting: g.ServerEnabled,
                    UsedToday: usage.UsedToday,
                    DailyLimit: g.DailyRequestLimit));
            })
            .WithTags("Maps")
            .RequireRateLimiting(RateLimitPolicies.Maps)
            .WithSummary("Configuración del mapa: OpenStreetMap por defecto; la clave de Google solo se entrega a la propia aplicación web");

        api.MapGet("/maps/inspections", async (MaintenanceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListPointsAsync(ct)))
            .WithTags("Maps")
            .WithSummary("Inspecciones ubicadas en el mapa, con su PCI");

        api.MapPost("/maps/crew-route", async (CrewRouteRequest request, MaintenanceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.PlanCrewRouteAsync(request, ct)))
            .WithTags("Maps")
            .RequireRateLimiting(RateLimitPolicies.Ai)
            .WithSummary("Plan de la jornada: el código propone el recorrido más corto, Muse Wonder (Meta) decide el orden final, el cierre y la hora de cada obra, y Google Routes traza las calles reales");

        return api;
    }

    /// <summary>
    /// True only for a request made by a page of this application: the browser's Fetch Metadata, Origin and
    /// Referer must all agree. Scripts and other sites get no key. A deterrent, not a secret: the key the map
    /// uses is visible to whoever opens the app, which is why it must also be restricted in Google Cloud.
    /// </summary>
    internal static bool IsOwnPage(HttpRequest request, IReadOnlySet<string> allowedOrigins)
    {
        var self = $"{request.Scheme}://{request.Host}";
        bool Allowed(string origin) => string.Equals(origin, self, StringComparison.OrdinalIgnoreCase) || allowedOrigins.Contains(origin);

        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site is not ("same-origin" or "same-site")) return false;

        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !Allowed(origin.TrimEnd('/'))) return false;

        var referer = request.Headers.Referer.ToString();
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var refUri)) return false;
        return Allowed(refUri.GetLeftPart(UriPartial.Authority));
    }

    /// <param name="MapProvider">"osm" (default, no key) or "google" (only when configured and asked by the app itself).</param>
    internal sealed record MapsConfigDto(string MapProvider, bool GoogleAvailable, string? BrowserKey, string TileUrl, string TileAttribution,
        bool GoogleRouting, int UsedToday, int DailyLimit);
}
