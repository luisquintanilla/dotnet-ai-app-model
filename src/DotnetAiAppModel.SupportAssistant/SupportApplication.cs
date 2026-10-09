using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DotnetAiAppModel.SupportAssistant;

public sealed partial class SupportApplication
{
    private static readonly ActivitySource ActivitySource =
        new("DotnetAiAppModel.SupportAssistant");

    private readonly IChatClient _chatClient;
    private readonly SupportConversationService _conversationService;
    private readonly SupportModelContextFactory _modelContextFactory;
    private readonly ILogger<SupportApplication> _logger;

    public SupportApplication(
        IChatClient chatClient,
        SupportConversationService conversationService,
        SupportModelContextFactory modelContextFactory,
        ILogger<SupportApplication> logger)
    {
        _chatClient = chatClient;
        _conversationService = conversationService;
        _modelContextFactory = modelContextFactory;
        _logger = logger;
    }

    public async Task<SupportResponse> HandleAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureValid(request);

        using var activity = ActivitySource.StartActivity("support.application.complete");
        var prepared = await _conversationService.PrepareAsync(
            request,
            cancellationToken);
        SetActivityTags(activity, prepared);
        using var scope = BeginScope(prepared);
        var modelContext = _modelContextFactory.Create(prepared);

        try
        {
            var response = await _chatClient.GetResponseAsync(
                modelContext.Messages,
                modelContext.Options,
                cancellationToken);

            var answer = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(answer))
            {
                throw new InvalidOperationException(
                    "The chat client returned an empty support response.");
            }

            await _conversationService.AppendAssistantTurnAsync(
                prepared,
                answer,
                cancellationToken);
            return new SupportResponse(
                answer,
                prepared.Request.TicketId,
                prepared.ConversationId,
                prepared.CorrelationId,
                modelContext.ActionResults.ToArray());
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
        var prepared = await _conversationService.PrepareAsync(
            request,
            cancellationToken);
        SetActivityTags(activity, prepared);
        using var scope = BeginScope(prepared);
        var modelContext = _modelContextFactory.Create(prepared);
        var answer = new System.Text.StringBuilder();
        var yieldedText = false;

        try
        {
            await foreach (var update in _chatClient
                               .GetStreamingResponseAsync(
                                   modelContext.Messages,
                                   modelContext.Options,
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

            await _conversationService.AppendAssistantTurnAsync(
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

    private static void SetActivityTags(
        Activity? activity,
        SupportConversationContext prepared)
    {
        activity?.SetTag("support.correlation_id", prepared.CorrelationId);
        activity?.SetTag("support.conversation_id", prepared.ConversationId);
        activity?.SetTag("support.ticket_id", prepared.Request.TicketId);
        activity?.SetTag("support.customer_id", prepared.Request.CustomerId);
    }

    private IDisposable? BeginScope(SupportConversationContext prepared) =>
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
