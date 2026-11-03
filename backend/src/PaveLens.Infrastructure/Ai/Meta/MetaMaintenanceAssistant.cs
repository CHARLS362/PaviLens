using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Inspections;
using PaveLens.Domain.Inspections;

namespace PaveLens.Infrastructure.Ai.Meta;

/// <summary>Follow-up answers grounded in the photo, the stored diagnosis and the conversation so far.</summary>
internal sealed class MetaMaintenanceAssistant(MetaChatClient chat) : IMaintenanceAssistant
{
    public AnalysisEngine Engine => AnalysisEngine.Meta;

    public Task<string> AnswerAsync(AssistantRequest request, CancellationToken cancellationToken)
    {
        var messages = new List<JsonObject>
        {
            MetaChatClient.System(Prompts.Load("assistant.v1.md")),
            MetaChatClient.UserWithImage(DescribeContext(request.Inspection, request.Distress), request.Image)
        };

        foreach (var turn in request.History)
        {
            messages.Add(turn.Role == MessageRole.User
                ? MetaChatClient.User(turn.Content)
                : MetaChatClient.Assistant(turn.Content));
        }

        messages.Add(MetaChatClient.User(request.Question));
        return chat.CompleteAsync(messages, jsonMode: false, cancellationToken);
    }

    internal static string DescribeContext(InspectionDetailDto inspection, DistressDto? focus)
    {
        var text = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        text.AppendLine(inv, $"Inspección: {inspection.Title}");
        text.AppendLine(inspection.Language == ReportLanguage.En ? "Idioma de respuesta: inglés (English)" : "Idioma de respuesta: español");
        if (inspection.Location is not null) text.AppendLine(inv, $"Ubicación: {inspection.Location}");
        if (inspection.Notes is not null) text.AppendLine(inv, $"Notas del inspector: {inspection.Notes}");
        if (inspection.Trace?.Orchestration is { } run) text.AppendLine(inv, $"Perfil regional y fuentes: {run.RegionalProfileJson}");
        text.AppendLine(inv, $"Diagnóstico global: {inspection.Summary}");
        if (inspection.Pci is { } pci)
        {
            text.AppendLine(inv,
                $"PCI aproximado: {pci.Pci} ({pci.Rating}){(pci.IsPartial ? ", parcial: área menor que una unidad de muestreo" : "")}. Intervención sugerida: {pci.Treatment}.");
        }

        text.AppendLine("Deterioros:");
        foreach (var d in inspection.Distresses)
        {
            var m = d.Measurement;
            text.AppendLine(inv,
                $"- {d.Code} {d.Type} ({d.RddCode ?? "-"}), severidad {d.Severity}: área {m.AreaM2} m², {m.LengthM} × {m.WidthM} m, e={m.Eccentricity}. {d.Diagnosis} Acción: {d.RecommendedAction}");
            if (d.Quantities is { } q)
            {
                text.AppendLine(inv,
                    $"  Cubicaje: profundidad {q.DepthM} m ({q.DepthSource}), corte {q.CutAreaM2} m², volumen {q.VolumeM3} m³, mezcla {q.MassT} t");
            }
        }

        text.AppendLine("Protocolo:");
        foreach (var s in inspection.RepairSteps)
        {
            text.AppendLine(inv, $"{s.Order}. {s.Title} [{s.Risk}] → {s.TargetCode ?? "general"}");
        }

        if (focus is not null)
        {
            text.AppendLine(inv, $"La pregunta se refiere a {focus.Code} ({focus.Type}).");
        }

        return text.ToString();
    }
}
