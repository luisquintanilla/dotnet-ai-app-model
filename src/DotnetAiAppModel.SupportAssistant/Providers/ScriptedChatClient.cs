using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace DotnetAiAppModel.SupportAssistant.Providers;

public sealed class ScriptedChatClient : IChatClient
{
    private static readonly Regex TicketIdRegex =
        new(@"Ticket ID:\s*(?<ticketId>[A-Za-z0-9-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _modelId;

    public ScriptedChatClient(string modelId = "scripted-support")
    {
        _modelId = modelId;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateResponse(messages, options));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = CreateResponse(messages, options);

        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (content is TextContent textContent)
                {
                    foreach (var chunk in Chunk(textContent.Text))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                        yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
                    }
                }
                else
                {
                    yield return new ChatResponseUpdate(
                        message.Role,
                        new List<AIContent> { content });
                }
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private ChatResponse CreateResponse(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options)
    {
        var materializedMessages = messages.ToArray();
        var functionResult = materializedMessages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .LastOrDefault();

        if (functionResult is not null)
        {
            return TextResponse(
                IsActionResult(functionResult.Result)
                    ? CreateActionAnswer(functionResult.Result)
                    : CreateTicketAnswer(functionResult.Result));
        }

        var ticketId = FindTicketId(materializedMessages);
        var specialistFunction = options?.Tools?
            .OfType<AIFunction>()
            .FirstOrDefault(function =>
                string.Equals(
                    function.Name,
                    "request_specialist_follow_up",
                    StringComparison.Ordinal));
        if (specialistFunction is not null
            && WantsSpecialistFollowUp(materializedMessages))
        {
            var functionCall = new FunctionCallContent(
                "scripted-specialist-follow-up",
                specialistFunction.Name,
                new Dictionary<string, object?>
                {
                    ["reason"] = "The customer explicitly requested specialist follow-up."
                });

            return new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                new List<AIContent> { functionCall }));
        }

        var ticketFunction = options?.Tools?
            .OfType<AIFunction>()
            .FirstOrDefault(function =>
                string.Equals(
                    function.Name,
                    "lookup_ticket",
                    StringComparison.Ordinal));
        if (ticketId is not null && ticketFunction is not null)
        {
            var functionCall = new FunctionCallContent(
                "scripted-ticket-lookup",
                ticketFunction.Name,
                new Dictionary<string, object?>
                {
                    ["ticketId"] = ticketId
                });

            return new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                new List<AIContent> { functionCall }));
        }

        return TextResponse(
            "Thanks for contacting support. We received your message and will follow up shortly.");
    }

    private static string CreateActionAnswer(object? result)
    {
        var action = result switch
        {
            SupportActionResult typedAction => typedAction,
            JsonElement json when json.ValueKind == JsonValueKind.Object =>
                json.Deserialize<SupportActionResult>(JsonOptions),
            _ => null
        };

        return action is not null
            ? action.Status switch
            {
                "denied" => $"I could not request specialist follow-up: {action.Message}",
                "already_applied" => action.Message,
                _ => action.Message
            }
            : $"Specialist follow-up result: {result}";
    }

    private static string CreateTicketAnswer(object? result)
    {
        var ticket = result switch
        {
            TicketLookupResult typedTicket => typedTicket,
            JsonElement json when json.ValueKind == JsonValueKind.Object =>
                json.Deserialize<TicketLookupResult>(JsonOptions),
            _ => null
        };

        return ticket is not null
            ? $"Ticket {ticket.TicketId} is {ticket.Status}: {ticket.Summary}"
            : $"Ticket lookup completed: {result}";
    }

    private static bool IsActionResult(object? result) =>
        result is SupportActionResult
            || result is JsonElement json
                && json.ValueKind == JsonValueKind.Object
                && json.TryGetProperty("actionName", out _);

    private static bool WantsSpecialistFollowUp(IEnumerable<ChatMessage> messages)
    {
        var latestUserMessage = messages
            .Reverse()
            .FirstOrDefault(message => message.Role == ChatRole.User);
        return latestUserMessage?.Text?.Contains(
                   "specialist",
                   StringComparison.OrdinalIgnoreCase) == true
            || latestUserMessage?.Text?.Contains(
                   "human",
                   StringComparison.OrdinalIgnoreCase) == true
            || latestUserMessage?.Text?.Contains(
                   "representative",
                   StringComparison.OrdinalIgnoreCase) == true
            || latestUserMessage?.Text?.Contains(
                   "escalate",
                   StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? FindTicketId(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages.Reverse())
        {
            var match = TicketIdRegex.Match(message.Text ?? string.Empty);
            if (match.Success)
            {
                return match.Groups["ticketId"].Value;
            }
        }

        return null;
    }

    private ChatResponse TextResponse(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = _modelId
        };

    private static IEnumerable<string> Chunk(string text)
    {
        const int chunkSize = 12;

        for (var index = 0; index < text.Length; index += chunkSize)
        {
            yield return text.Substring(index, Math.Min(chunkSize, text.Length - index));
        }
    }
}
