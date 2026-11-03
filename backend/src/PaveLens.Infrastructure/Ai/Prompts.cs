using System.Collections.Concurrent;

namespace PaveLens.Infrastructure.Ai;

/// <summary>Versioned prompt files embedded in the assembly (Ai/Prompts/*.md).</summary>
internal static class Prompts
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Load(string fileName) => Cache.GetOrAdd(fileName, static name =>
    {
        var assembly = typeof(Prompts).Assembly;
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(r => r.EndsWith($".Prompts.{name}", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Prompt '{name}' no está embebido.");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
