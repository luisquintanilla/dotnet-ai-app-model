using System.Text.Json.Serialization;

namespace DotnetAiAppModel.SupportAssistant;

public sealed record SupportRequest(
    string Message,
    string? TicketId = null,
    string? CustomerId = null,
    string? ConversationId = null,
    string? IdempotencyKey = null)
{
    [JsonIgnore]
    public string? CorrelationId { get; init; }

    public string ResolveConversationId(string correlationId) =>
        !string.IsNullOrWhiteSpace(ConversationId)
            ? ConversationId.Trim()
            : !string.IsNullOrWhiteSpace(CustomerId)
                ? $"customer:{CustomerId.Trim()}"
                : !string.IsNullOrWhiteSpace(TicketId)
                    ? $"ticket:{TicketId.Trim().ToUpperInvariant()}"
                    : $"correlation:{correlationId}";
}

public sealed record SupportResponse(
    string Answer,
    string? TicketId,
    string? ConversationId = null,
    string? CorrelationId = null,
    IReadOnlyList<SupportActionResult>? Actions = null);

public sealed record QueueAcceptedResponse(
    string CorrelationId,
    string? WorkItemId = null,
    string? StatusUrl = null,
    string Status = "pending");

public sealed record SupportStreamEvent([property: JsonPropertyName("text")] string Text);

public enum SupportConversationRole
{
    Customer,
    SupportAgent
}

public sealed record SupportConversationTurn(
    SupportConversationRole Role,
    string Text,
    string CorrelationId,
    DateTimeOffset RecordedAt);

public sealed record SupportConversation(
    string ConversationId,
    IReadOnlyList<SupportConversationTurn> Turns);

public sealed record SupportActionRequest(
    string ConversationId,
    string CorrelationId,
    string? TicketId,
    string? CustomerId,
    string Reason,
    string IdempotencyKey);

public sealed record SupportActionResult(
    string ActionName,
    string Status,
    bool Applied,
    bool AlreadyApplied,
    string Message,
    string IdempotencyKey,
    DateTimeOffset RecordedAt);

public sealed record SupportActionDecision(bool Allowed, string Reason);

public enum SupportWorkItemStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

public sealed record SupportWorkItem(
    string WorkItemId,
    SupportRequest Request,
    string CorrelationId,
    SupportWorkItemStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    SupportResponse? Response = null,
    string? Error = null);

public sealed record SupportWorkItemStatusResponse(
    string WorkItemId,
    string CorrelationId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    SupportResponse? Response,
    string? Error);

public sealed class SupportModelOptions
{
    public const string SectionName = "SupportModel";

    public string Provider { get; set; } = "scripted";

    public string Model { get; set; } = "gpt-4o-mini";

    public string? OpenAiApiKey { get; set; }

    public int MaxToolIterations { get; set; } = 4;

    public string SystemPrompt { get; set; } =
        "You are a concise support case assistant. Use the ticket lookup tool when a ticket ID is provided. Request specialist follow-up only when the customer explicitly asks for it.";
}

public sealed class SupportWorkOptions
{
    public const string SectionName = "SupportWork";

    public int QueueCapacity { get; set; } = 32;
}

public static class SupportRequestValidation
{
    public static Dictionary<string, string[]> Validate(SupportRequest? request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request is null)
        {
            errors["request"] = ["A support request is required."];
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            errors["message"] = ["Message is required."];
        }
        else if (request.Message.Length > 4000)
        {
            errors["message"] = ["Message must be 4000 characters or fewer."];
        }

        if (request.TicketId is { Length: > 100 })
        {
            errors["ticketId"] = ["TicketId must be 100 characters or fewer."];
        }

        if (request.CustomerId is { Length: > 100 })
        {
            errors["customerId"] = ["CustomerId must be 100 characters or fewer."];
        }

        if (request.ConversationId is { Length: > 200 })
        {
            errors["conversationId"] = ["ConversationId must be 200 characters or fewer."];
        }

        if (request.IdempotencyKey is { Length: > 200 })
        {
            errors["idempotencyKey"] = ["IdempotencyKey must be 200 characters or fewer."];
        }

        return errors;
    }
}
