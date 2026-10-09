namespace DotnetAiAppModel.SupportAssistant;

public sealed class SupportConversationService
{
    private readonly ISupportConversationStore _conversationStore;
    private readonly TimeProvider _timeProvider;

    public SupportConversationService(
        ISupportConversationStore conversationStore,
        TimeProvider timeProvider)
    {
        _conversationStore = conversationStore;
        _timeProvider = timeProvider;
    }

    public async ValueTask<SupportConversationContext> PrepareAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var requestWithCorrelation = request with { CorrelationId = correlationId };
        var conversationId = request.ResolveConversationId(correlationId);
        var conversation = await _conversationStore.LoadAsync(
            conversationId,
            cancellationToken);

        await _conversationStore.AppendAsync(
            conversationId,
            new SupportConversationTurn(
                SupportConversationRole.Customer,
                request.Message,
                correlationId,
                _timeProvider.GetUtcNow()),
            cancellationToken);

        return new SupportConversationContext(
            requestWithCorrelation,
            correlationId,
            conversationId,
            conversation,
            []);
    }

    public ValueTask AppendAssistantTurnAsync(
        SupportConversationContext context,
        string answer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException(
                "The support application cannot persist an empty assistant turn.");
        }

        return _conversationStore.AppendAsync(
            context.ConversationId,
            new SupportConversationTurn(
                SupportConversationRole.SupportAgent,
                answer,
                context.CorrelationId,
                _timeProvider.GetUtcNow()),
            cancellationToken);
    }
}

public sealed record SupportConversationContext(
    SupportRequest Request,
    string CorrelationId,
    string ConversationId,
    SupportConversation Conversation,
    List<SupportActionResult> ActionResults);
