namespace DotnetAiAppModel.SupportAssistant;

public sealed partial class SupportWorkItemProcessor
{
    private readonly ISupportWorkStore _workStore;
    private readonly SupportApplication _supportApplication;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SupportWorkItemProcessor> _logger;

    public SupportWorkItemProcessor(
        ISupportWorkStore workStore,
        SupportApplication supportApplication,
        TimeProvider timeProvider,
        ILogger<SupportWorkItemProcessor> logger)
    {
        _workStore = workStore;
        _supportApplication = supportApplication;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ProcessAsync(
        string workItemId,
        CancellationToken stoppingToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);

        var workItem = await _workStore.GetAsync(workItemId, stoppingToken);
        if (workItem is null)
        {
            LogWorkItemMissing(_logger, workItemId);
            return;
        }

        var processingItem = await _workStore.TryMarkProcessingAsync(
            workItemId,
            _timeProvider.GetUtcNow(),
            stoppingToken);
        if (processingItem is null)
        {
            LogWorkItemSkipped(_logger, workItemId, workItem.Status.ToString());
            return;
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["SupportWorkItemId"] = workItemId,
            ["SupportCorrelationId"] = processingItem.CorrelationId,
            ["SupportConversationId"] = processingItem.Request.ConversationId
        });

        try
        {
            var response = await _supportApplication.HandleAsync(
                processingItem.Request,
                stoppingToken);
            var completed = await _workStore.MarkCompletedAsync(
                workItemId,
                response,
                _timeProvider.GetUtcNow(),
                stoppingToken);
            if (completed is null)
            {
                throw new InvalidOperationException(
                    $"The support work item '{workItemId}' could not be marked completed.");
            }

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
}
