using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace DotnetAiAppModel.SupportAssistant;

public sealed record TicketLookupResult(string TicketId, string Status, string Summary);

public interface ITicketStore
{
    ValueTask<TicketLookupResult> LookupAsync(string ticketId, CancellationToken cancellationToken);
}

public sealed class InMemoryTicketStore : ITicketStore
{
    private readonly Dictionary<string, TicketLookupResult> _tickets =
        new Dictionary<string, TicketLookupResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["SUP-1001"] = new("SUP-1001", "open", "A billing specialist is reviewing the duplicate charge."),
            ["SUP-1002"] = new("SUP-1002", "pending_customer", "The support team is waiting for the requested log file.")
        };

    public ValueTask<TicketLookupResult> LookupAsync(
        string ticketId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return ValueTask.FromResult(new TicketLookupResult(
                string.Empty,
                "not_found",
                "No ticket ID was provided."));
        }

        var normalizedTicketId = ticketId.Trim().ToUpperInvariant();
        return ValueTask.FromResult(
            _tickets.TryGetValue(normalizedTicketId, out var ticket)
                ? ticket
                : new TicketLookupResult(
                    normalizedTicketId,
                    "not_found",
                    "No ticket with that ID was found."));
    }
}

public static class SupportTools
{
    public static AIFunction CreateTicketLookupFunction(ITicketStore ticketStore)
    {
        ArgumentNullException.ThrowIfNull(ticketStore);

        return AIFunctionFactory.Create(
            (string ticketId, CancellationToken cancellationToken) =>
                ticketStore.LookupAsync(ticketId, cancellationToken),
            new AIFunctionFactoryOptions
            {
                Name = "lookup_ticket",
                Description = "Looks up the current status and summary for a support ticket."
            });
    }

    public static AIFunction CreateSpecialistFollowUpFunction(
        Func<string, CancellationToken, ValueTask<SupportActionResult>> requestFollowUp)
    {
        ArgumentNullException.ThrowIfNull(requestFollowUp);

        return AIFunctionFactory.Create(
            (string reason, CancellationToken cancellationToken) =>
                requestFollowUp(reason, cancellationToken),
            new AIFunctionFactoryOptions
            {
                Name = "request_specialist_follow_up",
                Description = "Requests an idempotent specialist follow-up for an eligible support ticket."
            });
    }
}
