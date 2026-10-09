using System.Text.Json.Serialization;

namespace DotnetAiAppModel.SupportAssistant;

public sealed record SupportRequest(string Message, string? TicketId = null)
{
    [JsonIgnore]
    public string? CorrelationId { get; init; }
}

public sealed record SupportResponse(string Answer, string? TicketId);

public sealed record QueueAcceptedResponse(string CorrelationId);

public sealed record SupportStreamEvent([property: JsonPropertyName("text")] string Text);

public sealed class SupportAssistantOptions
{
    public const string SectionName = "SupportAssistant";

    public string Provider { get; set; } = "scripted";

    public string Model { get; set; } = "gpt-4o-mini";

    public string? OpenAiApiKey { get; set; }

    public int QueueCapacity { get; set; } = 32;

    public int MaxToolIterations { get; set; } = 4;

    public string SystemPrompt { get; set; } =
        "You are a concise support assistant. Use the ticket lookup tool when a ticket ID is provided.";
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

        return errors;
    }
}
