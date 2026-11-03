using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Inspections;
using PaveLens.Application.Orchestration;
using PaveLens.Domain.Inspections;

namespace PaveLens.IntegrationTests;

public class OrchestrationApiTests
{
    [Fact]
    public async Task PersistsOrderedEvidenceAndAnImmutableRegionalSnapshot()
    {
        await using var factory = new PaveLensApiFactory();
        using var client = factory.CreateClient();
        using var form = PaveLensApiFactory.Upload("av-el-sol-bache.jpg");
        using var created = await client.PostAsync("/api/v1/inspections", form, TestContext.Current.CancellationToken);
        var pending = await created.Content.ReadFromJsonAsync<InspectionDetailDto>(PaveLensApiFactory.Json, TestContext.Current.CancellationToken);
        var detail = await PaveLensApiFactory.WaitForAnalysisAsync(client, pending!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(InspectionStatus.Completed, detail.Status);
        var run = Assert.IsType<AgentRunTrace>(detail.Trace!.Orchestration);
        Assert.Equal("Simulation", run.Engine);
        Assert.Equal("NeedsReview", run.Status);
        Assert.Equal(InspectionTools.Order, run.Actions.Select(a => a.Tool));
        Assert.All(run.Actions, a => Assert.Equal("Succeeded", a.Status));
        Assert.Equal("pe-puno", RegionalCatalog.Restore(run.RegionalProfileJson).Id);
        Assert.Equal(0, run.ModelCalls);
    }

    [Fact]
    public async Task AnInternationalReferenceDoesNotReusePeruvianPrices()
    {
        await using var factory = new PaveLensApiFactory();
        using var client = factory.CreateClient();
        using var form = PaveLensApiFactory.Upload("av-el-sol-bache.jpg");
        form.Add(new StringContent("global-reference"), "regionId");
        using var created = await client.PostAsync("/api/v1/inspections", form, TestContext.Current.CancellationToken);
        var pending = await created.Content.ReadFromJsonAsync<InspectionDetailDto>(PaveLensApiFactory.Json, TestContext.Current.CancellationToken);
        var detail = await PaveLensApiFactory.WaitForAnalysisAsync(client, pending!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(InspectionStatus.Completed, detail.Status);
        using var budget = await client.GetAsync($"/api/v1/inspections/{detail.Id}/budget", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, budget.StatusCode);
        Assert.False(RegionalCatalog.Restore(detail.Trace!.Orchestration!.RegionalProfileJson).PricesAvailable);
    }

    [Fact]
    public async Task AnAgentCannotFinishBeforeVerification()
    {
        await using var factory = new PaveLensApiFactory { Coordinator = new PrematureCoordinator() };
        using var client = factory.CreateClient();
        using var form = PaveLensApiFactory.Upload("av-el-sol-bache.jpg");
        using var created = await client.PostAsync("/api/v1/inspections", form, TestContext.Current.CancellationToken);
        var pending = await created.Content.ReadFromJsonAsync<InspectionDetailDto>(PaveLensApiFactory.Json, TestContext.Current.CancellationToken);
        var detail = await PaveLensApiFactory.WaitForAnalysisAsync(client, pending!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(InspectionStatus.Failed, detail.Status);
        Assert.Equal("Rejected", detail.Trace!.Orchestration!.Actions.Single().Status);
        Assert.Equal("Failed", detail.Trace.Orchestration.Status);
    }

    [Fact]
    public async Task MissingFindingsAreRejectedInsteadOfReducingTheDamageCount()
    {
        await using var factory = new PaveLensApiFactory { Reasoner = new OmittingReasoner() };
        using var client = factory.CreateClient();
        using var form = PaveLensApiFactory.Upload("av-el-sol-bache.jpg");
        using var created = await client.PostAsync("/api/v1/inspections", form, TestContext.Current.CancellationToken);
        var pending = await created.Content.ReadFromJsonAsync<InspectionDetailDto>(PaveLensApiFactory.Json, TestContext.Current.CancellationToken);
        var detail = await PaveLensApiFactory.WaitForAnalysisAsync(client, pending!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(InspectionStatus.Failed, detail.Status);
        Assert.Contains("exactamente una vez", detail.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewSegmentationInvalidatesAnAlreadyVerifiedReport()
    {
        await using var factory = new PaveLensApiFactory { Coordinator = new ResegmentingCoordinator() };
        using var client = factory.CreateClient();
        using var form = PaveLensApiFactory.Upload("av-el-sol-bache.jpg");
        using var created = await client.PostAsync("/api/v1/inspections", form, TestContext.Current.CancellationToken);
        var pending = await created.Content.ReadFromJsonAsync<InspectionDetailDto>(PaveLensApiFactory.Json, TestContext.Current.CancellationToken);
        var detail = await PaveLensApiFactory.WaitForAnalysisAsync(client, pending!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(InspectionStatus.Failed, detail.Status);
        Assert.Equal("Rejected", detail.Trace!.Orchestration!.Actions[^1].Status);
    }

    [Fact]
    public async Task APhotoWithoutDistressStopsRightAfterSegmentation()
    {
        await using var factory = new PaveLensApiFactory { Segmenter = new EmptySegmenter() };
        using var client = factory.CreateClient();
        using var form = PaveLensApiFactory.Upload("av-el-sol-bache.jpg");
        using var created = await client.PostAsync("/api/v1/inspections", form, TestContext.Current.CancellationToken);
        var pending = await created.Content.ReadFromJsonAsync<InspectionDetailDto>(PaveLensApiFactory.Json, TestContext.Current.CancellationToken);
        var detail = await PaveLensApiFactory.WaitForAnalysisAsync(client, pending!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(InspectionStatus.Failed, detail.Status);
        Assert.Contains("no encontró deterioros", detail.FailureReason, StringComparison.Ordinal);
        var last = detail.Trace!.Orchestration!.Actions[^1];
        Assert.Equal(("segment_image", "Rejected"), (last.Tool, last.Status));
        Assert.Single(detail.Trace.Orchestration.Actions, a => a.Tool == "segment_image");
    }

    private sealed class EmptySegmenter : IDistressSegmenter
    {
        public AnalysisEngine Engine => AnalysisEngine.Simulation;
        public Task<SegmentationResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SegmentationResult("test-segmenter", []));
    }

    private sealed class ResegmentingCoordinator : IInspectionCoordinator
    {
        public string Engine => "Simulation";
        public string Model => "test-coordinator";
        public async Task<AgentUsage> RunAsync(string task, IReadOnlyList<AgentTool> tools, ExecuteAgentTool execute, CancellationToken cancellationToken)
        {
            var arguments = JsonSerializer.SerializeToElement(new { });
            foreach (var name in InspectionTools.Order.Where(n => n != "finish"))
                Assert.True((await execute(name, arguments, cancellationToken)).Success);
            Assert.True((await execute("segment_image", arguments, cancellationToken)).Success);
            Assert.False((await execute("finish", arguments, cancellationToken)).Success);
            return new AgentUsage(Model, 0, 0, 0);
        }
    }

    private sealed class PrematureCoordinator : IInspectionCoordinator
    {
        public string Engine => "Simulation";
        public string Model => "test-coordinator";
        public async Task<AgentUsage> RunAsync(string task, IReadOnlyList<AgentTool> tools, ExecuteAgentTool execute, CancellationToken cancellationToken)
        {
            var result = await execute("finish", JsonSerializer.SerializeToElement(new { }), cancellationToken);
            Assert.False(result.Success);
            return new AgentUsage(Model, 0, 0, 0);
        }
    }
    private sealed class OmittingReasoner : IDiagnosticReasoner
    {
        public AnalysisEngine Engine => AnalysisEngine.Simulation;
        public Task<DiagnosisResult> DiagnoseAsync(DiagnosisRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DiagnosisResult("test-reasoner", "Incomplete diagnosis", [], []));
    }
}
