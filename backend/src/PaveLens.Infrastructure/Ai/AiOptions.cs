using Microsoft.Extensions.Options;

namespace PaveLens.Infrastructure.Ai;

public enum AiProvider
{
    /// <summary>Deterministic fixtures. Every result is labelled as simulation.</summary>
    Simulation,

    /// <summary>Meta SAM 3 for masks and Muse Wonder for reasoning.</summary>
    Meta
}

public enum SegmentationService
{
    /// <summary>SAM 3.1 directly through Meta Model API, decoded with Meta's official parser.</summary>
    Meta,
    /// <summary>Meta SAM 3 on Roboflow's serverless API (<c>/sam3/concept_segment</c>). Only an API key is needed.</summary>
    Roboflow,

    /// <summary>Self-hosted service implementing <c>POST /v1/segment</c> (docs/ai-contracts.md).</summary>
    Sidecar
}

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public AiProvider Provider { get; set; } = AiProvider.Simulation;
    public MetaOptions Meta { get; set; } = new();
}

public sealed class MetaOptions
{
    public EndpointOptions Sam31 { get; set; } = new() { BaseUrl = new Uri("https://api.meta.ai/v1/"), Model = "sam-3.1", TimeoutSeconds = 60 };
    public string Sam31ParserScript { get; set; } = "../../tools/sam31/decode.mjs";
    public string NodeExecutable { get; set; } = "node";
    public int MaxSpecialistOutputTokens { get; set; } = 8192;
    public SegmentationService SegmentationService { get; set; } = SegmentationService.Roboflow;

    /// <summary>SAM 3 endpoint (see docs/ai-contracts.md).</summary>
    public EndpointOptions Segmentation { get; set; } = new()
    {
        BaseUrl = new Uri("https://serverless.roboflow.com/"),
        Model = "sam3",
        TimeoutSeconds = 60
    };

    /// <summary>Minimum SAM 3 instance confidence kept.</summary>
    public double SegmentationScoreThreshold { get; set; } = 0.5;

    /// <summary>OpenAI-compatible chat completions endpoint serving Muse Wonder.</summary>
    public EndpointOptions Reasoning { get; set; } = new()
    {
        BaseUrl = new Uri("https://api.meta.ai/v1/"),
        Model = "muse-wonder-1.0",
        TimeoutSeconds = 120
    };

    /// <summary>Omitted from the request when null; some reasoning models reject it.</summary>
    public double? Temperature { get; set; }

    /// <summary><c>minimal</c>, <c>low</c>, <c>medium</c>, <c>high</c>… Omitted when null, so the model picks.</summary>
    public string? ReasoningEffort { get; set; } = "low";

    /// <summary>Sends <c>response_format: json_object</c>. Disable if the endpoint rejects it; the parser still validates.</summary>
    public bool JsonMode { get; set; } = true;
}

public sealed class EndpointOptions
{
    public Uri? BaseUrl { get; set; }
    public string Model { get; set; } = string.Empty;

    /// <summary>Secret. Provide through user-secrets or environment variables, never appsettings.json.</summary>
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 60;
}

internal sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    private static readonly string[] Efforts = ["minimal", "low", "medium", "high", "xhigh", "max"];

    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        if (options.Provider != AiProvider.Meta)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        var meta = options.Meta;
        if (meta.MaxSpecialistOutputTokens is < 512 or > 32768) errors.Add("Ai:Meta:MaxSpecialistOutputTokens debe estar entre 512 y 32768.");
        if (meta.SegmentationService == SegmentationService.Meta &&
            (meta.Sam31.BaseUrl is null || string.IsNullOrWhiteSpace(meta.Sam31.Model) || meta.Sam31.TimeoutSeconds is < 5 or > 600 ||
             string.IsNullOrWhiteSpace(meta.Sam31ParserScript) || string.IsNullOrWhiteSpace(meta.NodeExecutable)))
            errors.Add("Configura Ai:Meta:Sam31, Sam31ParserScript y NodeExecutable para usar SAM 3.1 directo.");
        if (meta.Segmentation.BaseUrl is null) errors.Add("Ai:Meta:Segmentation:BaseUrl es obligatorio con Provider=Meta.");
        if (meta.SegmentationService == SegmentationService.Roboflow && string.IsNullOrWhiteSpace(meta.Segmentation.ApiKey))
            errors.Add("Ai:Meta:Segmentation:ApiKey es obligatorio con SegmentationService=Roboflow.");
        if (meta.Reasoning.BaseUrl is null) errors.Add("Ai:Meta:Reasoning:BaseUrl es obligatorio con Provider=Meta.");
        if (string.IsNullOrWhiteSpace(meta.Reasoning.ApiKey)) errors.Add("Ai:Meta:Reasoning:ApiKey es obligatorio con Provider=Meta.");
        if (string.IsNullOrWhiteSpace(meta.Segmentation.Model)) errors.Add("Ai:Meta:Segmentation:Model es obligatorio.");
        if (string.IsNullOrWhiteSpace(meta.Reasoning.Model)) errors.Add("Ai:Meta:Reasoning:Model es obligatorio.");
        if (meta.Segmentation.TimeoutSeconds is < 5 or > 600) errors.Add("Ai:Meta:Segmentation:TimeoutSeconds debe estar entre 5 y 600.");
        if (meta.Reasoning.TimeoutSeconds is < 5 or > 600) errors.Add("Ai:Meta:Reasoning:TimeoutSeconds debe estar entre 5 y 600.");
        if (meta.SegmentationScoreThreshold is <= 0 or >= 1) errors.Add("Ai:Meta:SegmentationScoreThreshold debe estar entre 0 y 1.");
        if (meta.Temperature is < 0 or > 2) errors.Add("Ai:Meta:Temperature debe estar entre 0 y 2.");
        if (meta.ReasoningEffort is not null && !Efforts.Contains(meta.ReasoningEffort))
            errors.Add($"Ai:Meta:ReasoningEffort debe ser uno de: {string.Join(", ", Efforts)}.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
