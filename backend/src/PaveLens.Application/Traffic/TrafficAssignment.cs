namespace PaveLens.Application.Traffic;

/// <summary>A lane closure on one segment, applied to both directions.</summary>
public sealed record WorkZone(string SegmentId, int LanesClosed);

public sealed record LinkFlow(string SegmentId, string From, string To, double FlowVph, double CapacityVph, double TimeH);

public sealed record AssignmentResult(
    IReadOnlyList<LinkFlow> Links,
    double TotalTravelTimeVehH,
    int Iterations,
    double RelativeGap);

/// <summary>
/// Static user-equilibrium assignment (Wardrop's first principle) with BPR link delays, solved by
/// Frank-Wolfe on Beckmann's convex program (PLANIFICACION §6): all-or-nothing loads on Dijkstra
/// shortest paths, bisection line search, stop at a relative gap below the tolerance.
/// </summary>
public static class TrafficAssignment
{
    public const double Alpha = 0.15;
    public const double Beta = 4;

    public static double BprTime(double freeFlowTimeH, double flowVph, double capacityVph) =>
        freeFlowTimeH * (1 + Alpha * Math.Pow(flowVph / capacityVph, Beta));

    public static AssignmentResult Solve(
        RoadNetwork network, WorkZone? workZone = null, double gapTolerance = 1e-4, int maxIterations = 200)
    {
        var nodeIndex = network.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var links = BuildLinks(network, nodeIndex, workZone);
        var outgoing = Enumerable.Range(0, network.Nodes.Count)
            .Select(n => links.Where(l => l.From == n).ToArray())
            .ToArray();
        var demandByOrigin = network.Demand
            .GroupBy(d => nodeIndex[d.From])
            .ToDictionary(g => g.Key, g => g.Select(d => (To: nodeIndex[d.To], d.Vph)).ToArray());

        var times = links.Select(l => l.FreeFlowTimeH).ToArray();
        var flows = AllOrNothing(links, outgoing, demandByOrigin, times, network.Nodes.Count);
        var gap = double.PositiveInfinity;
        var iteration = 0;

        while (iteration < maxIterations)
        {
            iteration++;
            UpdateTimes(links, flows, times);
            var target = AllOrNothing(links, outgoing, demandByOrigin, times, network.Nodes.Count);

            var current = Dot(flows, times);
            gap = current > 0 ? (current - Dot(target, times)) / current : 0;
            if (gap < gapTolerance)
            {
                break;
            }

            var step = LineSearch(links, flows, target);
            for (var i = 0; i < flows.Length; i++)
            {
                flows[i] += step * (target[i] - flows[i]);
            }
        }

        UpdateTimes(links, flows, times);
        var nodes = network.Nodes;
        var result = links
            .Select(l => new LinkFlow(l.SegmentId, nodes[l.From].Id, nodes[l.To].Id, flows[l.Index], l.CapacityVph, times[l.Index]))
            .ToList();
        return new AssignmentResult(result, Dot(flows, times), iteration, Math.Max(gap, 0));
    }

    private static DirectedLink[] BuildLinks(RoadNetwork network, Dictionary<string, int> nodeIndex, WorkZone? workZone)
    {
        if (workZone is not null && network.Segments.All(s => s.Id != workZone.SegmentId))
        {
            throw new ArgumentException($"El tramo '{workZone.SegmentId}' no existe en la red.", nameof(workZone));
        }

        var links = new List<DirectedLink>();
        foreach (var s in network.Segments)
        {
            var lanes = s.LanesPerDirection;
            if (workZone?.SegmentId == s.Id)
            {
                // C_work = C · (1 − closed / total); a fully closed segment is removed from the graph.
                lanes -= Math.Clamp(workZone.LanesClosed, 0, lanes);
                if (lanes == 0)
                {
                    continue;
                }
            }

            var capacity = lanes * s.CapacityPerLaneVph;
            var (from, to) = (nodeIndex[s.From], nodeIndex[s.To]);
            links.Add(new DirectedLink(links.Count, s.Id, from, to, s.FreeFlowTimeH, capacity));
            links.Add(new DirectedLink(links.Count, s.Id, to, from, s.FreeFlowTimeH, capacity));
        }

        return links.ToArray();
    }

    private static void UpdateTimes(DirectedLink[] links, double[] flows, double[] times)
    {
        foreach (var l in links)
        {
            times[l.Index] = BprTime(l.FreeFlowTimeH, flows[l.Index], l.CapacityVph);
        }
    }

    /// <summary>Loads every origin's demand on its shortest-path tree under the given link times.</summary>
    private static double[] AllOrNothing(
        DirectedLink[] links,
        DirectedLink[][] outgoing,
        Dictionary<int, (int To, double Vph)[]> demandByOrigin,
        double[] times,
        int nodeCount)
    {
        var flows = new double[links.Length];
        foreach (var (origin, trips) in demandByOrigin)
        {
            var predecessor = ShortestPathTree(origin, outgoing, times, nodeCount);
            foreach (var (to, vph) in trips)
            {
                if (to != origin && predecessor[to] is null)
                {
                    throw new InvalidOperationException("El cierre deja pares origen-destino sin ruta posible.");
                }

                for (var node = to; node != origin; node = predecessor[node]!.From)
                {
                    flows[predecessor[node]!.Index] += vph;
                }
            }
        }

        return flows;
    }

    private static DirectedLink?[] ShortestPathTree(int origin, DirectedLink[][] outgoing, double[] times, int nodeCount)
    {
        var distance = Enumerable.Repeat(double.PositiveInfinity, nodeCount).ToArray();
        var predecessor = new DirectedLink?[nodeCount];
        var queue = new PriorityQueue<int, double>();
        distance[origin] = 0;
        queue.Enqueue(origin, 0);

        while (queue.TryDequeue(out var node, out var d))
        {
            if (d > distance[node])
            {
                continue; // stale entry
            }

            foreach (var link in outgoing[node])
            {
                var candidate = d + times[link.Index];
                if (candidate < distance[link.To])
                {
                    distance[link.To] = candidate;
                    predecessor[link.To] = link;
                    queue.Enqueue(link.To, candidate);
                }
            }
        }

        return predecessor;
    }

    /// <summary>
    /// Step λ ∈ [0, 1] minimizing Beckmann's objective along x + λ(y − x): its derivative
    /// Σ (y − x)·t(x + λ(y − x)) is increasing in λ, so bisection on the sign finds the root.
    /// </summary>
    private static double LineSearch(DirectedLink[] links, double[] x, double[] y)
    {
        double Derivative(double lambda) => links.Sum(l =>
        {
            var direction = y[l.Index] - x[l.Index];
            return direction * BprTime(l.FreeFlowTimeH, x[l.Index] + lambda * direction, l.CapacityVph);
        });

        if (Derivative(1) <= 0)
        {
            return 1;
        }

        double low = 0, high = 1;
        for (var i = 0; i < 40; i++)
        {
            var mid = (low + high) / 2;
            if (Derivative(mid) > 0) high = mid;
            else low = mid;
        }

        return (low + high) / 2;
    }

    private static double Dot(double[] a, double[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }
}
