namespace PaveLens.Application.Traffic;

/// <summary>
/// Synthetic demo network laid out as a grid around Puno's Plaza de Armas (≈ −15.8402, −70.0219).
/// Street names give orientation only: geometry, capacities and demand are illustrative, not survey data.
/// </summary>
public static class PunoDemoNetwork
{
    private const double OriginLat = -15.8402;
    private const double OriginLon = -70.0219;

    // ~110 m per 0.001° of latitude; ~107 m per 0.001° of longitude at this latitude.
    private const double BlockLat = 0.0018;
    private const double BlockLon = 0.0019;

    public static RoadNetwork Create()
    {
        string[] rows = ["Jr. Puno", "Jr. Lima", "Jr. Moquegua", "Jr. Arequipa"];
        string[] cols = ["Av. Titicaca", "Jr. Deústua", "Jr. Ayacucho", "Av. El Sol"];

        var nodes = new List<RoadNode>();
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                nodes.Add(new RoadNode(
                    $"N{r}{c}",
                    $"{cols[c]} / {rows[r]}",
                    c * 100,
                    r * 100,
                    Math.Round(OriginLat + (1.5 - r) * BlockLat, 6),
                    Math.Round(OriginLon + (c - 1.5) * BlockLon, 6)));
            }
        }

        var segments = new List<RoadSegment>();

        // East-west streets: the two outer ones are arterials with two lanes per direction.
        for (var r = 0; r < 4; r++)
        {
            var arterial = r is 0 or 3;
            for (var c = 0; c < 3; c++)
            {
                segments.Add(new RoadSegment(
                    $"H{r}{c}", rows[r], $"N{r}{c}", $"N{r}{c + 1}", 0.20,
                    arterial ? 40 : 30, arterial ? 2 : 1, arterial ? 900 : 700));
            }
        }

        // North-south streets: Av. Titicaca and Av. El Sol are the arterials.
        for (var c = 0; c < 4; c++)
        {
            var arterial = c is 0 or 3;
            for (var r = 0; r < 3; r++)
            {
                segments.Add(new RoadSegment(
                    $"V{r}{c}", cols[c], $"N{r}{c}", $"N{r + 1}{c}", 0.20,
                    arterial ? 40 : 30, arterial ? 2 : 1, arterial ? 900 : 700));
            }
        }

        TripDemand[] demand =
        [
            new("N00", "N33", 900),
            new("N33", "N00", 800),
            new("N03", "N30", 700),
            new("N30", "N03", 650),
            new("N10", "N13", 500),
            new("N13", "N10", 450),
            new("N01", "N32", 400),
            new("N31", "N02", 400)
        ];

        return new RoadNetwork(
            "Red sintética · centro de Puno",
            "Malla de demostración de 16 intersecciones alrededor de la Plaza de Armas. Los nombres orientan; la geometría, las capacidades y la demanda son ilustrativas, no provienen de aforos.",
            nodes,
            segments,
            demand);
    }
}
