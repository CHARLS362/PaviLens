using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PaveLens.Application.Common;
using PaveLens.Application.Orchestration;
using PaveLens.Infrastructure.Ai;
using PaveLens.Infrastructure.Ai.Meta;

namespace PaveLens.UnitTests.Infrastructure;

public class MetaAgentTests
{
    private static readonly string[] ExpectedCalls = ["load_region", "finish"];
    [Fact]
    public async Task DispatchesToolsWithMatchingCallIdsAndRecordsCoordinatorUsage()
    {
        var requests = new List<JsonObject>();
        using var handler = new AgentHandler(async request =>
        {
            requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject());
            var name = requests.Count == 1 ? "load_region" : "finish";
            return new JsonObject
            {
                ["usage"] = new JsonObject { ["prompt_tokens"] = 12, ["completion_tokens"] = 3 },
                ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject
                {
                    ["role"] = "assistant", ["tool_calls"] = new JsonArray(new JsonObject
                    {
                        ["id"] = $"call-{requests.Count}", ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = name, ["arguments"] = "{}" }
                    })
                } })
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") };
        var chat = new MetaChatClient(http, Options.Create(new AiOptions { Meta = new MetaOptions { Reasoning = new EndpointOptions { ApiKey = "test-key", Model = "muse-wonder-1.0" } } }));
        var coordinator = new MetaInspectionCoordinator(chat, Options.Create(new OrchestrationOptions()));
        var executed = new List<string>();
        var usage = await coordinator.RunAsync("Inspect", InspectionTools.Definitions, (name, _, _) =>
        { executed.Add(name); return Task.FromResult(new AgentToolResult(true, "verified")); }, TestContext.Current.CancellationToken);
        Assert.Equal(ExpectedCalls, executed);
        Assert.Equal(2, usage.Calls); Assert.Equal(24, usage.InputTokens); Assert.Equal(6, usage.OutputTokens);
        Assert.Equal("call-1", requests[1]["messages"]![3]!["tool_call_id"]!.GetValue<string>());
        Assert.Equal("auto", requests[0]["tool_choice"]!.GetValue<string>());
    }

    [Fact]
    public async Task RemindsOnceAfterAProseReplyAndThenRunsTheTools()
    {
        var replies = 0;
        using var handler = new AgentHandler(_ => Task.FromResult(++replies == 1
            ? JsonNode.Parse("""{"choices":[{"message":{"role":"assistant","content":"I will start now."}}]}""")!.AsObject()
            : JsonNode.Parse("""{"choices":[{"message":{"role":"assistant","tool_calls":[{"id":"c","type":"function","function":{"name":"finish","arguments":"{}"}}]}}]}""")!.AsObject()));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var coordinator = new MetaInspectionCoordinator(new MetaChatClient(http, Options.Create(new AiOptions())), Options.Create(new OrchestrationOptions()));
        var usage = await coordinator.RunAsync("Inspect", InspectionTools.Definitions,
            (_, _, _) => Task.FromResult(new AgentToolResult(true, "verified")), TestContext.Current.CancellationToken);
        Assert.Equal(2, usage.Calls);
    }

    [Fact]
    public async Task RejectsAProseAnswerWithoutToolExecution()
    {
        using var handler = new AgentHandler(_ => Task.FromResult(JsonNode.Parse("""{"choices":[{"message":{"role":"assistant","content":"All done"}}]}""")!.AsObject()));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var coordinator = new MetaInspectionCoordinator(new MetaChatClient(http, Options.Create(new AiOptions())), Options.Create(new OrchestrationOptions()));
        await Assert.ThrowsAsync<ModelOutputException>(() => coordinator.RunAsync("Inspect", InspectionTools.Definitions,
            (_, _, _) => throw new InvalidOperationException("A tool must not execute."), TestContext.Current.CancellationToken));
    }

    private sealed class AgentHandler(Func<HttpRequestMessage, Task<JsonObject>> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            new(HttpStatusCode.OK) { Content = new StringContent((await respond(request)).ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
    }
}
