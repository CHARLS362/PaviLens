using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Application.Inspections;
using PaveLens.Domain.Common;
using PaveLens.Domain.Inspections;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace PaveLens.Application.Assistant;

/// <summary>Follow-up questions about a finished inspection, with persisted conversation memory.</summary>
public sealed class AssistantService(
    IInspectionQueries queries,
    IImageRepository images,
    IImageStorage storage,
    IConversationRepository conversations,
    IMaintenanceAssistant assistant,
    IUnitOfWork unitOfWork,
    IValidator<AskQuestionCommand> validator,
    IOptions<AnalysisOptions> options,
    TimeProvider clock)
{
    public async Task<AnswerDto> AskAsync(AskQuestionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var inspection = await queries.GetAsync(command.InspectionId, cancellationToken)
            ?? throw new NotFoundException("Inspección", command.InspectionId);
        if (inspection.Status != InspectionStatus.Completed)
        {
            throw new DomainException("Solo se pueden hacer preguntas sobre inspecciones completadas.");
        }

        DistressDto? distress = null;
        if (command.DistressId is { } distressId)
        {
            distress = inspection.Distresses.FirstOrDefault(d => d.Id == distressId)
                ?? throw new NotFoundException("Deterioro", distressId);
        }

        var image = await images.GetAsync(inspection.Image.Id, cancellationToken)
            ?? throw new NotFoundException("Imagen", inspection.Image.Id);
        var bytes = await storage.ReadAllBytesAsync(image.StorageKey, cancellationToken);

        var history = await conversations.ListRecentAsync(
            command.InspectionId, command.DistressId, options.Value.AssistantHistoryTurns * 2, cancellationToken);

        var answer = await assistant.AnswerAsync(
            new AssistantRequest(
                new ImagePayload(bytes, image.ContentType, image.Width, image.Height, image.Sha256),
                inspection,
                distress,
                history.Select(InspectionMapper.ToDto).ToList(),
                command.Question.Trim()),
            cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var question = ConversationMessage.FromUser(command.InspectionId, command.DistressId, command.Question, now);
        var reply = ConversationMessage.FromAssistant(command.InspectionId, command.DistressId, answer, assistant.Engine, now.AddTicks(1));
        conversations.Add(question);
        conversations.Add(reply);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AnswerDto(InspectionMapper.ToDto(question), InspectionMapper.ToDto(reply));
    }

    public async Task<IReadOnlyList<ConversationMessageDto>> GetConversationAsync(
        Guid inspectionId, Guid? distressId, CancellationToken cancellationToken)
    {
        _ = await queries.GetAsync(inspectionId, cancellationToken) ?? throw new NotFoundException("Inspección", inspectionId);
        return await queries.GetConversationAsync(inspectionId, distressId, cancellationToken);
    }
}
