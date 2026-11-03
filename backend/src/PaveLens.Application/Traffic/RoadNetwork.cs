namespace PaveLens.Application.Traffic;

/// <summary>An intersection. X/Y are schematic coordinates for drawing; Lat/Lon are approximate.</summary>
public sealed record RoadNode(string Id, string Name, double X, double Y, double Lat, double Lon);

/// <summary>A two-way street segment. <see cref="LanesPerDirection"/> and capacity apply to each direction.</summary>
public sealed record RoadSegment(
    string Id,
    string Name,
    string From,
    string To,
    double LengthKm,
    double FreeSpeedKmh,
    int LanesPerDirection,
    double CapacityPerLaneVph)
{
    public double FreeFlowTimeH => LengthKm / FreeSpeedKmh;
}

/// <summary>Hourly trips from one node to another.</summary>
public sealed record TripDemand(string From, string To, double Vph);

public sealed record RoadNetwork(
    string Name,
    string Description,
    IReadOnlyList<RoadNode> Nodes,
    IReadOnlyList<RoadSegment> Segments,
    IReadOnlyList<TripDemand> Demand);

/// <summary>One travel direction of a segment: the unit the assignment works on.</summary>
internal sealed record DirectedLink(int Index, string SegmentId, int From, int To, double FreeFlowTimeH, double CapacityVph);
