using PaveLens.Domain.Inspections;
using Microsoft.Extensions.Options;

namespace PaveLens.Application.Pavement;

/// <summary>Patch quantities from the ground measurement (PLANIFICACION §4.D) and ASTM pothole severity.</summary>
public sealed class QuantityEstimator(IOptions<PavementOptions> options)
{
    private readonly PavementOptions _options = options.Value;

    public double AssumedDepthM(Severity severity) => severity switch
    {
        Severity.Low => _options.AssumedDepthLowM,
        Severity.Medium => _options.AssumedDepthMediumM,
        _ => _options.AssumedDepthHighM
    };

    /// <summary>A_cut = (L + 2m)(W + 2m), V = A_cut·d, M = V·ρ·(1 + f).</summary>
    public DistressQuantities Estimate(DistressMeasurement measurement, double depthM, DepthSource source)
    {
        var margin = 2 * _options.PatchMarginM;
        var cutArea = (measurement.LengthM + margin) * (measurement.WidthM + margin);
        var volume = cutArea * depthM;
        var mass = volume * _options.HmaDensityTPerM3 * (1 + _options.WasteFactor);
        return new DistressQuantities(
            Math.Round(depthM, 4),
            source,
            Math.Round(cutArea, 3),
            Math.Round(volume, 4),
            Math.Round(mass, 4));
    }

    /// <summary>Linear metres of saw cut around the rectangular patch.</summary>
    public double CutPerimeterM(DistressMeasurement measurement)
    {
        var margin = 2 * _options.PatchMarginM;
        return 2 * (measurement.LengthM + margin + measurement.WidthM + margin);
    }

    /// <summary>
    /// ASTM D6433 pothole severity from maximum depth and mean diameter. Used only with a measured depth;
    /// otherwise the model's visual severity stands.
    /// </summary>
    public static Severity PotholeSeverity(double depthM, double diameterM)
    {
        var depthMm = depthM * 1000;
        var column = (diameterM * 1000) switch
        {
            < 200 => 0,
            < 450 => 1,
            _ => 2
        };

        Severity[] row = depthMm switch
        {
            <= 25 => [Severity.Low, Severity.Low, Severity.Medium],
            <= 50 => [Severity.Low, Severity.Medium, Severity.High],
            _ => [Severity.Medium, Severity.Medium, Severity.High]
        };
        return row[column];
    }
}
