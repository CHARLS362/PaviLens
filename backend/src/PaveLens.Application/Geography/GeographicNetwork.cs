using PaveLens.Domain.Common;

namespace PaveLens.Application.Geography;

public sealed record GeographicNode(long Id, double Latitude, double Longitude);
public sealed record GeographicEdge(string Id, long WayId, long From, long To, string Name, double LengthM, double SpeedKmh, bool SpeedAssumed, bool Forward, bool Backward);
public sealed record TurnRestriction(long FromWay, long ViaNode, long ToWay, bool Only, string Kind = "");
public sealed record GeographicNetwork(string Id, DateTime RetrievedAtUtc, double Latitude, double Longitude, int RadiusM,
    IReadOnlyList<GeographicNode> Nodes, IReadOnlyList<GeographicEdge> Edges, IReadOnlyList<TurnRestriction> Restrictions, IReadOnlyList<string> Warnings);
public sealed record GeographicRoute(string Direction, IReadOnlyList<long> NodeIds, IReadOnlyList<string> EdgeIds, double DistanceM, double ReferenceTimeMin);
public sealed record GeographicClosure(string NetworkId, string ClosedEdgeId, IReadOnlyList<GeographicRoute> Routes, IReadOnlyList<string> Warnings);

public interface IGeographicNetworkProvider
{
    Task<GeographicNetwork> LoadAsync(double latitude, double longitude, int radiusM, CancellationToken cancellationToken);
    GeographicNetwork FindSnapshot(string id);
}

/// <summary>Directed shortest paths with via-node turn restrictions. No synthetic demand or congestion claims.</summary>
public static class GeographicRouting
{
    private static readonly string[] ReferenceWarnings =
    [
        "Ruta de referencia para automóvil. No incluye congestión, aforos, demanda ni validación operativa en campo.",
        "Comprueba señalización, restricciones condicionales y cierres actuales antes de utilizar el recorrido."
    ];
    private sealed record Arc(GeographicEdge Edge, long To);
    private readonly record struct State(long Node, long IncomingWay, long PreviousNode);

    public static GeographicClosure Close(GeographicNetwork network, string edgeId)
    {
        var closed = network.Edges.FirstOrDefault(e => e.Id == edgeId) ?? throw new DomainException("Tramo geográfico desconocido.");
        var adjacency = network.Nodes.ToDictionary(n => n.Id, _ => new List<Arc>());
        foreach (var e in network.Edges.Where(e => e.Id != edgeId))
        {
            if (e.Forward) adjacency[e.From].Add(new Arc(e, e.To));
            if (e.Backward) adjacency[e.To].Add(new Arc(e, e.From));
        }
        var routes = new List<GeographicRoute>();
        if (closed.Forward && Find(closed.From, closed.To, "Forward") is { } forward) routes.Add(forward);
        if (closed.Backward && Find(closed.To, closed.From, "Backward") is { } backward) routes.Add(backward);
        var expected = (closed.Forward ? 1 : 0) + (closed.Backward ? 1 : 0);
        var warnings = network.Warnings.Concat(ReferenceWarnings).ToList();
        if (routes.Count < expected) warnings.Add("No se encontró desvío para todos los sentidos dentro del área importada. Amplía el área o revisa el cierre.");
        return new GeographicClosure(network.Id, edgeId, routes, warnings);

        GeographicRoute? Find(long start, long end, string direction)
        {
            var initial = new State(start, 0, 0);
            var queue = new PriorityQueue<State, double>();
            var distances = new Dictionary<State, double> { [initial] = 0 };
            var previous = new Dictionary<State, (State State, GeographicEdge Edge)>();
            queue.Enqueue(initial, 0);
            while (queue.TryDequeue(out var current, out var distance))
            {
                if (distance > distances[current]) continue;
                if (current.Node == end)
                {
                    var nodes = new List<long> { end };
                    var edges = new List<GeographicEdge>();
                    while (previous.TryGetValue(current, out var p)) { edges.Add(p.Edge); current = p.State; nodes.Add(current.Node); }
                    nodes.Reverse(); edges.Reverse();
                    return new GeographicRoute(direction, nodes, edges.Select(e => e.Id).ToArray(), Math.Round(edges.Sum(e => e.LengthM), 1), Math.Round(distance, 2));
                }
                foreach (var arc in adjacency[current.Node])
                {
                    var rules = network.Restrictions.Where(r => r.ViaNode == current.Node && r.FromWay == current.IncomingWay).ToArray();
                    bool Matches(TurnRestriction rule) => rule.ToWay == arc.Edge.WayId && (rule.Kind switch
                    {
                        "no_u_turn" or "only_u_turn" => arc.To == current.PreviousNode,
                        "no_straight_on" or "only_straight_on" when rule.FromWay == rule.ToWay => arc.To != current.PreviousNode,
                        _ => true
                    });
                    if (rules.Any(r => !r.Only && Matches(r)) || rules.Any(r => r.Only) && !rules.Any(r => r.Only && Matches(r))) continue;
                    var next = new State(arc.To, arc.Edge.WayId, current.Node);
                    var cost = distance + arc.Edge.LengthM / (arc.Edge.SpeedKmh * 1000) * 60;
                    if (cost >= distances.GetValueOrDefault(next, double.PositiveInfinity)) continue;
                    distances[next] = cost; previous[next] = (current, arc.Edge); queue.Enqueue(next, cost);
                }
            }
            return null;
        }
    }
}
