using PaveLens.Domain.Common;

namespace PaveLens.Domain.Inspections;

/// <summary>One text prompt sent to the segmentation model and how many of its masks were kept.</summary>
public sealed record ConceptTrace
{
    public ConceptTrace(string concept, int masks)
    {
        Concept = Guard.NotBlank(concept, "Concept", 100);
        Masks = masks >= 0 ? masks : throw new DomainException("Masks no puede ser negativo.");
    }

    public string Concept { get; }
    public int Masks { get; }
}

/// <summary>
/// How a completed analysis was produced: what each model was asked and how long it took.
/// Lets the inspector (and an auditor) see which numbers came from a model and which from code.
/// </summary>
public sealed record AnalysisTrace
{
    public AnalysisTrace(
        IReadOnlyList<ConceptTrace> concepts,
        long segmentationMs,
        long reasoningMs,
        int reasoningAttempts,
        int skippedBeyondHorizon,
        AgentRunTrace? orchestration = null)
    {
        if (concepts.Count == 0)
        {
            throw new DomainException("La traza necesita al menos un concepto de segmentación.");
        }

        if (segmentationMs < 0 || reasoningMs < 0 || reasoningAttempts < 1 || skippedBeyondHorizon < 0)
        {
            throw new DomainException("La traza del análisis tiene valores fuera de rango.");
        }

        Concepts = concepts;
        SegmentationMs = segmentationMs;
        ReasoningMs = reasoningMs;
        ReasoningAttempts = reasoningAttempts;
        SkippedBeyondHorizon = skippedBeyondHorizon;
        Orchestration = orchestration;
    }

    public IReadOnlyList<ConceptTrace> Concepts { get; }
    public long SegmentationMs { get; }
    public long ReasoningMs { get; }

    /// <summary>1 when the model's first answer passed validation; 2 after one self-repair retry.</summary>
    public int ReasoningAttempts { get; }

    /// <summary>Masks left out because they reach beyond the calibration horizon.</summary>
    public int SkippedBeyondHorizon { get; }
    public AgentRunTrace? Orchestration { get; }
}
