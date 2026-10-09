namespace DotnetAiAppModel.SupportAssistant;

public sealed class SupportActionPolicy
{
    private static readonly string[] ExplicitFollowUpRequests =
    [
        "specialist",
        "human",
        "representative",
        "agent",
        "escalate"
    ];

    public SupportActionDecision EvaluateSpecialistFollowUp(
        SupportRequest request,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TicketId))
        {
            return new(false, "A ticket ID is required before requesting specialist follow-up.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            return new(false, "A customer ID is required before requesting specialist follow-up.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return new(false, "A reason is required before requesting specialist follow-up.");
        }

        if (!ExplicitFollowUpRequests.Any(
                keyword => request.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return new(false, "Specialist follow-up requires an explicit customer request.");
        }

        return new(true, "The support request is eligible for specialist follow-up.");
    }
}

public interface ISupportActionStore
{
    ValueTask<SupportActionResult> RequestSpecialistFollowUpAsync(
        SupportActionRequest request,
        CancellationToken cancellationToken = default);

    IReadOnlyList<SupportActionResult> GetRecordedActions();
}

public sealed class InMemorySupportActionStore : ISupportActionStore
{
    private const string ActionName = "request_specialist_follow_up";
    private readonly object _gate = new();
    private readonly Dictionary<string, SupportActionResult> _actions =
        new(StringComparer.Ordinal);
    private readonly List<string> _actionOrder = [];
    private readonly TimeProvider _timeProvider;

    public InMemorySupportActionStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<SupportActionResult> RequestSpecialistFollowUpAsync(
        SupportActionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (_actions.TryGetValue(request.IdempotencyKey, out var existing))
            {
                return ValueTask.FromResult(
                    existing with
                    {
                        Status = "already_applied",
                        AlreadyApplied = true,
                        Message = "Specialist follow-up was already requested for this idempotency key."
                    });
            }

            var result = new SupportActionResult(
                ActionName,
                "applied",
                Applied: true,
                AlreadyApplied: false,
                "Specialist follow-up was recorded for the support ticket.",
                request.IdempotencyKey,
                _timeProvider.GetUtcNow());

            _actions.Add(request.IdempotencyKey, result);
            _actionOrder.Add(request.IdempotencyKey);
            return ValueTask.FromResult(result);
        }
    }

    public IReadOnlyList<SupportActionResult> GetRecordedActions()
    {
        lock (_gate)
        {
            return _actionOrder
                .Select(key => _actions[key])
                .ToArray();
        }
    }
}
