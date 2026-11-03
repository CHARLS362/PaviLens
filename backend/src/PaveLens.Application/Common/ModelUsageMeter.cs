namespace PaveLens.Application.Common;

/// <summary>
/// Counts the model calls and tokens of one inspection run. The processor opens it around the run; model clients
/// record into <see cref="Current"/>, which flows with the async calls of that run only.
/// </summary>
public sealed class ModelUsageMeter
{
    private static readonly AsyncLocal<ModelUsageMeter?> Ambient = new();
    private int _calls;
    private long _input, _output;

    public static ModelUsageMeter? Current => Ambient.Value;

    public int Calls => Volatile.Read(ref _calls);
    public long InputTokens => Interlocked.Read(ref _input);
    public long OutputTokens => Interlocked.Read(ref _output);
    public long Tokens => InputTokens + OutputTokens;

    public static ModelUsageMeter Start() => Ambient.Value = new ModelUsageMeter();

    public void Record(long inputTokens, long outputTokens)
    {
        Interlocked.Increment(ref _calls);
        Interlocked.Add(ref _input, inputTokens);
        Interlocked.Add(ref _output, outputTokens);
    }
}
