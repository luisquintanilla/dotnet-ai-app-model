namespace DotnetAiAppModel.SupportAssistant;

public sealed class SupportWorkSubmissionService
{
    private readonly SupportWorkChannel _channel;
    private readonly ISupportWorkStore _workStore;
    private readonly TimeProvider _timeProvider;

    public SupportWorkSubmissionService(
        SupportWorkChannel channel,
        ISupportWorkStore workStore,
        TimeProvider timeProvider)
    {
        _channel = channel;
        _workStore = workStore;
        _timeProvider = timeProvider;
    }

    public async ValueTask<SupportQueueSubmission> SubmitAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = Guid.NewGuid().ToString("N");
        var queuedRequest = request with { CorrelationId = correlationId };
        var workItem = await _workStore.CreateAsync(
            queuedRequest,
            correlationId,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        if (_channel.TryEnqueue(workItem.WorkItemId))
        {
            return new SupportQueueSubmission(workItem, Enqueued: true);
        }

        var failed = await _workStore.MarkFailedAsync(
            workItem.WorkItemId,
            "The support work queue is full.",
            _timeProvider.GetUtcNow(),
            CancellationToken.None);
        if (failed is null)
        {
            throw new InvalidOperationException(
                $"The support work item '{workItem.WorkItemId}' could not be marked failed after queue rejection.");
        }

        return new SupportQueueSubmission(failed, Enqueued: false);
    }
}

public sealed record SupportQueueSubmission(
    SupportWorkItem WorkItem,
    bool Enqueued);
