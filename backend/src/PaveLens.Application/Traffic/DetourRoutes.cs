namespace PaveLens.Application.Traffic;

/// <summary>
/// A path around the work zone, as an ordered list of intersections. <see cref="ExtraTimeMin"/> compares it with
/// crossing the closed segment before the closure.
/// </summary>
public sealed record DetourRouteDto(
    string Id,
    string Label,
    string FromNode,
    string ToNode,
    IReadOnlyList<string> Nodes,
    IReadOnlyList<string> SegmentIds,
    double TimeMin,
    double ExtraTimeMin);

/// <summary>
/// Shortest paths around a closed segment on the work-zone travel times (Dijkstra). The model may choose among
/// these and name them, but the paths themselves always come from this code.
/// </summary>
public static class DetourRouteFinder
{
    /// <param name="alternatives">Routes per direction: the fastest one, then ones that share no segment with it.</param>
    public static IReadOnlyList<DetourRouteDto> Find(
        RoadNetwork network, IReadOnlyList<LinkComparisonDto> links, string closedSegmentId, int alternatives = 1)
    {
        var closed = network.Segments.Single(s => s.Id == closedSegmentId);
        var nodes = network.Nodes.ToDictionary(n => n.Id);
        var routes = new List<DetourRouteDto>();
        var directionNumber = 0;

        foreach (var (from, to) in new[] { (closed.From, closed.To), (closed.To, closed.From) })
        {
            directionNumber++;
            var direct = links.FirstOrDefault(l => l.SegmentId == closedSegmentId && l.From == from && l.To == to);
            var directMin = direct?.BaselineTimeMin ?? 0;
            var excluded = new HashSet<string> { closedSegmentId };

            for (var k = 0; k < alternatives; k++)
            {
                var path = ShortestPath(links, from, to, excluded);
                if (path is null)
                {
                    break;
                }

                var time = path.Sum(l => l.WorkZoneTimeMin);
                var suffix = k == 0 ? "" : ((char)('a' + k)).ToString();
                routes.Add(new DetourRouteDto(
                    $"R{directionNumber}{suffix}",
                    $"{(k == 0 ? "Desvío" : "Alterna")} hacia el {Heading(nodes[from], nodes[to])}",
                    from,
                    to,
                    [from, .. path.Select(l => l.To)],
                    path.Select(l => l.SegmentId).ToList(),
                    Math.Round(time, 2),
                    Math.Round(time - directMin, 2)));

                foreach (var l in path)
                {
                    excluded.Add(l.SegmentId);
                }
            }
        }

        return routes;
    }

    private static List<LinkComparisonDto>? ShortestPath(
        IReadOnlyList<LinkComparisonDto> links, string from, string to, HashSet<string> excludedSegments)
    {
        var usable = links.Where(l => !l.Closed && !excludedSegments.Contains(l.SegmentId)).ToLookup(l => l.From);
        var best = new Dictionary<string, double> { [from] = 0 };
        var via = new Dictionary<string, LinkComparisonDto>();
        var queue = new PriorityQueue<string, double>();
        queue.Enqueue(from, 0);

        while (queue.TryDequeue(out var node, out var cost))
        {
            if (node == to)
            {
                break;
            }

            if (cost > best[node])
            {
                continue;
            }

            foreach (var link in usable[node])
            {
                var next = cost + link.WorkZoneTimeMin;
                if (next < best.GetValueOrDefault(link.To, double.PositiveInfinity))
                {
                    best[link.To] = next;
                    via[link.To] = link;
                    queue.Enqueue(link.To, next);
                }
            }
        }

        if (!via.ContainsKey(to))
        {
            return null;
        }

        var path = new List<LinkComparisonDto>();
        for (var n = to; n != from; n = via[n].From)
        {
            path.Add(via[n]);
        }

        path.Reverse();
        return path;
    }

    // Schematic Y grows southwards (row 0 is Jr. Puno, the northern street).
    private static string Heading(RoadNode a, RoadNode b) =>
        Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y)
            ? (b.X > a.X ? "este" : "oeste")
            : (b.Y > a.Y ? "sur" : "norte");
}
