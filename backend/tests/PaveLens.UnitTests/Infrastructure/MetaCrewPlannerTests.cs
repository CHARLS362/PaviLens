using System.Text;
using System.Text.Json.Nodes;
using PaveLens.Application.Common;
using PaveLens.Application.Maintenance;
using PaveLens.Domain.Inspections;
using PaveLens.Infrastructure.Ai;
using PaveLens.Infrastructure.Ai.Meta;
using Microsoft.Extensions.Options;

namespace PaveLens.UnitTests.Infrastructure;

public class MetaCrewPlannerTests
{
    private const string Dropped = """{"summary":"Plan","order":["S1"],"sites":[{"id":"S1","closure":"Un carril","window":"09:00–10:00","rationale":"r"}]}""";

    private const string Valid = """
        {"summary":"Primero el colegio, luego el bache.","order":["S2","S1"],
         "sites":[{"id":"S1","closure":"Cierre de un carril con paso alterno","window":"10:30–11:45","rationale":"Bache aislado."},
                  {"id":"S2","closure":"Cierre total con desvío","window":"09:00–10:30","rationale":"Junto a un colegio."}],
         "risks":["Lluvia"],"communication":["Aviso a vecinos"]}
        """;

    [Fact]
    public async Task RepairsAPlanThatDropsASiteAndSendsTheProblemBack()
    {
        var handler = new ScriptedHandler(Dropped, Valid);
        var plan = await Planner(handler).PlanAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Bodies.Count);
        var retry = JsonNode.Parse(handler.Bodies[1])!["messages"]!.AsArray();
        Assert.Contains("order", retry[^1]!["content"]!.GetValue<string>());
        Assert.Equal(["S2", "S1"], plan.Order);
        Assert.Equal("muse-wonder-1.0", plan.Model);
    }

    [Fact]
    public async Task SendsTheFactsComputedByCodeAndGivesUpAfterOneRepair()
    {
        var handler = new ScriptedHandler(Dropped, Dropped);
        await Assert.ThrowsAsync<ModelOutputException>(() => Planner(handler).PlanAsync(Request(), TestContext.Current.CancellationToken));

        var user = JsonNode.Parse(handler.Bodies[0])!["messages"]!.AsArray()[1]!["content"]!.GetValue<string>();
        var input = JsonNode.Parse(user[user.IndexOf('{', StringComparison.Ordinal)..])!;
        Assert.Equal("S1", input["sites"]![0]!["id"]!.GetValue<string>());
        Assert.Equal(420, input["sites"]![0]!["legDistanceM"]!.GetValue<double>());
        Assert.Equal("google", input["totals"]!["routing"]!.GetValue<string>());
    }

    private static MetaCrewPlanner Planner(ScriptedHandler handler)
    {
        var options = Options.Create(new AiOptions { Provider = AiProvider.Meta });
        options.Value.Meta.Reasoning.ApiKey = "test-key";
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.meta.test/v1/") };
        return new MetaCrewPlanner(new MetaChatClient(http, options));
    }

    private static CrewPlanRequest Request() => new(
        [
            new CrewSiteFact("S1", "Bache Av. El Sol", null, 30, "Urgente", 2, 1, 420, 2),
            new CrewSiteFact("S2", "Jr. Lima", "Colegio", 55, "Programar", 5, 0, 600, 3)
        ],
        1020, 5, "google", ReportLanguage.Es);

    private sealed class ScriptedHandler(params string[] contents) : HttpMessageHandler
    {
        private int _next;
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var content = contents[Math.Min(_next++, contents.Length - 1)];
            var body = new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = content } }) };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
