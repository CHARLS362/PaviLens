using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Pavement;

/// <summary>ASTM D6433 distress groups handled by PaveLens. Longitudinal and transverse cracks form one group.</summary>
public enum PciDistress
{
    AlligatorCracking,
    LongitudinalTransverseCracking,
    Pothole,
    Rutting
}

/// <summary>How a group's quantity is measured, which sets the density units.</summary>
public enum QuantityUnit
{
    SquareMetres,
    Metres,
    Count
}

public sealed record PciInput(DistressType Type, Severity Severity, double AreaM2, double LengthM);

public sealed record DeductEntry(PciDistress Distress, Severity Severity, double Quantity, QuantityUnit Unit, double DensityPct, double DeductValue);

public sealed record CdvIteration(int Q, double TotalDeductValue, double CorrectedDeductValue);

public sealed record PciResult(
    double Pci,
    string Rating,
    string Treatment,
    double SurveyedAreaM2,
    double SampleUnitAreaM2,
    bool IsPartial,
    double MaxAllowedDeducts,
    IReadOnlyList<DeductEntry> Deducts,
    IReadOnlyList<CdvIteration> Iterations);

/// <summary>
/// Pavement Condition Index after ASTM D6433 (PLANIFICACION §5.A). The standard publishes the deduct and
/// correction curves as charts; the anchors below are an approximate digitisation, interpolated linearly in
/// log10(density). Results are labelled as approximate.
/// </summary>
public static class PciCalculator
{
    /// <summary>Nominal ASTM D6433 asphalt sample unit (225 ± 90 m²).</summary>
    public const double StandardSampleUnitAreaM2 = 225;

    /// <summary>Lower bound of a sample unit. Below it the photo covers only part of a unit and the PCI is partial.</summary>
    public const double MinSampleUnitAreaM2 = 135;

    private static readonly double[] AreaDensities = [1, 10, 50];
    private static readonly double[] PotholeDensities = [0.1, 1, 10];

    private static readonly Dictionary<(PciDistress, Severity), double[]> DeductAnchors = new()
    {
        [(PciDistress.AlligatorCracking, Severity.Low)] = [10, 38, 58],
        [(PciDistress.AlligatorCracking, Severity.Medium)] = [18, 50, 72],
        [(PciDistress.AlligatorCracking, Severity.High)] = [25, 61, 82],
        [(PciDistress.LongitudinalTransverseCracking, Severity.Low)] = [3, 12, 25],
        [(PciDistress.LongitudinalTransverseCracking, Severity.Medium)] = [8, 25, 45],
        [(PciDistress.LongitudinalTransverseCracking, Severity.High)] = [18, 45, 70],
        [(PciDistress.Rutting, Severity.Low)] = [10, 30, 50],
        [(PciDistress.Rutting, Severity.Medium)] = [18, 43, 62],
        [(PciDistress.Rutting, Severity.High)] = [26, 55, 75],
        [(PciDistress.Pothole, Severity.Low)] = [18, 45, 85],
        [(PciDistress.Pothole, Severity.Medium)] = [30, 60, 95],
        [(PciDistress.Pothole, Severity.High)] = [45, 75, 100]
    };

    /// <summary>CDV / TDV for q = 1…7+, read from the correction curves at TDV ≈ 100 (approximation).</summary>
    private static readonly double[] CorrectionRatios = [1.00, 0.71, 0.62, 0.56, 0.52, 0.48, 0.46];

    /// <remarks>
    /// When the photo covers less than a sample unit, densities are taken over a standard 225 m² unit and the
    /// unseen part is assumed free of distress. The result is then an upper bound of the unit's PCI, flagged as partial.
    /// </remarks>
    public static PciResult Compute(IEnumerable<PciInput> distresses, double surveyedAreaM2)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(surveyedAreaM2);
        var isPartial = surveyedAreaM2 < MinSampleUnitAreaM2;
        var unitArea = isPartial ? StandardSampleUnitAreaM2 : surveyedAreaM2;

        var deducts = distresses
            .Select(d => (Group: GroupOf(d.Type), d.Severity, Quantity: QuantityOf(d)))
            .Where(d => d.Group is not null)
            .GroupBy(d => (Group: d.Group!.Value, d.Severity))
            .Select(g =>
            {
                var quantity = g.Sum(d => d.Quantity);
                var density = Math.Min(quantity / unitArea * 100, 100);
                var dv = DeductValue(g.Key.Group, g.Key.Severity, density);
                return new DeductEntry(g.Key.Group, g.Key.Severity, Math.Round(quantity, 3), UnitOf(g.Key.Group),
                    Math.Round(density, 3), Math.Round(dv, 2));
            })
            .OrderByDescending(d => d.DeductValue)
            .ToList();

        var (cdv, m, iterations) = CorrectedDeductValue(deducts.Select(d => d.DeductValue).ToList());
        var pci = Math.Round(Math.Clamp(100 - cdv, 0, 100), 1);
        var (rating, treatment) = Classify(pci);
        return new PciResult(pci, rating, treatment, Math.Round(surveyedAreaM2, 2), Math.Round(unitArea, 2),
            isPartial, Math.Round(m, 2), deducts, iterations);
    }

    /// <summary>Rating band and intervention from PLANIFICACION §5.A.</summary>
    public static (string Rating, string Treatment) Classify(double pci) => pci switch
    {
        >= 85 => ("Excelente", "Mantenimiento preventivo y sellado de microfisuras"),
        >= 70 => ("Bueno", "Bacheo superficial focalizado y sellado de juntas"),
        >= 55 => ("Regular", "Bacheo profundo en caliente y fresado de huellas"),
        >= 40 => ("Malo", "Recapeo asfáltico (overlay) de 5 cm"),
        _ => ("Fallado", "Reconstrucción de base granular y carpeta")
    };

    internal static double DeductValue(PciDistress group, Severity severity, double densityPct)
    {
        if (densityPct <= 0)
        {
            return 0;
        }

        var xs = (group == PciDistress.Pothole ? PotholeDensities : AreaDensities).Select(Math.Log10).ToArray();
        var ys = DeductAnchors[(group, severity)];
        var x = Math.Log10(densityPct);

        if (x <= xs[0])
        {
            // Fade to zero two decades below the first anchor.
            return Math.Max(0, ys[0] * (x - (xs[0] - 2)) / 2);
        }

        for (var i = 1; i < xs.Length; i++)
        {
            if (x <= xs[i])
            {
                return Lerp(xs[i - 1], ys[i - 1], xs[i], ys[i], x);
            }
        }

        var n = xs.Length - 1;
        return Math.Min(100, Lerp(xs[n - 1], ys[n - 1], xs[n], ys[n], x));
    }

    /// <summary>ASTM iteration: keep the m highest deducts, then reduce the smallest one above 2 to 2 until q = 1.</summary>
    internal static (double Cdv, double M, IReadOnlyList<CdvIteration> Iterations) CorrectedDeductValue(IReadOnlyList<double> deductValues)
    {
        var values = deductValues.Where(v => v > 0).OrderByDescending(v => v).ToList();
        if (values.Count == 0)
        {
            return (0, 10, []);
        }

        var m = Math.Min(1 + 9.0 / 98 * (100 - values[0]), 10);
        if (values.Count > m)
        {
            var whole = (int)Math.Floor(m);
            var kept = values.Take(whole).ToList();
            kept.Add(values[whole] * (m - whole));
            values = kept;
        }

        var iterations = new List<CdvIteration>();
        while (true)
        {
            var q = values.Count(v => v > 2);
            var tdv = values.Sum();
            var cdv = q <= 1 ? tdv : tdv * CorrectionRatios[Math.Min(q, CorrectionRatios.Length) - 1];
            iterations.Add(new CdvIteration(Math.Max(q, 1), Math.Round(tdv, 2), Math.Round(Math.Min(cdv, 100), 2)));
            if (q <= 1)
            {
                break;
            }

            var smallest = values.Select((v, i) => (v, i)).Where(t => t.v > 2).MinBy(t => t.v).i;
            values[smallest] = 2;
        }

        return (iterations.Max(i => i.CorrectedDeductValue), m, iterations);
    }

    private static PciDistress? GroupOf(DistressType type) => type switch
    {
        DistressType.AlligatorCrack => PciDistress.AlligatorCracking,
        DistressType.LongitudinalCrack or DistressType.TransverseCrack => PciDistress.LongitudinalTransverseCracking,
        DistressType.Pothole => PciDistress.Pothole,
        DistressType.Rutting => PciDistress.Rutting,
        _ => null
    };

    private static QuantityUnit UnitOf(PciDistress group) => group switch
    {
        PciDistress.LongitudinalTransverseCracking => QuantityUnit.Metres,
        PciDistress.Pothole => QuantityUnit.Count,
        _ => QuantityUnit.SquareMetres
    };

    private static double QuantityOf(PciInput d) => GroupOf(d.Type) switch
    {
        PciDistress.LongitudinalTransverseCracking => d.LengthM,
        PciDistress.Pothole => 1,
        _ => d.AreaM2
    };

    private static double Lerp(double x0, double y0, double x1, double y1, double x) =>
        y0 + (y1 - y0) * (x - x0) / (x1 - x0);
}
