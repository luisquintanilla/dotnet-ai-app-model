using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DotnetAiAppModel.SupportAssistant;

public static class SupportHandlers
{
    private static readonly ActivitySource ActivitySource =
        new("DotnetAiAppModel.SupportAssistant");

    public static async Task<SupportResponse> CompleteAsync(
        SupportRequest request,
        IChatClient chatClient,
        AIFunction ticketLookup,
        SupportAssistantOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        EnsureValid(request);

        using var activity = ActivitySource.StartActivity("support.handler.complete");
        SetActivityTags(activity, request);
        using var scope = BeginScope(logger, request);

        var response = await chatClient.GetResponseAsync(
            CreateMessages(request),
            CreateChatOptions(ticketLookup, options),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("The chat client returned an empty support response.");
        }

        return new SupportResponse(response.Text.Trim(), request.TicketId);
    }

    public static async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(
        SupportRequest request,
        IChatClient chatClient,
        AIFunction ticketLookup,
        SupportAssistantOptions options,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureValid(request);

        using var activity = ActivitySource.StartActivity("support.handler.stream");
        SetActivityTags(activity, request);
        using var scope = BeginScope(logger, request);

        var yieldedText = false;
        await foreach (var update in chatClient.GetStreamingResponseAsync(
                           CreateMessages(request),
                           CreateChatOptions(ticketLookup, options),
                           cancellationToken).WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yieldedText = true;
            }

            yield return update;
        }

        if (!yieldedText)
        {
            throw new InvalidOperationException("The chat client returned no text updates.");
        }
    }

    private static ChatOptions CreateChatOptions(
        AIFunction ticketLookup,
        SupportAssistantOptions options) =>
        new()
        {
            Instructions = options.SystemPrompt,
            Tools = [ticketLookup],
            AllowMultipleToolCalls = false
        };

    private static IReadOnlyList<ChatMessage> CreateMessages(SupportRequest request)
    {
        var message = request.TicketId is null
            ? request.Message
            : $"{request.Message}{Environment.NewLine}Ticket ID: {request.TicketId}";

        return [new ChatMessage(ChatRole.User, message)];
    }

    private static IDisposable? BeginScope(
        ILogger logger,
        SupportRequest request) =>
        logger.BeginScope(new Dictionary<string, object?>
        {
            ["SupportCorrelationId"] = request.CorrelationId ?? "direct",
            ["SupportTicketId"] = request.TicketId
        });

    private static void SetActivityTags(
        Activity? activity,
        SupportRequest request)
    {
        activity?.SetTag("support.correlation_id", request.CorrelationId);
        activity?.SetTag("support.ticket_id", request.TicketId);
    }

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
}
