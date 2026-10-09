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
    private readonly ISupportWorkStore _workStore;
    private readonly SupportApplication _supportApplication;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SupportWorker> _logger;

    public SupportWorker(
        SupportWorkChannel channel,
        ISupportWorkStore workStore,
        SupportApplication supportApplication,
        TimeProvider timeProvider,
        ILogger<SupportWorker> logger)
    {
        _channel = channel;
        _workStore = workStore;
        _supportApplication = supportApplication;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var workItemId in _channel.ReadAllAsync(stoppingToken))
            {
                var workItem = await _workStore.GetAsync(workItemId, stoppingToken);
                if (workItem is null)
                {
                    LogWorkItemMissing(_logger, workItemId);
                    continue;
                }

                var processingItem = await _workStore.TryMarkProcessingAsync(
                    workItemId,
                    _timeProvider.GetUtcNow(),
                    stoppingToken);
                if (processingItem is null)
                {
                    LogWorkItemSkipped(_logger, workItemId, workItem.Status.ToString());
                    continue;
                }

                using var scope = _logger.BeginScope(new Dictionary<string, object?>
                {
                    ["SupportWorkItemId"] = workItemId,
                    ["SupportCorrelationId"] = processingItem.CorrelationId,
                    ["SupportConversationId"] =
                        processingItem.Request.ConversationId
                });

                try
                {
                    var response = await _supportApplication.HandleAsync(
                        processingItem.Request,
                        stoppingToken);
                    await _workStore.MarkCompletedAsync(
                        workItemId,
                        response,
                        _timeProvider.GetUtcNow(),
                        stoppingToken);
                    LogWorkItemCompleted(_logger, workItemId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    await _workStore.MarkFailedAsync(
                        workItemId,
                        "Support work was canceled during worker shutdown.",
                        _timeProvider.GetUtcNow(),
                        CancellationToken.None);
                    LogWorkItemCanceled(_logger, workItemId);
                    return;
                }
                catch (Exception exception)
                {
                    var failureReason = string.IsNullOrWhiteSpace(exception.Message)
                        ? exception.GetType().Name
                        : exception.Message;
                    await _workStore.MarkFailedAsync(
                        workItemId,
                        failureReason,
                        _timeProvider.GetUtcNow(),
                        CancellationToken.None);
                    LogWorkItemFailed(_logger, workItemId, exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogWorkerStopping(_logger);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Queued support work item completed: {WorkItemId}.")]
    private static partial void LogWorkItemCompleted(
        ILogger logger,
        string workItemId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Queued support work item canceled: {WorkItemId}.")]
    private static partial void LogWorkItemCanceled(
        ILogger logger,
        string workItemId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Queued support work item failed: {WorkItemId}.")]
    private static partial void LogWorkItemFailed(
        ILogger logger,
        string workItemId,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Queued support work item was missing from the work store: {WorkItemId}.")]
    private static partial void LogWorkItemMissing(
        ILogger logger,
        string workItemId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Queued support work item was skipped: {WorkItemId} has status {Status}.")]
    private static partial void LogWorkItemSkipped(
        ILogger logger,
        string workItemId,
        string status);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Support worker stopping.")]
    private static partial void LogWorkerStopping(ILogger logger);
}
