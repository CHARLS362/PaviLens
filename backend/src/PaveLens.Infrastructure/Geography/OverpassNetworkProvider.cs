using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using PaveLens.Application.Common;
using PaveLens.Application.Geography;
using PaveLens.Domain.Common;

namespace PaveLens.Infrastructure.Geography;

public sealed class GeographicNetworkCache
{
    internal ConcurrentDictionary<string, GeographicNetwork> Networks { get; } = new(StringComparer.Ordinal);
    internal SemaphoreSlim Gate { get; } = new(1, 1);
}

public sealed class OverpassNetworkProvider(HttpClient http, GeographicNetworkCache cache) : IGeographicNetworkProvider
{
    public GeographicNetwork FindSnapshot(string id) => cache.Networks.TryGetValue(id, out var network)
        ? network : throw new DomainException("La red ya no está disponible. Carga nuevamente el área geográfica.");

    public async Task<GeographicNetwork> LoadAsync(double latitude, double longitude, int radiusM, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -85 or > 85 || longitude is < -180 or > 180 || radiusM is < 200 or > 1500)
            throw new DomainException("Usa coordenadas válidas y un radio entre 200 y 1500 m.");
        var key = FormattableString.Invariant($"{latitude:F4}:{longitude:F4}:{radiusM}");
        await cache.Gate.WaitAsync(cancellationToken);
        try
        {
            var existing = cache.Networks.Values.FirstOrDefault(n => FormattableString.Invariant($"{n.Latitude:F4}:{n.Longitude:F4}:{n.RadiusM}") == key && n.RetrievedAtUtc > DateTime.UtcNow.AddMinutes(-20));
            if (existing is not null) return existing;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var requestToken = deadline.Token;
            var query = FormattableString.Invariant($"[out:json][timeout:20];way(around:{radiusM},{latitude:F4},{longitude:F4})[highway~\"^(motorway|trunk|primary|secondary|tertiary|residential|unclassified|living_street|service|.*_link)$\"]->.roads;(.roads;rel(bw.roads)[type=restriction];);out geom;");
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("api/interpreter", UriKind.Relative)) { Content = content };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if (!response.IsSuccessStatusCode) throw new ModelUnavailableException($"OpenStreetMap/Overpass respondió {(int)response.StatusCode}.");
            await using var stream = await response.Content.ReadAsStreamAsync(requestToken);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, requestToken)) > 0)
            {
                if (buffer.Length + read > 8 * 1024 * 1024) throw new DomainException("El área contiene demasiados datos. Reduce el radio.");
                await buffer.WriteAsync(bytes.AsMemory(0, read), requestToken);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            var result = Parse(document.RootElement, latitude, longitude, radiusM);
            while (cache.Networks.Count >= 16)
            {
                var oldest = cache.Networks.Values.MinBy(n => n.RetrievedAtUtc)!;
                cache.Networks.TryRemove(oldest.Id, out _);
            }
            cache.Networks[result.Id] = result;
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ModelUnavailableException("OpenStreetMap/Overpass agotó el tiempo de espera. Reduce el área o vuelve a intentarlo más tarde.");
        }
        catch (HttpRequestException)
        {
            throw new ModelUnavailableException("No se pudo conectar con OpenStreetMap/Overpass. Vuelve a intentarlo más tarde.");
        }
        catch (JsonException)
        {
            throw new ModelUnavailableException("OpenStreetMap/Overpass devolvió datos no válidos. Vuelve a cargar el área.");
        }
        finally { cache.Gate.Release(); }
    }

    public static GeographicNetwork Parse(JsonElement root, double latitude, double longitude, int radiusM)
    {
        if (root.TryGetProperty("remark", out _)) throw new ModelUnavailableException("Overpass devolvió una respuesta incompleta. Reduce el área o vuelve a intentarlo.");
        var nodes = new Dictionary<long, GeographicNode>();
        var edges = new List<GeographicEdge>();
        var restrictions = new List<TurnRestriction>();
        var warnings = new List<string> { "Geometría de OpenStreetMap; los datos pueden estar incompletos o desactualizados. © OpenStreetMap contributors, ODbL." };
        foreach (var element in root.GetProperty("elements").EnumerateArray())
        {
            var type = element.GetProperty("type").GetString();
            if (!element.TryGetProperty("tags", out var tags)) continue;
            string Tag(string name) => tags.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
            if (type == "relation")
            {
                var rule = Tag("restriction");
                var members = element.GetProperty("members").EnumerateArray().ToArray();
                var from = members.FirstOrDefault(m => m.GetProperty("role").GetString() == "from");
                var to = members.FirstOrDefault(m => m.GetProperty("role").GetString() == "to");
                var via = members.FirstOrDefault(m => m.GetProperty("role").GetString() == "via");
                if (from.ValueKind == JsonValueKind.Undefined || to.ValueKind == JsonValueKind.Undefined || via.ValueKind == JsonValueKind.Undefined ||
                    via.GetProperty("type").GetString() != "node" || Tag("restriction:conditional") != "" || Tag("except") != "")
                { warnings.Add("Existen restricciones de giro complejas o condicionales que requieren revisión local."); continue; }
                if (rule.StartsWith("no_", StringComparison.Ordinal) || rule.StartsWith("only_", StringComparison.Ordinal))
                    restrictions.Add(new TurnRestriction(from.GetProperty("ref").GetInt64(), via.GetProperty("ref").GetInt64(), to.GetProperty("ref").GetInt64(), rule.StartsWith("only_", StringComparison.Ordinal), rule));
                continue;
            }
            if (type != "way" || Tag("motor_vehicle") is "no" or "private" || Tag("vehicle") is "no" or "private" || Tag("access") is "no" or "private") continue;
            var ids = element.GetProperty("nodes").EnumerateArray().Select(n => n.GetInt64()).ToArray();
            var geometry = element.GetProperty("geometry").EnumerateArray().ToArray();
            if (ids.Length != geometry.Length) throw new DomainException("Geometría OSM incompleta.");
            var wayId = element.GetProperty("id").GetInt64();
            var oneWay = Tag("oneway");
            if (oneWay is "reversible" or "alternating") { warnings.Add("Se excluyeron vías con sentido reversible o alternado: requieren regulación operativa local."); continue; }
            if (oneWay == "" && Tag("highway") == "motorway") oneWay = "yes";
            var forward = oneWay != "-1";
            var backward = !(oneWay is "yes" or "1" or "true" || (Tag("junction") == "roundabout" && oneWay != "no"));
            var speedTag = Tag("maxspeed");
            var mph = speedTag.EndsWith(" mph", StringComparison.Ordinal);
            var knownSpeed = double.TryParse(speedTag.Replace(" mph", "", StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) && speed > 0;
            speed = knownSpeed ? Math.Clamp(mph ? speed * 1.609344 : speed, 5, 130) : 30;
            if (Tag("access:conditional") != "" || Tag("oneway:conditional") != "") warnings.Add("Hay accesos o sentidos condicionales que requieren comprobación en campo.");
            for (var i = 0; i < ids.Length; i++)
                nodes[ids[i]] = new GeographicNode(ids[i], geometry[i].GetProperty("lat").GetDouble(), geometry[i].GetProperty("lon").GetDouble());
            for (var i = 1; i < ids.Length; i++)
            {
                var a = nodes[ids[i - 1]]; var b = nodes[ids[i]];
                var dLat = (b.Latitude - a.Latitude) * Math.PI / 180;
                var dLon = (b.Longitude - a.Longitude) * Math.PI / 180;
                var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(a.Latitude * Math.PI / 180) * Math.Cos(b.Latitude * Math.PI / 180) * Math.Pow(Math.Sin(dLon / 2), 2);
                var length = 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
                if (length <= 0) continue;
                edges.Add(new GeographicEdge($"{wayId}:{i}", wayId, a.Id, b.Id, Tag("name") is { Length: > 0 } name ? name : Tag("highway"), length, speed, !knownSpeed, forward, backward));
            }
            if (nodes.Count > 5000 || edges.Count > 7000) throw new DomainException("Red demasiado grande. Reduce el radio.");
        }
        if (edges.Count == 0) throw new DomainException("No se encontraron calles transitables en esta área.");
        if (edges.Any(e => e.SpeedAssumed)) warnings.Add("Algunas velocidades no están informadas: se usa 30 km/h como referencia explícita, sin congestión.");
        return new GeographicNetwork(Guid.NewGuid().ToString("N"), DateTime.UtcNow, latitude, longitude, radiusM, nodes.Values.ToArray(), edges, restrictions, warnings.Distinct(StringComparer.Ordinal).ToArray());
    }
}
