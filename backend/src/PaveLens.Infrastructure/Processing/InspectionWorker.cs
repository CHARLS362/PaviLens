using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Inspections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PaveLens.Infrastructure.Processing;

/// <summary>
/// In-process queue. Durable state lives in the database (status Pending/Processing), so a restart
/// loses nothing: the worker re-enqueues unfinished inspections on start. Replace with a broker
/// (RabbitMQ, Azure Service Bus, SQS) to scale workers horizontally.
/// </summary>
internal sealed class InMemoryInspectionQueue : IInspectionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true
    });

    public ValueTask EnqueueAsync(Guid inspectionId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(inspectionId, cancellationToken);

    public async IAsyncEnumerable<Guid> DequeueAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var id in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return id;
        }
    }
}

internal sealed partial class InspectionWorker(
    IInspectionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<InspectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverUnfinishedAsync(stoppingToken);

        await foreach (var inspectionId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<InspectionProcessor>();
                await processor.ProcessAsync(inspectionId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // One bad item must not stop the worker.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogUnhandled(ex, inspectionId);
            }
        }
    }

    private async Task RecoverUnfinishedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInspectionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var ids = await repository.ListUnfinishedIdsAsync(cancellationToken);
        foreach (var id in ids)
        {
            var inspection = await repository.GetAsync(id, cancellationToken);
            inspection?.ResetInterrupted();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var id in ids)
        {
            await queue.EnqueueAsync(id, cancellationToken);
        }

        if (ids.Count > 0)
        {
            LogRecovered(ids.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error while processing inspection {InspectionId}")]
    private partial void LogUnhandled(Exception exception, Guid inspectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-queued {Count} unfinished inspections")]
    private partial void LogRecovered(int count);
}
