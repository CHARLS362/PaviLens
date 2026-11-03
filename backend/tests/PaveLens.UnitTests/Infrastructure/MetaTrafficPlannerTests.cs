using System.Text;
using System.Text.Json.Nodes;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Application.Traffic;
using PaveLens.Infrastructure.Ai;
using PaveLens.Infrastructure.Ai.Meta;
using Microsoft.Extensions.Options;

namespace PaveLens.UnitTests.Infrastructure;

public class MetaTrafficPlannerTests
{
    private const string Invalid = """{"summary":"Plan","routes":[{"id":"R1","name":"Directo","nodes":["N11","N12"]}],"signage":[]}""";

    private const string Valid = """
        {"summary":"Cierre de Jr. Lima con desvío por Jr. Puno.",
         "routes":[{"id":"R1","name":"Desvío norte","vehicles":"Todos","nodes":["N11","N01","N02","N12"],"rationale":"Más rápida."}],
         "signage":[{"nodeId":"N11","kind":"Reglamentaria","message":"Jr. Lima CERRADO"}],
         "phases":[{"order":2,"title":"Obra","when":"Noche","description":"…"},{"order":1,"title":"Aviso","when":"48 h antes","description":"…"}],
         "communication":["Radio"],"risks":[]}
        """;

    [Fact]
    public async Task RepairsARouteThatCrossesTheWorkZoneAndSendsTheProblemBack()
    {
        var handler = new ScriptedHandler(Invalid, Valid);
        var planner = Planner(handler);

        var plan = await planner.PlanAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Bodies.Count);
        var retry = JsonNode.Parse(handler.Bodies[1])!["messages"]!.AsArray();
        Assert.Contains("H11", retry[^1]!["content"]!.GetValue<string>());
        Assert.Equal(["N11", "N01", "N02", "N12"], plan.Routes.Single().Nodes);
        Assert.Equal("muse-wonder-1.0", plan.Model);
    }

    [Fact]
    public async Task GivesUpAfterOneRepair()
    {
        var planner = Planner(new ScriptedHandler(Invalid, Invalid));

        await Assert.ThrowsAsync<ModelOutputException>(() => planner.PlanAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendsTheComputedCandidatesAndResultsToTheModel()
    {
        var handler = new ScriptedHandler(Valid);

        await Planner(handler).PlanAsync(Request(), TestContext.Current.CancellationToken);

        var sent = JsonNode.Parse(handler.Bodies[0])!;
        Assert.Equal("muse-wonder-1.0", sent["model"]!.GetValue<string>());
        var user = sent["messages"]!.AsArray()[1]!["content"]!.GetValue<string>();
        var input = JsonNode.Parse(user[(user.IndexOf('{', StringComparison.Ordinal))..])!;
        Assert.Equal("H11", input["closure"]!["segmentId"]!.GetValue<string>());
        Assert.NotEmpty(input["candidates"]!.AsArray());
        Assert.NotNull(input["results"]!["userCostPen"]);
    }

    private static MetaTrafficPlanner Planner(ScriptedHandler handler)
    {
        var options = Options.Create(new AiOptions { Provider = AiProvider.Meta });
        options.Value.Meta.Reasoning.ApiKey = "test-key";
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.meta.test/v1/") };
        return new MetaTrafficPlanner(new MetaChatClient(http, options));
    }

    private static TrafficPlanRequest Request()
    {
        var traffic = new TrafficService();
        var simulation = traffic.Simulate(new SimulateWorkZoneCommand("H11", 1));
        return new TrafficPlanRequest(simulation, traffic.Network, traffic.CandidateRoutes(simulation));
    }

    private sealed class ScriptedHandler(params string[] contents) : HttpMessageHandler
    {
        private int _next;

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var content = contents[Math.Min(_next++, contents.Length - 1)];
            var body = new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = content } })
            };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
        }
    }
}
