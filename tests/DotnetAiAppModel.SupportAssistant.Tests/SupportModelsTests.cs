namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class SupportModelsTests
{
    [Fact]
    public void ConversationIdResolutionPrefersExplicitCustomerTicketThenCorrelation()
    {
        Assert.Equal(
            "explicit",
            new SupportRequest("message", ConversationId: " explicit ")
                .ResolveConversationId("correlation"));
        Assert.Equal(
            "customer:customer-42",
            new SupportRequest("message", CustomerId: " customer-42 ")
                .ResolveConversationId("correlation"));
        Assert.Equal(
            "ticket:SUP-1001",
            new SupportRequest("message", TicketId: " sup-1001 ")
                .ResolveConversationId("correlation"));
        Assert.Equal(
            "correlation:correlation",
            new SupportRequest("message").ResolveConversationId("correlation"));
    }

    [Fact]
    public void InvalidSupportRequestReportsDomainFields()
    {
        var errors = SupportRequestValidation.Validate(
            new SupportRequest(
                string.Empty,
                CustomerId: new string('c', 101),
                ConversationId: new string('v', 201),
                IdempotencyKey: new string('i', 201)));

        Assert.Contains("message", errors.Keys);
        Assert.Contains("customerId", errors.Keys);
        Assert.Contains("conversationId", errors.Keys);
        Assert.Contains("idempotencyKey", errors.Keys);
    }
}
