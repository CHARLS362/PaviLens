namespace PaveLens.Application.Maintenance;

/// <summary>
/// Orders the work sites a crew visits in one day. Sites are grouped by urgency (PCI band) when asked, and
/// each group is ordered by nearest neighbour and then improved with 2-opt over great-circle distances.
/// Exact for the handful of sites a crew can patch in a day; no external optimiser is needed.
/// </summary>
public static class CrewRoutePlanner
{
    public const double EarthRadiusM = 6_371_000;

    /// <summary>Urgency band from PCI: worse pavement first. Unknown PCI goes last.</summary>
    public static int Band(double? pci) => pci switch
    {
        null => 3,
        < 40 => 0,
        < 70 => 1,
        _ => 2
    };

    public static string BandLabel(double? pci) => Band(pci) switch
    {
        0 => "Urgente",
        1 => "Programar",
        2 => "Preventivo",
        _ => "Sin PCI"
    };

    public static double DistanceM(GeoPoint a, GeoPoint b)
    {
        var lat1 = a.Latitude * Math.PI / 180;
        var lat2 = b.Latitude * Math.PI / 180;
        var dLat = lat2 - lat1;
        var dLon = (b.Longitude - a.Longitude) * Math.PI / 180;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>Returns the indexes of <paramref name="sites"/> in visiting order, starting from <paramref name="depot"/>.</summary>
    public static IReadOnlyList<int> Order(GeoPoint depot, IReadOnlyList<(GeoPoint Point, double? Pci)> sites, bool priorityFirst)
    {
        var groups = priorityFirst
            ? Enumerable.Range(0, sites.Count).GroupBy(i => Band(sites[i].Pci)).OrderBy(g => g.Key).Select(g => g.ToList()).ToList()
            : [Enumerable.Range(0, sites.Count).ToList()];

        var order = new List<int>(sites.Count);
        var start = depot;
        foreach (var group in groups)
        {
            var tour = NearestNeighbour(start, group, sites);
            TwoOpt(start, tour, sites);
            order.AddRange(tour);
            start = sites[tour[^1]].Point;
        }
        return order;
    }

    private static List<int> NearestNeighbour(GeoPoint start, List<int> group, IReadOnlyList<(GeoPoint Point, double? Pci)> sites)
    {
        var left = new List<int>(group);
        var tour = new List<int>(group.Count);
        var at = start;
        while (left.Count > 0)
        {
            var next = left.MinBy(i => DistanceM(at, sites[i].Point));
            left.Remove(next);
            tour.Add(next);
            at = sites[next].Point;
        }
        return tour;
    }

    /// <summary>Open-path 2-opt: reverses segments while that shortens the path from <paramref name="start"/>.</summary>
    private static void TwoOpt(GeoPoint start, List<int> tour, IReadOnlyList<(GeoPoint Point, double? Pci)> sites)
    {
        GeoPoint At(int k) => k < 0 ? start : sites[tour[k]].Point;
        var improved = true;
        for (var pass = 0; improved && pass < 50; pass++)
        {
            improved = false;
            for (var i = 0; i < tour.Count - 1; i++)
            {
                for (var j = i + 1; j < tour.Count; j++)
                {
                    var before = DistanceM(At(i - 1), At(i)) + (j + 1 < tour.Count ? DistanceM(At(j), At(j + 1)) : 0);
                    var after = DistanceM(At(i - 1), At(j)) + (j + 1 < tour.Count ? DistanceM(At(i), At(j + 1)) : 0);
                    if (after + 1e-6 < before)
                    {
                        tour.Reverse(i, j - i + 1);
                        improved = true;
                    }
                }
            }
        }
    }
}
