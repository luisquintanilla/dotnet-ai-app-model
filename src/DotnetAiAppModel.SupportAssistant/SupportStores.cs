namespace DotnetAiAppModel.SupportAssistant;

public interface ISupportConversationStore
{
    ValueTask<SupportConversation> LoadAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    ValueTask AppendAsync(
        string conversationId,
        SupportConversationTurn turn,
        CancellationToken cancellationToken = default);
}

public sealed class InMemorySupportConversationStore : ISupportConversationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<SupportConversationTurn>> _conversations =
        new(StringComparer.Ordinal);

    public ValueTask<SupportConversation> LoadAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        lock (_gate)
        {
            var turns = _conversations.TryGetValue(conversationId, out var storedTurns)
                ? storedTurns.ToArray()
                : [];

            return ValueTask.FromResult(
                new SupportConversation(conversationId, turns));
        }
    }

    public ValueTask AppendAsync(
        string conversationId,
        SupportConversationTurn turn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(turn);

        lock (_gate)
        {
            if (!_conversations.TryGetValue(conversationId, out var turns))
            {
                turns = [];
                _conversations[conversationId] = turns;
            }

            turns.Add(turn);
        }

        return ValueTask.CompletedTask;
    }
}

public interface ISupportWorkStore
{
    ValueTask<SupportWorkItem> CreateAsync(
        SupportRequest request,
        string correlationId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    ValueTask<SupportWorkItem?> GetAsync(
        string workItemId,
        CancellationToken cancellationToken = default);

    ValueTask<SupportWorkItem?> TryMarkProcessingAsync(
        string workItemId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    ValueTask<SupportWorkItem?> MarkCompletedAsync(
        string workItemId,
        SupportResponse response,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    ValueTask<SupportWorkItem?> MarkFailedAsync(
        string workItemId,
        string failureReason,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}

public sealed class InMemorySupportWorkStore : ISupportWorkStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SupportWorkItem> _workItems =
        new(StringComparer.Ordinal);

    public ValueTask<SupportWorkItem> CreateAsync(
        SupportRequest request,
        string correlationId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var workItemId = Guid.NewGuid().ToString("N");
        var storedRequest = request with { CorrelationId = correlationId };
        var workItem = new SupportWorkItem(
            workItemId,
            storedRequest,
            correlationId,
            SupportWorkItemStatus.Pending,
            createdAt);

        lock (_gate)
        {
            _workItems.Add(workItemId, workItem);
        }

        return ValueTask.FromResult(workItem);
    }

    public ValueTask<SupportWorkItem?> GetAsync(
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);

        lock (_gate)
        {
            _workItems.TryGetValue(workItemId, out var workItem);
            return ValueTask.FromResult(workItem);
        }
    }

    public ValueTask<SupportWorkItem?> TryMarkProcessingAsync(
        string workItemId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_workItems.TryGetValue(workItemId, out var workItem)
                || workItem.Status != SupportWorkItemStatus.Pending)
            {
                return ValueTask.FromResult<SupportWorkItem?>(null);
            }

            var processing = workItem with
            {
                Status = SupportWorkItemStatus.Processing,
                StartedAt = startedAt
            };
            _workItems[workItemId] = processing;
            return ValueTask.FromResult<SupportWorkItem?>(processing);
        }
    }

    public ValueTask<SupportWorkItem?> MarkCompletedAsync(
        string workItemId,
        SupportResponse response,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(response);

        lock (_gate)
        {
            if (!_workItems.TryGetValue(workItemId, out var workItem)
                || workItem.Status != SupportWorkItemStatus.Processing)
            {
                return ValueTask.FromResult<SupportWorkItem?>(null);
            }

            var completed = workItem with
            {
                Status = SupportWorkItemStatus.Completed,
                CompletedAt = completedAt,
                Response = response,
                Error = null
            };
            _workItems[workItemId] = completed;
            return ValueTask.FromResult<SupportWorkItem?>(completed);
        }
    }

    public ValueTask<SupportWorkItem?> MarkFailedAsync(
        string workItemId,
        string failureReason,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        lock (_gate)
        {
            if (!_workItems.TryGetValue(workItemId, out var workItem)
                || (workItem.Status != SupportWorkItemStatus.Pending
                    && workItem.Status != SupportWorkItemStatus.Processing))
            {
                return ValueTask.FromResult<SupportWorkItem?>(null);
            }

            var failed = workItem with
            {
                Status = SupportWorkItemStatus.Failed,
                CompletedAt = completedAt,
                Error = failureReason,
                Response = null
            };
            _workItems[workItemId] = failed;
            return ValueTask.FromResult<SupportWorkItem?>(failed);
        }
    }
}
