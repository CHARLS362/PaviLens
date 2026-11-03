using PaveLens.Application.Inspections;
using PaveLens.Domain.Inspections;
using Microsoft.Extensions.Options;

namespace PaveLens.Application.Pavement;

/// <summary>
/// Unit-price budget (APU) for the patching and sealing work of one inspection. Quantities come from the
/// stored measurements; prices from <see cref="PavementOptions.Prices"/>, so a price change needs no reanalysis.
/// </summary>
public sealed class CostEstimator(IOptions<PavementOptions> options, QuantityEstimator quantities)
{
    private readonly PavementOptions _options = options.Value;

    public BudgetDto Estimate(InspectionDetailDto inspection)
    {
        var prices = _options.Prices;
        var patches = inspection.Distresses.Where(d => d.Quantities is not null).ToList();
        var cracks = inspection.Distresses
            .Where(d => d.Type is DistressType.LongitudinalCrack or DistressType.TransverseCrack)
            .ToList();

        var cutLength = patches.Sum(d => quantities.CutPerimeterM(ToMeasurement(d.Measurement)));
        var cutArea = patches.Sum(d => d.Quantities!.CutAreaM2);
        var volume = patches.Sum(d => d.Quantities!.VolumeM3);
        var mass = patches.Sum(d => d.Quantities!.MassT);
        var sealLength = cracks.Sum(d => d.Measurement.LengthM);

        var lines = new List<BudgetLineDto>();
        void Add(string item, string description, string unit, double quantity, decimal unitPrice)
        {
            if (quantity <= 0) return;
            var rounded = Math.Round(quantity, 3);
            lines.Add(new BudgetLineDto(item, description, unit, rounded, unitPrice, Money((decimal)rounded * unitPrice)));
        }

        Add("01", "Corte perimetral rectangular con disco diamantado", "m", cutLength, prices.SawCutPerM);
        Add("02", "Demolición y eliminación de carpeta deteriorada", "m³", volume, prices.RemovalPerM3);
        Add("03", "Riego de liga con emulsión asfáltica", "m²", cutArea, prices.TackCoatPerM2);
        Add("04", "Mezcla asfáltica en caliente, suministro y colocación", "t", mass, prices.HotMixPerT);
        Add("05", "Compactación de la mezcla", "m²", cutArea, prices.CompactionPerM2);
        Add("06", "Sellado de fisuras", "m", sealLength, prices.CrackSealPerM);

        var direct = lines.Sum(l => l.Amount);
        var overhead = Money(direct * prices.OverheadPct / 100);
        var profit = Money(direct * prices.ProfitPct / 100);
        var subtotal = direct + overhead + profit;
        var igv = Money(subtotal * prices.IgvPct / 100);
        var assumed = patches.Count(d => d.Quantities!.DepthSource == DepthSource.Assumed);

        // Assumed depths are reported through AssumedDepthCount, which the client words and highlights.
        var notes = new List<string>
        {
            "Precios unitarios referenciales y configurables (Pavement:Prices): reemplázalos con el análisis de precios de la obra.",
            "Las cantidades salen del área visible proyectada al terreno; el corte añade un margen a cada lado del deterioro."
        };

        var region = _options.Region;
        return new BudgetDto(
            inspection.Id,
            prices.Currency,
            lines,
            direct,
            prices.OverheadPct,
            overhead,
            prices.ProfitPct,
            profit,
            subtotal,
            prices.IgvPct,
            igv,
            subtotal + igv,
            assumed,
            new RegionDto(region.Name, region.AltitudeM, region.RecommendedBinder, region.Notes),
            notes);
    }

    private static DistressMeasurement ToMeasurement(MeasurementDto m) =>
        new(m.AreaM2, m.PerimeterM, m.LengthM, m.WidthM, m.Eccentricity, m.OrientationDeg);

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
