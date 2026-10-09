using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DotnetAiAppModel.SupportAssistant;

public sealed class SupportAssistant
{
    private static readonly ActivitySource ActivitySource = new("DotnetAiAppModel.SupportAssistant");

    private readonly IChatClient _chatClient;
    private readonly AIFunction _ticketLookup;
    private readonly SupportAssistantOptions _options;
    private readonly ILogger<SupportAssistant> _logger;

    public SupportAssistant(
        IChatClient chatClient,
        AIFunction ticketLookup,
        IOptions<SupportAssistantOptions> options,
        ILogger<SupportAssistant> logger)
    {
        _chatClient = chatClient;
        _ticketLookup = ticketLookup;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SupportResponse> GetResponseAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureValid(request);

        using var activity = ActivitySource.StartActivity("support.assistant.response");
        SetActivityTags(activity, request);
        using var scope = BeginScope(request);

        var response = await _chatClient.GetResponseAsync(
            CreateMessages(request),
            CreateChatOptions(),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("The chat client returned an empty support response.");
        }

        return new SupportResponse(response.Text.Trim(), request.TicketId);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        SupportRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureValid(request);

        using var activity = ActivitySource.StartActivity("support.assistant.stream");
        SetActivityTags(activity, request);
        using var scope = BeginScope(request);

        var yieldedText = false;
        await foreach (var update in _chatClient.GetStreamingResponseAsync(
                           CreateMessages(request),
                           CreateChatOptions(),
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

    private ChatOptions CreateChatOptions() =>
        new()
        {
            Instructions = _options.SystemPrompt,
            Tools = [_ticketLookup],
            AllowMultipleToolCalls = false
        };

    private static IReadOnlyList<ChatMessage> CreateMessages(SupportRequest request)
    {
        var message = request.TicketId is null
            ? request.Message
            : $"{request.Message}{Environment.NewLine}Ticket ID: {request.TicketId}";

        return [new ChatMessage(ChatRole.User, message)];
    }

    private IDisposable? BeginScope(SupportRequest request) =>
        _logger.BeginScope(new Dictionary<string, object?>
        {
            ["SupportCorrelationId"] = request.CorrelationId ?? "direct",
            ["SupportTicketId"] = request.TicketId
        });

    private static void SetActivityTags(Activity? activity, SupportRequest request)
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
            throw new ArgumentException(string.Join(" ", errors.Values.SelectMany(values => values)), nameof(request));
        }
    }
}
