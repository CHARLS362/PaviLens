using PaveLens.Application.Inspections;
using PaveLens.Application.Pavement;
using PaveLens.Domain.Inspections;
using Microsoft.Extensions.Options;

namespace PaveLens.UnitTests.Application;

public class PciCalculatorTests
{
    [Fact]
    public void NoDistressMeansPerfectPavement()
    {
        var result = PciCalculator.Compute([], 225);

        Assert.Equal(100, result.Pci);
        Assert.Equal("Excelente", result.Rating);
        Assert.False(result.IsPartial);
    }

    [Theory]
    [InlineData(PciDistress.AlligatorCracking, Severity.Low, 1, 10)]
    [InlineData(PciDistress.AlligatorCracking, Severity.High, 10, 61)]
    [InlineData(PciDistress.Pothole, Severity.Medium, 1, 60)]
    [InlineData(PciDistress.LongitudinalTransverseCracking, Severity.Medium, 50, 45)]
    public void DeductValueHitsTheAnchors(PciDistress group, Severity severity, double density, double expected)
    {
        Assert.Equal(expected, PciCalculator.DeductValue(group, severity, density), 9);
    }

    [Fact]
    public void DeductValueGrowsWithDensityAndSeverity()
    {
        var low = PciCalculator.DeductValue(PciDistress.Rutting, Severity.Low, 5);
        var lowDense = PciCalculator.DeductValue(PciDistress.Rutting, Severity.Low, 20);
        var high = PciCalculator.DeductValue(PciDistress.Rutting, Severity.High, 5);

        Assert.True(lowDense > low);
        Assert.True(high > low);
        Assert.Equal(0, PciCalculator.DeductValue(PciDistress.Rutting, Severity.Low, 0));
    }

    [Fact]
    public void SingleDeductIsNotCorrected()
    {
        var (cdv, _, iterations) = PciCalculator.CorrectedDeductValue([40]);

        Assert.Equal(40, cdv, 9);
        Assert.Single(iterations);
    }

    [Fact]
    public void IterationReducesSmallestDeductsToTwoAndKeepsTheMaximumCdv()
    {
        // q = 2: TDV 70 → CDV 0.71·70 = 49.7; then 30 → 2: q = 1, TDV 42 → CDV 42. Max = 49.7.
        var (cdv, m, iterations) = PciCalculator.CorrectedDeductValue([40, 30]);

        Assert.Equal(1 + 9.0 / 98 * 60, m, 9);
        Assert.Equal(2, iterations.Count);
        Assert.Equal(49.7, cdv, 6);
    }

    [Fact]
    public void OnlyTheAllowedNumberOfDeductsIsKept()
    {
        // HDV = 89 → m = 2.01: keep two values plus 1 % of the third.
        var (_, m, iterations) = PciCalculator.CorrectedDeductValue([89, 50, 40, 30]);

        Assert.Equal(1 + 9.0 / 98 * 11, m, 9);
        Assert.Equal(89 + 50 + 40 * (m - 2), iterations[0].TotalDeductValue, 2);
    }

    [Fact]
    public void SmallPhotoIsEvaluatedOverAStandardSampleUnitAndFlaggedPartial()
    {
        PciInput[] potholes = [new(DistressType.Pothole, Severity.High, 0.5, 0.8)];

        var result = PciCalculator.Compute(potholes, 9);

        Assert.True(result.IsPartial);
        Assert.Equal(PciCalculator.StandardSampleUnitAreaM2, result.SampleUnitAreaM2);
        Assert.Equal(100.0 / 225, result.Deducts.Single().DensityPct, 3);
        Assert.InRange(result.Pci, 1, 99);
    }

    [Fact]
    public void LongitudinalAndTransverseCracksShareOneGroupMeasuredInMetres()
    {
        PciInput[] cracks =
        [
            new(DistressType.LongitudinalCrack, Severity.Low, 0.2, 6),
            new(DistressType.TransverseCrack, Severity.Low, 0.1, 4)
        ];

        var entry = PciCalculator.Compute(cracks, 200).Deducts.Single();

        Assert.Equal(PciDistress.LongitudinalTransverseCracking, entry.Distress);
        Assert.Equal(QuantityUnit.Metres, entry.Unit);
        Assert.Equal(10, entry.Quantity);
        Assert.Equal(5, entry.DensityPct, 9);
    }

    [Theory]
    [InlineData(92, "Excelente")]
    [InlineData(70, "Bueno")]
    [InlineData(60, "Regular")]
    [InlineData(40, "Malo")]
    [InlineData(12, "Fallado")]
    public void RatingBands(double pci, string rating)
    {
        Assert.Equal(rating, PciCalculator.Classify(pci).Rating);
    }
}

public class QuantityAndCostTests
{
    private static readonly IOptions<PavementOptions> Options = Microsoft.Extensions.Options.Options.Create(new PavementOptions());

    [Fact]
    public void PatchQuantitiesFollowTheRectangularCut()
    {
        var estimator = new QuantityEstimator(Options);
        var measurement = new DistressMeasurement(0.4, 2.4, 0.8, 0.6, 0.5, 0);

        var q = estimator.Estimate(measurement, 0.05, DepthSource.Measured);

        // (0.8 + 0.3)(0.6 + 0.3) = 0.99 m²; V = 0.0495 m³; M = V · 2.35 · 1.05.
        Assert.Equal(0.99, q.CutAreaM2, 9);
        Assert.Equal(0.0495, q.VolumeM3, 9);
        Assert.Equal(Math.Round(0.0495 * 2.35 * 1.05, 4), q.MassT, 9);
        Assert.Equal(2 * (1.1 + 0.9), estimator.CutPerimeterM(measurement), 9);
    }

    [Theory]
    [InlineData(0.02, 0.15, Severity.Low)]
    [InlineData(0.02, 0.6, Severity.Medium)]
    [InlineData(0.04, 0.3, Severity.Medium)]
    [InlineData(0.04, 0.6, Severity.High)]
    [InlineData(0.07, 0.15, Severity.Medium)]
    [InlineData(0.07, 0.9, Severity.High)]
    public void PotholeSeverityFollowsTheAstmMatrix(double depthM, double diameterM, Severity expected)
    {
        Assert.Equal(expected, QuantityEstimator.PotholeSeverity(depthM, diameterM));
    }

    [Fact]
    public void BudgetAddsOverheadProfitAndIgv()
    {
        var estimator = new QuantityEstimator(Options);
        var measurement = new DistressMeasurement(0.4, 2.4, 0.8, 0.6, 0.5, 0);
        var q = estimator.Estimate(measurement, 0.05, DepthSource.Assumed);
        var pothole = new DistressDto(Guid.NewGuid(), "B1", DistressType.Pothole, "D40", Severity.Medium, 0.9,
            new BoxDto(0, 0, 0.1, 0.1), [], 4, null,
            new MeasurementDto(0.4, 2.4, 0.8, 0.6, 0.5, 0),
            new QuantitiesDto(q.DepthM, q.DepthSource, q.CutAreaM2, q.VolumeM3, q.MassT), null, null);
        var crack = pothole with
        {
            Id = Guid.NewGuid(), Code = "F1", Type = DistressType.TransverseCrack, Quantities = null,
            Measurement = new MeasurementDto(0.2, 8, 4, 0.05, 0.99, 0)
        };
        var detail = new InspectionDetailDto(Guid.NewGuid(), "t", null, null, null, InspectionStatus.Completed, null, null, ReportLanguage.Es,
            AnalysisEngine.Simulation, null, null, null, null, 3.5, null, new ImageDto(Guid.NewGuid(), "/x", 1, 1, "image/jpeg", 1), 1,
            DateTime.UtcNow, null, null, null, 9, null, [pothole, crack], []);

        var budget = new CostEstimator(Options, estimator).Estimate(detail);

        Assert.Equal(6, budget.Lines.Count);
        Assert.Equal(4 * 9.00m, budget.Lines.Single(l => l.Item == "06").Amount);
        Assert.Equal(budget.Lines.Sum(l => l.Amount), budget.DirectCost);
        Assert.Equal(budget.DirectCost + budget.Overhead + budget.Profit, budget.Subtotal);
        Assert.Equal(Math.Round(budget.Subtotal * 0.18m, 2), budget.Igv);
        Assert.Equal(1, budget.AssumedDepthCount);
    }
}
