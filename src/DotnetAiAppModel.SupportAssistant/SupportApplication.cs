using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DotnetAiAppModel.SupportAssistant;

public sealed partial class SupportApplication
{
    private const string SpecialistFollowUpActionName = "request_specialist_follow_up";
    private static readonly ActivitySource ActivitySource =
        new("DotnetAiAppModel.SupportAssistant");

    private readonly IChatClient _chatClient;
    private readonly ITicketStore _ticketStore;
    private readonly ISupportConversationStore _conversationStore;
    private readonly ISupportActionStore _actionStore;
    private readonly SupportActionPolicy _actionPolicy;
    private readonly SupportModelOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SupportApplication> _logger;

    public SupportApplication(
        IChatClient chatClient,
        ITicketStore ticketStore,
        ISupportConversationStore conversationStore,
        ISupportActionStore actionStore,
        SupportActionPolicy actionPolicy,
        IOptions<SupportModelOptions> options,
        TimeProvider timeProvider,
        ILogger<SupportApplication> logger)
    {
        _chatClient = chatClient;
        _ticketStore = ticketStore;
        _conversationStore = conversationStore;
        _actionStore = actionStore;
        _actionPolicy = actionPolicy;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SupportResponse> HandleAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureValid(request);

        using var activity = ActivitySource.StartActivity("support.application.complete");
        var prepared = await PrepareAsync(request, activity, cancellationToken);
        using var scope = BeginScope(prepared);

        try
        {
            var response = await _chatClient.GetResponseAsync(
                prepared.Messages,
                CreateChatOptions(prepared.Tools),
                cancellationToken);

            var answer = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(answer))
            {
                throw new InvalidOperationException(
                    "The chat client returned an empty support response.");
            }

            await AppendAssistantTurnAsync(prepared, answer, cancellationToken);
            return new SupportResponse(
                answer,
                prepared.Request.TicketId,
                prepared.ConversationId,
                prepared.CorrelationId,
                prepared.ActionResults.ToArray());
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                LogCompleteCanceled(_logger, prepared.CorrelationId);
            }
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(
        SupportRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureValid(request);

        using var activity = ActivitySource.StartActivity("support.application.stream");
        var prepared = await PrepareAsync(request, activity, cancellationToken);
        using var scope = BeginScope(prepared);
        var answer = new StringBuilder();
        var yieldedText = false;

        try
        {
            await foreach (var update in _chatClient
                               .GetStreamingResponseAsync(
                                   prepared.Messages,
                                   CreateChatOptions(prepared.Tools),
                                   cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yieldedText = true;
                    answer.Append(update.Text);
                }

                yield return update;
            }

            if (!yieldedText)
            {
                throw new InvalidOperationException(
                    "The chat client returned no text updates.");
            }

            await AppendAssistantTurnAsync(
                prepared,
                answer.ToString().Trim(),
                cancellationToken);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                LogStreamingCanceled(_logger, prepared.CorrelationId);
            }
        }
    }

    private async ValueTask<PreparedSupportRequest> PrepareAsync(
        SupportRequest request,
        Activity? activity,
        CancellationToken cancellationToken)
    {
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

        var actionResults = new List<SupportActionResult>();
        var tools = new[]
        {
            SupportTools.CreateTicketLookupFunction(_ticketStore),
            SupportTools.CreateSpecialistFollowUpFunction(
                (reason, token) => RequestSpecialistFollowUpAsync(
                    requestWithCorrelation,
                    conversationId,
                    correlationId,
                    reason,
                    actionResults,
                    token))
        };

        activity?.SetTag("support.correlation_id", correlationId);
        activity?.SetTag("support.conversation_id", conversationId);
        activity?.SetTag("support.ticket_id", request.TicketId);
        activity?.SetTag("support.customer_id", request.CustomerId);

        return new PreparedSupportRequest(
            requestWithCorrelation,
            correlationId,
            conversationId,
            CreateMessages(conversation, request),
            tools,
            actionResults);
    }

    private async ValueTask AppendAssistantTurnAsync(
        PreparedSupportRequest prepared,
        string answer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException(
                "The support application cannot persist an empty assistant turn.");
        }

        await _conversationStore.AppendAsync(
            prepared.ConversationId,
            new SupportConversationTurn(
                SupportConversationRole.SupportAgent,
                answer,
                prepared.CorrelationId,
                _timeProvider.GetUtcNow()),
            cancellationToken);
    }

    private async ValueTask<SupportActionResult> RequestSpecialistFollowUpAsync(
        SupportRequest request,
        string conversationId,
        string correlationId,
        string reason,
        List<SupportActionResult> actionResults,
        CancellationToken cancellationToken)
    {
        var decision = _actionPolicy.EvaluateSpecialistFollowUp(request, reason);
        if (!decision.Allowed)
        {
            var denied = new SupportActionResult(
                SpecialistFollowUpActionName,
                "denied",
                Applied: false,
                AlreadyApplied: false,
                decision.Reason,
                ResolveIdempotencyKey(request, conversationId),
                _timeProvider.GetUtcNow());
            actionResults.Add(denied);
            return denied;
        }

        var action = await _actionStore.RequestSpecialistFollowUpAsync(
            new SupportActionRequest(
                conversationId,
                correlationId,
                request.TicketId,
                request.CustomerId,
                reason,
                ResolveIdempotencyKey(request, conversationId)),
            cancellationToken);
        actionResults.Add(action);
        return action;
    }

    private ChatOptions CreateChatOptions(IReadOnlyList<AIFunction> tools) =>
        new()
        {
            Instructions = _options.SystemPrompt,
            Tools = tools.Cast<AITool>().ToList(),
            AllowMultipleToolCalls = false
        };

    private static List<ChatMessage> CreateMessages(
        SupportConversation conversation,
        SupportRequest request)
    {
        var messages = conversation.Turns
            .Select(turn => new ChatMessage(
                turn.Role == SupportConversationRole.Customer
                    ? ChatRole.User
                    : ChatRole.Assistant,
                turn.Text))
            .ToList();

        var currentMessage = request.TicketId is null
            ? request.Message
            : $"{request.Message}{Environment.NewLine}Ticket ID: {request.TicketId}";
        messages.Add(new ChatMessage(ChatRole.User, currentMessage));
        return messages;
    }

    private static string ResolveIdempotencyKey(
        SupportRequest request,
        string conversationId) =>
        string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"{SpecialistFollowUpActionName}:{conversationId}:{request.TicketId}:{request.CustomerId}"
            : request.IdempotencyKey.Trim();

    private IDisposable? BeginScope(PreparedSupportRequest prepared) =>
        _logger.BeginScope(new Dictionary<string, object?>
        {
            ["SupportCorrelationId"] = prepared.CorrelationId,
            ["SupportConversationId"] = prepared.ConversationId,
            ["SupportTicketId"] = prepared.Request.TicketId
        });

    private static void EnsureValid(SupportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = SupportRequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            throw new ArgumentException(
                string.Join(" ", errors.Values.SelectMany(values => values)),
                nameof(request));
        }
    }

    private sealed record PreparedSupportRequest(
        SupportRequest Request,
        string CorrelationId,
        string ConversationId,
        IReadOnlyList<ChatMessage> Messages,
        IReadOnlyList<AIFunction> Tools,
        List<SupportActionResult> ActionResults);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Support streaming application canceled for correlation {CorrelationId}.")]
    private static partial void LogStreamingCanceled(
        ILogger logger,
        string correlationId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Support application canceled for correlation {CorrelationId}.")]
    private static partial void LogCompleteCanceled(
        ILogger logger,
        string correlationId);
}
