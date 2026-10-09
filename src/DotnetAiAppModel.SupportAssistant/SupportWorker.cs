using System.Threading.Channels;

namespace DotnetAiAppModel.SupportAssistant;

public sealed class SupportWorkChannel
{
    private readonly Channel<string> _channel;

    public SupportWorkChannel(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Queue capacity must be greater than zero.");
        }

        _channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public bool TryEnqueue(string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        return _channel.Writer.TryWrite(workItemId);
    }

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete(Exception? error = null) => _channel.Writer.TryComplete(error);
}

public sealed partial class SupportWorker : BackgroundService
{
    private readonly SupportWorkChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SupportWorker> _logger;

    public SupportWorker(
        SupportWorkChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<SupportWorker> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var workItemId in _channel.ReadAllAsync(stoppingToken))
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<SupportWorkItemProcessor>();
                await processor.ProcessAsync(workItemId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogWorkerStopping(_logger);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Support worker stopping.")]
    private static partial void LogWorkerStopping(ILogger logger);
}
