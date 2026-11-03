using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace PaveLens.Api.Infrastructure;

/// <summary>
/// Reads a local <c>.env</c> file (see <c>.env.example</c> at the repository root) so a fresh clone can be
/// configured by editing one file. Precedence: real environment variables and command-line arguments win over
/// <c>.env</c>, which wins over user-secrets and appsettings. The file is git-ignored; values are never logged.
/// </summary>
public static class DotEnvFile
{
    public const string FileName = ".env";

    /// <summary>Short names accepted in <c>.env</c>, mapped to configuration keys.</summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["PAVELENS_AI_PROVIDER"] = "Ai:Provider",
        ["PAVELENS_SEGMENTATION_SERVICE"] = "Ai:Meta:SegmentationService",
        ["META_API_KEY"] = "Ai:Meta:Reasoning:ApiKey",
        ["META_REASONING_MODEL"] = "Ai:Meta:Reasoning:Model",
        ["META_SAM31_API_KEY"] = "Ai:Meta:Sam31:ApiKey",
        ["ROBOFLOW_API_KEY"] = "Ai:Meta:Segmentation:ApiKey",
    };

    /// <summary>
    /// Adds the nearest <c>.env</c> (current folder, content root or any parent) as a configuration source.
    /// Skipped in the Testing environment so a developer's keys never reach the automated tests.
    /// Returns the file used, or null. <paramref name="searchFrom"/> replaces the current folder as the starting point.
    /// </summary>
    public static string? AddTo(ConfigurationManager configuration, IHostEnvironment environment, string? searchFrom = null)
    {
        if (environment.IsEnvironment("Testing")) return null;

        var path = Find(searchFrom ?? Directory.GetCurrentDirectory()) ?? Find(environment.ContentRootPath);
        if (path is null) return null;

        var values = Parse(File.ReadAllLines(path));
        if (values.Count == 0) return path;

        // Just before the unprefixed environment-variables source, so real variables still override the file.
        var source = new MemoryConfigurationSource { InitialData = values! };
        var sources = configuration.Sources;
        var index = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" }) index = i;
        }
        if (index >= 0) sources.Insert(index, source);
        else sources.Add(source);
        return path;
    }

    /// <summary>
    /// Parses <c>KEY=value</c> lines. Blank lines and <c># comments</c> are skipped, an optional <c>export </c>
    /// prefix and surrounding quotes are removed, empty values are ignored. Keys are either aliases or
    /// configuration paths written with <c>__</c> (e.g. <c>Ai__Meta__Reasoning__Model</c>); anything else is ignored.
    /// </summary>
    public static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                value = value[1..^1];
            else if (value.IndexOf(" #", StringComparison.Ordinal) is var hash and >= 0)
                value = value[..hash].TrimEnd();
            if (value.Length == 0) continue;

            if (Aliases.TryGetValue(key, out var mapped)) values[mapped] = value;
            else if (key.Contains("__", StringComparison.Ordinal)) values[key.Replace("__", ":", StringComparison.Ordinal)] = value;
        }
        return values;
    }

    private static string? Find(string? start)
    {
        for (var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Configuration: {DotEnv} | AI provider: {Provider}")]
    public static partial void Configuration(ILogger logger, string dotEnv, string provider);
}
