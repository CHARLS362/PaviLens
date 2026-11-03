using System.Net;
using System.Net.Http.Json;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Application.Inspections;
using PaveLens.Application.Traffic;
using PaveLens.Domain.Inspections;
using PaveLens.Infrastructure.Ai.Simulation;
using static PaveLens.IntegrationTests.PaveLensApiFactory;

namespace PaveLens.IntegrationTests;

public sealed class InspectionApiTests(PaveLensApiFactory factory) : IClassFixture<PaveLensApiFactory>
{
    private const string ObliqueCalibration =
        """{"corners":[[0.08,0.95],[0.92,0.95],[0.60,0.42],[0.40,0.42]],"widthM":7,"lengthM":25}""";

    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<InspectionDetailDto> CreateAndWaitAsync(MultipartFormDataContent form)
    {
        using var response = await _client.PostAsync("/api/v1/inspections", form, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<InspectionDetailDto>(Json, Ct);
        return await WaitForAnalysisAsync(_client, created!.Id, Ct);
    }

    private Task<InspectionDetailDto> CreatePotholeAsync() =>
        CreateAndWaitAsync(Upload("av-el-sol-bache.jpg", referenceWidthM: 3.0, roadSegmentId: "V13"));

    [Fact]
    public async Task EveryStepSaysWhatItFoundAndReviewerIssuesStayApartFromAssumptions()
    {
        var run = (await CreatePotholeAsync()).Trace!.Orchestration!;

        Assert.All(run.Actions, a => Assert.False(string.IsNullOrWhiteSpace(a.Summary)));
        Assert.Contains("zonas dañadas", run.Actions.Single(a => a.Tool == "segment_image").Summary, StringComparison.Ordinal);
        Assert.StartsWith("PCI ", run.Actions.Single(a => a.Tool == "compute_report").Summary, StringComparison.Ordinal);
        var issue = Assert.Single(run.ReviewIssues!);
        Assert.DoesNotContain(issue, run.Warnings);
        Assert.Contains(run.Warnings, w => w.StartsWith("Profundidad supuesta", StringComparison.Ordinal));
        Assert.Equal("NeedsReview", run.Status);
    }

    [Fact]
    public async Task NadirPhotoIsMeasuredInMetresWithAssumedDepth()
    {
        var detail = await CreatePotholeAsync();

        Assert.Equal(InspectionStatus.Completed, detail.Status);
        Assert.Equal(AnalysisEngine.Simulation, detail.Engine);
        Assert.Equal("V13", detail.RoadSegmentId);

        var b1 = detail.Distresses.Single(d => d.Code == "B1");
        Assert.Equal(DistressType.Pothole, b1.Type);
        Assert.Equal("D40", b1.RddCode);
        Assert.InRange(b1.Measurement.AreaM2, 0.53, 0.58); // drawn with 0.554 m² on the ground
        Assert.Equal(DepthSource.Assumed, b1.Quantities!.DepthSource);
        Assert.Null(detail.Distresses.Single(d => d.Code == "FL1").Quantities);

        Assert.Equal(6.75, detail.SurveyedAreaM2!.Value, 6);
        Assert.True(detail.Pci!.IsPartial);
        Assert.InRange(detail.Pci.Pci, 0, 100);

        // The trace names every SAM 3 concept that was prompted, with how many masks each returned.
        Assert.Null(detail.Stage);
        var trace = detail.Trace!;
        Assert.Equal(1, trace.ReasoningAttempts);
        Assert.Equal(0, trace.SkippedBeyondHorizon);
        Assert.Contains(trace.Concepts, c => c.Concept == "rutting");
        Assert.Equal(detail.Distresses.Count, trace.Concepts.Sum(c => c.Masks));
    }

    [Fact]
    public async Task ObliquePhotoUsesTheCalibrationHomography()
    {
        var detail = await CreateAndWaitAsync(Upload("jr-lima-tramo.jpg", calibration: ObliqueCalibration));

        Assert.Equal(InspectionStatus.Completed, detail.Status);
        Assert.Equal(175, detail.SurveyedAreaM2!.Value, 6);
        Assert.False(detail.Pci!.IsPartial);
        Assert.InRange(detail.Distresses.Single(d => d.Code == "B1").Measurement.AreaM2, 0.35, 0.41);
        Assert.InRange(detail.Distresses.Single(d => d.Code == "PC1").Measurement.AreaM2, 5.2, 5.8);

        // The rut runs along the road: its major axis is longitudinal (±90° from the cross-road axis).
        var rut = detail.Distresses.Single(d => d.Code == "AH1");
        Assert.InRange(Math.Abs(rut.Measurement.OrientationDeg), 85, 90);
        Assert.True(rut.Measurement.Eccentricity > 0.95);
    }

    [Fact]
    public async Task MasksBeyondTheCalibrationHorizonAreSkippedNotFatal()
    {
        // A new hash falls back to the generic scene, whose crack reaches y = 0.2: above this calibration's horizon.
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", "jr-lima-tramo.jpg"));
        bytes[^3] ^= 0x55;
        var image = new ByteArrayContent(bytes);
        image.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Oblicua sin escena"), "title" },
            { image, "image", "nueva.jpg" },
            { new StringContent(ObliqueCalibration), "calibration" }
        };

        var detail = await CreateAndWaitAsync(form);

        Assert.Equal(InspectionStatus.Completed, detail.Status);
        Assert.Equal("B1", detail.Distresses.Single().Code);
        Assert.DoesNotContain(detail.RepairSteps, s => s.TargetCode is not null && s.TargetCode != "B1");
        Assert.Contains("horizonte", detail.Summary, StringComparison.Ordinal);
        Assert.Single(detail.Pci!.Deducts);
        Assert.True(detail.Trace!.SkippedBeyondHorizon > 0);
    }

    public static TheoryData<string> Scenes() => new(
        System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "manifest.json")))!
            .AsArray().Select(e => (string)e!["file"]!));

    [Theory]
    [MemberData(nameof(Scenes))]
    public async Task EveryMaskMeasuresTheDrawnGroundAreaWithinOnePercent(string file)
    {
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "manifest.json")))!.AsArray();
        var entry = manifest.Single(e => (string)e!["file"]! == file)!;
        var calibration = entry["calibration"]?.ToJsonString();
        var width = entry["referenceWidthM"]?.GetValue<double>();

        var detail = await CreateAndWaitAsync(Upload(file, referenceWidthM: width, calibration: calibration));

        var truth = entry["groundTruthAreaM2"]!.AsObject();
        Assert.Equal(truth.Count, detail.Distresses.Count);
        foreach (var (code, area) in truth)
        {
            var expected = area!.GetValue<double>();
            var distress = detail.Distresses.Single(d => d.Code == code);
            // Areal distresses: within 1 %. A 2 cm crack 20 m away is 1–2 px wide in the oblique photo, so its
            // area is resolution-bound; cracks are rated by length (ASTM D6433), which is unaffected.
            var tolerance = distress.Type is DistressType.LongitudinalCrack or DistressType.TransverseCrack ? 0.05 : 0.01;
            var measured = distress.Measurement.AreaM2;
            Assert.True(Math.Abs(measured - expected) <= tolerance * expected + 0.001,
                $"{file} {code}: {measured} m² medido vs {expected} m² real");
        }
    }

    [Fact]
    public async Task MeasuredDepthRecomputesQuantitiesSeverityAndPci()
    {
        var detail = await CreatePotholeAsync();
        var b1 = detail.Distresses.Single(d => d.Code == "B1");

        using var response = await _client.PutAsJsonAsync(
            $"/api/v1/inspections/{detail.Id}/distresses/{b1.Id}/depth", new { depthM = 0.02 }, Ct);
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<DistressDto>(Json, Ct);

        Assert.Equal(DepthSource.Measured, updated!.Quantities!.DepthSource);
        Assert.Equal(0.02, updated.Quantities.DepthM);
        Assert.Equal(Severity.Medium, updated.Severity); // ≤ 25 mm deep, ≥ 450 mm wide
        var reloaded = await _client.GetFromJsonAsync<InspectionDetailDto>($"/api/v1/inspections/{detail.Id}", Json, Ct);
        Assert.True(reloaded!.Pci!.Pci > detail.Pci!.Pci);
    }

    [Fact]
    public async Task DepthOnACrackIsAConflictAndOutOfRangeDepthIsInvalid()
    {
        var detail = await CreatePotholeAsync();
        var crack = detail.Distresses.Single(d => d.Code == "FL1");
        var b1 = detail.Distresses.Single(d => d.Code == "B1");

        using var conflict = await _client.PutAsJsonAsync(
            $"/api/v1/inspections/{detail.Id}/distresses/{crack.Id}/depth", new { depthM = 0.03 }, Ct);
        using var invalid = await _client.PutAsJsonAsync(
            $"/api/v1/inspections/{detail.Id}/distresses/{b1.Id}/depth", new { depthM = 2.0 }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task BudgetIsComputedFromTheStoredQuantities()
    {
        var detail = await CreatePotholeAsync();

        var budget = await _client.GetFromJsonAsync<BudgetDto>($"/api/v1/inspections/{detail.Id}/budget", Json, Ct);

        Assert.Equal("PEN", budget!.Currency);
        Assert.Contains(budget.Lines, l => l.Item == "04" && l.Unit == "t");
        Assert.Contains(budget.Lines, l => l.Item == "06");
        Assert.Equal(budget.Subtotal + budget.Igv, budget.Total);
        Assert.Equal(1, budget.AssumedDepthCount);
        Assert.Contains("Puno", budget.Region.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnglishReportKeepsTheGeometryAndTranslatesTheText()
    {
        var detail = await CreateAndWaitAsync(Upload("jr-lima-tramo.jpg", calibration: ObliqueCalibration, language: "en"));

        Assert.Equal(ReportLanguage.En, detail.Language);
        Assert.StartsWith("[Simulation]", detail.Summary, StringComparison.Ordinal);
        var b1 = detail.Distresses.Single(d => d.Code == "B1");
        Assert.StartsWith("Pothole", b1.Diagnosis, StringComparison.Ordinal);
        Assert.InRange(b1.Measurement.AreaM2, 0.35, 0.41);
        Assert.Equal("Sign and close one lane", detail.RepairSteps[0].Title);
    }

    [Fact]
    public async Task UnknownReportLanguageIsAValidationProblem()
    {
        using var response = await _client.PostAsync("/api/v1/inspections",
            Upload("jr-lima-tramo.jpg", language: "fr"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidCalibrationIsAValidationProblem()
    {
        using var response = await _client.PostAsync("/api/v1/inspections",
            Upload("jr-lima-tramo.jpg", calibration: "{no es json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task IdenticalUploadsShareOneStoredImage()
    {
        var first = await CreateAndWaitAsync(Upload("jr-arequipa-cocodrilo.jpg", referenceWidthM: 4));
        var second = await CreateAndWaitAsync(Upload("jr-arequipa-cocodrilo.jpg", referenceWidthM: 4));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Image.Id, second.Image.Id);
    }

    [Fact]
    public async Task ImageIsServedWithEtagAndHonoursIfNoneMatch()
    {
        var detail = await CreatePotholeAsync();

        using var response = await _client.GetAsync(detail.Image.Url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.Public);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, detail.Image.Url);
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var notModified = await _client.SendAsync(conditional, Ct);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    [Fact]
    public async Task StepCompletionIsPersisted()
    {
        var detail = await CreatePotholeAsync();
        var step = detail.RepairSteps[0];

        using var response = await _client.PutAsJsonAsync(
            $"/api/v1/inspections/{detail.Id}/steps/{step.Id}/completion", new { completed = true }, Ct);
        response.EnsureSuccessStatusCode();

        var reloaded = await _client.GetFromJsonAsync<InspectionDetailDto>($"/api/v1/inspections/{detail.Id}", Json, Ct);
        Assert.NotNull(reloaded!.RepairSteps.Single(s => s.Id == step.Id).CompletedAt);
    }

    [Fact]
    public async Task QuestionsAndAnswersArePersisted()
    {
        var detail = await CreatePotholeAsync();
        var b1 = detail.Distresses.Single(d => d.Code == "B1");

        using var response = await _client.PostAsJsonAsync($"/api/v1/inspections/{detail.Id}/conversation",
            new { question = "¿Cuánta mezcla necesito para B1?", distressId = b1.Id }, Ct);
        response.EnsureSuccessStatusCode();

        var conversation = await _client.GetFromJsonAsync<List<ConversationMessageDto>>(
            $"/api/v1/inspections/{detail.Id}/conversation?distressId={b1.Id}", Json, Ct);
        Assert.NotNull(conversation);
        Assert.Equal([MessageRole.User, MessageRole.Assistant], conversation.Select(m => m.Role));
        Assert.Contains("B1", conversation[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListIsPagedNewestFirstWithPci()
    {
        await CreatePotholeAsync();
        var newest = await CreateAndWaitAsync(Upload("jr-arequipa-cocodrilo.jpg", "Más reciente", referenceWidthM: 4));

        var page = await _client.GetFromJsonAsync<PagedResult<InspectionSummaryDto>>("/api/v1/inspections?pageSize=1", Json, Ct);

        Assert.Equal(newest.Id, page!.Items.Single().Id);
        Assert.NotNull(page.Items.Single().Pci);
        Assert.True(page.TotalCount >= 2);
    }

    [Fact]
    public async Task DeleteRemovesTheInspection()
    {
        var detail = await CreatePotholeAsync();

        using var deleted = await _client.DeleteAsync($"/api/v1/inspections/{detail.Id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var missing = await _client.GetAsync($"/api/v1/inspections/{detail.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task DeleteRemovesQuestionsAboutItsDistresses()
    {
        var detail = await CreatePotholeAsync();
        var b1 = detail.Distresses.Single(d => d.Code == "B1");
        using var asked = await _client.PostAsJsonAsync($"/api/v1/inspections/{detail.Id}/conversation",
            new { question = "¿Cuánta mezcla necesito para B1?", distressId = b1.Id }, Ct);
        asked.EnsureSuccessStatusCode();

        using var deleted = await _client.DeleteAsync($"/api/v1/inspections/{detail.Id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var missing = await _client.GetAsync($"/api/v1/inspections/{detail.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task NonImageUploadIsRejectedAsValidationProblem()
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("Falsa"), "title" },
            { new ByteArrayContent("esto no es una imagen"u8.ToArray()), "image", "fake.jpg" }
        };

        using var response = await _client.PostAsync("/api/v1/inspections", form, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RetryingACompletedInspectionIsAConflict()
    {
        var detail = await CreatePotholeAsync();

        using var response = await _client.PostAsync($"/api/v1/inspections/{detail.Id}/retry", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpointsReportHealthy()
    {
        Assert.Equal("Healthy", await _client.GetStringAsync("/health", Ct));
        Assert.Equal("Healthy", await _client.GetStringAsync("/health/ready", Ct));
    }
}

public sealed class TrafficApiTests(PaveLensApiFactory factory) : IClassFixture<PaveLensApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NetworkIsServed()
    {
        var network = await _client.GetFromJsonAsync<RoadNetwork>("/api/v1/network", Json, Ct);

        Assert.Equal(16, network!.Nodes.Count);
        Assert.Contains(network.Segments, s => s.Id == "H11");
        Assert.NotEmpty(network.Demand);
    }

    [Fact]
    public async Task WorkZoneSimulationComparesBothEquilibria()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/traffic/simulate", new { segmentId = "V00", lanesClosed = 1 }, Ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TrafficSimulationDto>(Json, Ct);

        Assert.True(result!.WorkZone.TotalTravelTimeVehH > result.Baseline.TotalTravelTimeVehH);
        Assert.All(result.Links.Where(l => l.SegmentId == "V00"), l => Assert.Equal(l.CapacityVph / 2, l.WorkZoneCapacityVph));
    }

    [Fact]
    public async Task NightWindowAndDurationAreAccepted()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/traffic/simulate",
            new { segmentId = "V00", lanesClosed = 1, period = "Night", durationH = 6 }, Ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TrafficSimulationDto>(Json, Ct);

        Assert.Equal(WorkPeriod.Night, result!.Period);
        Assert.Equal(6, result.DurationH);
        Assert.Equal(3, result.Windows.Count);
    }

    [Fact]
    public async Task TrafficPlanReturnsValidatedRoutesAndSignage()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/traffic/plan",
            new { segmentId = "H11", lanesClosed = 1, period = "OffPeak", durationH = 6 }, Ct);
        response.EnsureSuccessStatusCode();
        var plan = await response.Content.ReadFromJsonAsync<TrafficPlanDto>(Json, Ct);

        Assert.Equal("Simulation", plan!.Engine);
        Assert.Equal(2, plan.Routes.Count);
        Assert.All(plan.Routes, r => Assert.DoesNotContain("H11", r.SegmentIds));
        Assert.Contains(plan.Signage, s => s.NodeId == "N11");
        Assert.Contains(plan.Signage, s => s.NodeId == "N12");
        Assert.NotEmpty(plan.Phases);
        Assert.Equal(WorkPeriod.OffPeak, plan.Simulation.Period);
    }

    [Fact]
    public async Task TrafficPlanIsWrittenInTheRequestedLanguage()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/traffic/plan",
            new { segmentId = "H11", lanesClosed = 1, language = "En" }, Ct);
        response.EnsureSuccessStatusCode();
        var plan = await response.Content.ReadFromJsonAsync<TrafficPlanDto>(Json, Ct);

        Assert.StartsWith("[Simulation]", plan!.Summary, StringComparison.Ordinal);
        Assert.All(plan.Routes, r =>
        {
            Assert.Matches(@"^(Detour|Alternative) heading (north|south|east|west) via ", r.Name);
            Assert.DoesNotContain("hacia", r.Name, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AiStatusNeverExposesKeys()
    {
        var body = await _client.GetStringAsync("/api/v1/ai/status", Ct);

        Assert.Contains("\"provider\":\"Simulation\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownSegmentIsAValidationProblem()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/traffic/simulate", new { segmentId = "X99", lanesClosed = 1 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

/// <summary>Intact pavement inside a mask is measured out, stored and returned for drawing.</summary>
public sealed class MaskHoleTests
{
    /// <summary>The simulated fixture with a small square hole punched in the middle of the pothole.</summary>
    private sealed class HoleSegmenter : IDistressSegmenter
    {
        private readonly SimulatedSegmenter _inner = new(new SimulationCatalog());
        public AnalysisEngine Engine => _inner.Engine;
        public bool Punch { get; set; }

        public async Task<SegmentationResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken)
        {
            var result = await _inner.SegmentAsync(request, cancellationToken);
            var segments = result.Segments.Select(s =>
            {
                if (!Punch || s.Concept != "pothole") return s;
                var (cx, cy) = (s.Polygon.Average(p => p.X), s.Polygon.Average(p => p.Y));
                NormalizedPoint[] hole = [new(cx - 0.01, cy - 0.01), new(cx - 0.01, cy + 0.01), new(cx + 0.01, cy + 0.01), new(cx + 0.01, cy - 0.01)];
                return s with { Holes = [hole] };
            }).ToList();
            return result with { Segments = segments };
        }
    }

    [Fact]
    public async Task AHoleIsSubtractedPersistedAndReturned()
    {
        var segmenter = new HoleSegmenter();
        await using var factory = new PaveLensApiFactory { Segmenter = segmenter };
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        async Task<InspectionDetailDto> AnalyseAsync()
        {
            using var response = await client.PostAsync("/api/v1/inspections", Upload("av-el-sol-bache.jpg", referenceWidthM: 3.0), ct);
            var created = await response.Content.ReadFromJsonAsync<InspectionDetailDto>(Json, ct);
            return await WaitForAnalysisAsync(client, created!.Id, ct);
        }

        var solid = (await AnalyseAsync()).Distresses.Single(d => d.Code == "B1");
        segmenter.Punch = true;
        var detail = await AnalyseAsync();

        Assert.Equal(InspectionStatus.Completed, detail.Status);
        var b1 = detail.Distresses.Single(d => d.Code == "B1");
        var hole = Assert.Single(b1.Holes!);
        Assert.Equal(4, hole.Count);
        Assert.Empty(solid.Holes!);
        Assert.True(b1.Measurement.AreaM2 < solid.Measurement.AreaM2, $"{b1.Measurement.AreaM2} m² con hueco, {solid.Measurement.AreaM2} m² sin él");
        Assert.Empty(detail.Distresses.Single(d => d.Code == "FL1").Holes!);
    }
}

/// <summary>A model outage must fail the inspection with a reason, never fall back to fake data.</summary>
public sealed class ModelFailureTests
{
    private sealed class UnavailableSegmenter : IDistressSegmenter
    {
        public AnalysisEngine Engine => AnalysisEngine.Meta;

        public Task<SegmentationResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken) =>
            throw new ModelUnavailableException("SAM 3 no responde.");
    }

    [Fact]
    public async Task SegmentationOutageMarksTheInspectionFailed()
    {
        await using var factory = new PaveLensApiFactory { Segmenter = new UnavailableSegmenter() };
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        using var response = await client.PostAsync("/api/v1/inspections", Upload("av-el-sol-bache.jpg"), ct);
        var created = await response.Content.ReadFromJsonAsync<InspectionDetailDto>(Json, ct);
        var detail = await WaitForAnalysisAsync(client, created!.Id, ct);

        Assert.Equal(InspectionStatus.Failed, detail.Status);
        Assert.Contains("SAM 3", detail.FailureReason, StringComparison.Ordinal);
        Assert.Empty(detail.Distresses);
    }
}
