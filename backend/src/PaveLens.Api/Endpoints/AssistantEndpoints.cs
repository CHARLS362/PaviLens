using PaveLens.Application.Assistant;
using PaveLens.Application.Inspections;
using Microsoft.AspNetCore.Http.HttpResults;

namespace PaveLens.Api.Endpoints;

internal static class AssistantEndpoints
{
    public static RouteGroupBuilder MapAssistantEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/inspections/{id:guid}/conversation").WithTags("Assistant");

        group.MapGet("/", GetAsync).WithSummary("Historial de preguntas y respuestas");
        group.MapPost("/", AskAsync)
            .RequireRateLimiting(RateLimitPolicies.Ai)
            .WithSummary("Pregunta al asistente sobre el tramo o un deterioro");

        return api;
    }

    private static async Task<Ok<IReadOnlyList<ConversationMessageDto>>> GetAsync(
        Guid id, AssistantService service, CancellationToken cancellationToken, Guid? distressId = null) =>
        TypedResults.Ok(await service.GetConversationAsync(id, distressId, cancellationToken));

    private static async Task<Ok<AnswerDto>> AskAsync(
        Guid id, AskRequest request, AssistantService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.AskAsync(new AskQuestionCommand(id, request.DistressId, request.Question), cancellationToken));

    internal sealed record AskRequest(string Question, Guid? DistressId);
}
