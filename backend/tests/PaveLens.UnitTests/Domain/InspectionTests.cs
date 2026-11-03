using PaveLens.Domain.Common;
using PaveLens.Domain.Inspections;

namespace PaveLens.UnitTests.Domain;

public class InspectionTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly NormalizedPoint[] Square =
        [new(0.1, 0.1), new(0.3, 0.1), new(0.3, 0.3), new(0.1, 0.3)];

    private static readonly DistressMeasurement Measured = new(0.49, 2.8, 0.7, 0.7, 0.1, 0);

    private static Distress Pothole(string code) =>
        Distress.Create(code, DistressType.Pothole, Severity.Medium, 0.9, Square, Square.Length, Measured,
            new DistressQuantities(0.05, DepthSource.Assumed, 1, 0.05, 0.12), "pothole", null, null);

    private static Distress Crack(string code) =>
        Distress.Create(code, DistressType.LongitudinalCrack, Severity.Low, 0.9, Square, Square.Length, Measured,
            null, "longitudinal crack", null, null);

    private static Inspection Processing()
    {
        var inspection = Inspection.Create("Jr. Lima", null, null, null, Guid.NewGuid(), null, null, ReportLanguage.Es, Now);
        inspection.BeginProcessing(Now);
        return inspection;
    }

    private static void Complete(Inspection inspection, IEnumerable<Distress> distresses, IEnumerable<RepairStep> steps) =>
        inspection.Complete(AnalysisEngine.Simulation, "seg", "reason", "Resumen", distresses, steps, 200, 80, Trace, Now.AddSeconds(2), 2000);

    private static readonly AnalysisTrace Trace = new([new ConceptTrace("pothole", 1)], 900, 1500, 1, 0);

    [Fact]
    public void StageAdvancesOnlyForwardWhileProcessingAndClearsOnCompletion()
    {
        var inspection = Processing();
        Assert.Equal(AnalysisStage.Segmenting, inspection.Stage);

        inspection.AdvanceTo(AnalysisStage.Reasoning);
        Assert.Throws<DomainException>(() => inspection.AdvanceTo(AnalysisStage.Measuring));

        Complete(inspection, [], []);
        Assert.Null(inspection.Stage);
        Assert.Equal(1, inspection.Trace!.ReasoningAttempts);
        Assert.Throws<DomainException>(() => inspection.AdvanceTo(AnalysisStage.Computing));
    }

    [Fact]
    public void TraceRejectsImpossibleValues()
    {
        Assert.Throws<DomainException>(() => new AnalysisTrace([], 1, 1, 1, 0));
        Assert.Throws<DomainException>(() => new AnalysisTrace([new ConceptTrace("pothole", 0)], 1, 1, 0, 0));
    }

    [Fact]
    public void NewInspectionIsPendingWithOneLaneAsDefaultScale()
    {
        var inspection = Inspection.Create("Jr. Lima", "Bache", "Puno", "H11", Guid.NewGuid(), null, null, ReportLanguage.Es, Now);

        Assert.Equal(InspectionStatus.Pending, inspection.Status);
        Assert.Equal(Inspection.DefaultReferenceWidthM, inspection.ReferenceWidthM);
        Assert.Equal("H11", inspection.RoadSegmentId);
        Assert.Equal(0, inspection.Attempts);
    }

    [Fact]
    public void BeginProcessingTwiceIsRejected()
    {
        var inspection = Processing();

        Assert.Throws<DomainException>(() => inspection.BeginProcessing(Now));
    }

    [Fact]
    public void CompleteStoresDistressesStepsAndPci()
    {
        var inspection = Processing();

        Complete(inspection, [Pothole("B1"), Crack("F1")],
            [RepairStep.Create(1, "Bachear B1", "Corte y relleno", ["Cortadora"], RiskLevel.Medium, "B1")]);

        Assert.Equal(InspectionStatus.Completed, inspection.Status);
        Assert.Equal(2, inspection.Distresses.Count);
        Assert.Single(inspection.RepairSteps);
        Assert.Equal(80, inspection.Pci);
        Assert.Equal(200, inspection.SurveyedAreaM2);
    }

    [Fact]
    public void CompleteRejectsDuplicateCodes()
    {
        var inspection = Processing();

        Assert.Throws<DomainException>(() => Complete(inspection, [Pothole("B1"), Pothole("B1")], []));
    }

    [Fact]
    public void CompleteRejectsStepsTargetingUnknownDistresses()
    {
        var inspection = Processing();

        Assert.Throws<DomainException>(() => Complete(inspection, [Pothole("B1")],
            [RepairStep.Create(1, "Bachear B9", "x", [], RiskLevel.Low, "B9")]));
    }

    [Fact]
    public void CracksCannotCarryPatchQuantities()
    {
        Assert.Throws<DomainException>(() => Distress.Create("F1", DistressType.TransverseCrack, Severity.Low, 0.9,
            Square, 4, Measured, new DistressQuantities(0.05, DepthSource.Assumed, 1, 0.05, 0.12), null, null, null));
    }

    [Fact]
    public void MeasuredDepthReplacesQuantitiesOnlyOnPatchableDistresses()
    {
        var inspection = Processing();
        var pothole = Pothole("B1");
        var crack = Crack("F1");
        Complete(inspection, [pothole, crack], []);

        inspection.UpdateDistressDepth(pothole.Id, Severity.High,
            new DistressQuantities(0.08, DepthSource.Measured, 1, 0.08, 0.2), 61);

        Assert.Equal(DepthSource.Measured, pothole.Quantities!.DepthSource);
        Assert.Equal(Severity.High, pothole.Severity);
        Assert.Equal(61, inspection.Pci);
        Assert.Throws<DomainException>(() => inspection.UpdateDistressDepth(crack.Id, Severity.Low,
            new DistressQuantities(0.02, DepthSource.Measured, 1, 0.02, 0.05), 61));
    }

    [Fact]
    public void OnlyFailedInspectionsCanBeRetried()
    {
        var inspection = Processing();
        Assert.Throws<DomainException>(inspection.Retry);

        inspection.Fail("Modelo caído", Now);
        inspection.Retry();

        Assert.Equal(InspectionStatus.Pending, inspection.Status);
        Assert.Null(inspection.FailureReason);
    }

    [Fact]
    public void StepCompletionRequiresCompletedInspection()
    {
        var inspection = Processing();
        var step = RepairStep.Create(1, "Paso", "x", [], RiskLevel.Low, null);
        Assert.Throws<DomainException>(() => inspection.SetStepCompletion(step.Id, true, Now));
        Complete(inspection, [Pothole("B1")], [step]);

        inspection.SetStepCompletion(step.Id, true, Now);
        Assert.NotNull(inspection.RepairSteps.Single().CompletedAt);

        inspection.SetStepCompletion(step.Id, false, Now);
        Assert.Null(inspection.RepairSteps.Single().CompletedAt);
    }

    [Fact]
    public void EveryMutationRotatesTheConcurrencyToken()
    {
        var inspection = Inspection.Create("Jr. Lima", null, null, null, Guid.NewGuid(), null, null, ReportLanguage.Es, Now);
        var before = inspection.Version;

        inspection.BeginProcessing(Now);

        Assert.NotEqual(before, inspection.Version);
    }

    [Fact]
    public void DegenerateCalibrationIsRejected()
    {
        NormalizedPoint[] collinear = [new(0.1, 0.9), new(0.5, 0.9), new(0.9, 0.9), new(0.3, 0.9)];

        Assert.Throws<DomainException>(() => new GroundCalibration(collinear, 7, 25));
    }

    [Fact]
    public void PointsOutsideTheImageAreRejected()
    {
        Assert.Throws<DomainException>(() => new NormalizedPoint(1.2, 0.5));
    }
}
