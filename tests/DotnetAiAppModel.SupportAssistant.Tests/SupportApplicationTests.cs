namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class SupportApplicationTests
{
    [Fact]
    public async Task BufferedResponsePersistsCustomerAndAssistantTurns()
    {
        using var time = new TestTimeProvider();
        using var host = TestSupportApplicationHost.Create(timeProvider: time);

        var response = await host.Application.HandleAsync(
            new SupportRequest(
                "How do I update my contact email?",
                CustomerId: "CUS-42",
                ConversationId: "conversation-42")
            {
                CorrelationId = "correlation-42"
            });

        Assert.StartsWith("Thanks for contacting support.", response.Answer);
        Assert.Equal("conversation-42", response.ConversationId);
        Assert.Equal("correlation-42", response.CorrelationId);

        var conversation = await host.ConversationStore.LoadAsync("conversation-42");
        Assert.Collection(
            conversation.Turns,
            customer =>
            {
                Assert.Equal(SupportConversationRole.Customer, customer.Role);
                Assert.Equal("How do I update my contact email?", customer.Text);
                Assert.Equal("correlation-42", customer.CorrelationId);
                Assert.Equal(time.GetUtcNow(), customer.RecordedAt);
            },
            assistant =>
            {
                Assert.Equal(SupportConversationRole.SupportAgent, assistant.Role);
                Assert.Equal(response.Answer, assistant.Text);
                Assert.Equal("correlation-42", assistant.CorrelationId);
                Assert.Equal(time.GetUtcNow(), assistant.RecordedAt);
            });
    }

    [Fact]
    public async Task BufferedRequestInvokesReadOnlyTicketLookup()
    {
        using var host = TestSupportApplicationHost.Create();

        var response = await host.Application.HandleAsync(
            new SupportRequest("What is the status of my ticket?", "SUP-1001"));

        Assert.Equal(
            "Ticket SUP-1001 is open: A billing specialist is reviewing the duplicate charge.",
            response.Answer);
        Assert.Equal("SUP-1001", response.TicketId);
        Assert.Empty(host.ActionStore.GetRecordedActions());
    }

    [Fact]
    public async Task ApplicationLoadsPriorTurnsOnlyForTheSameConversation()
    {
        var client = new CapturingChatClient();
        using var host = TestSupportApplicationHost.Create(client);

        await host.Application.HandleAsync(
            new SupportRequest("first", ConversationId: "conversation-a"));
        await host.Application.HandleAsync(
            new SupportRequest("second", ConversationId: "conversation-a"));
        await host.Application.HandleAsync(
            new SupportRequest("other", ConversationId: "conversation-b"));

        Assert.Contains(
            client.Requests[1],
            message => message.Text == "first");
        Assert.DoesNotContain(
            client.Requests[2],
            message => message.Text == "first");
        Assert.Equal(
            ["lookup_ticket", "request_specialist_follow_up"],
            client.ToolNames[0]);
    }

    [Fact]
    public async Task PolicyGatedFollowUpIsRecordedOnceForRepeatedIdempotencyKey()
    {
        using var host = TestSupportApplicationHost.Create();
        var request = new SupportRequest(
            "Please escalate this to a specialist.",
            "SUP-1001",
            CustomerId: "CUS-42",
            ConversationId: "conversation-42",
            IdempotencyKey: "follow-up-42");

        var first = await host.Application.HandleAsync(request);
        var second = await host.Application.HandleAsync(request);

        var firstAction = Assert.Single(first.Actions!);
        Assert.Equal("applied", firstAction.Status);
        Assert.True(firstAction.Applied);
        Assert.False(firstAction.AlreadyApplied);

        var secondAction = Assert.Single(second.Actions!);
        Assert.Equal("already_applied", secondAction.Status);
        Assert.True(secondAction.Applied);
        Assert.True(secondAction.AlreadyApplied);
        Assert.Single(host.ActionStore.GetRecordedActions());
    }

    [Fact]
    public async Task PolicyDeniesFollowUpWithoutCustomerIdentity()
    {
        using var host = TestSupportApplicationHost.Create();

        var response = await host.Application.HandleAsync(
            new SupportRequest(
                "Please escalate this to a specialist.",
                "SUP-1001"));

        var action = Assert.Single(response.Actions!);
        Assert.Equal("denied", action.Status);
        Assert.False(action.Applied);
        Assert.Contains("customer ID", action.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(host.ActionStore.GetRecordedActions());
    }

    [Fact]
    public async Task ModelFailureLeavesCustomerTurnWithoutAssistantTurn()
    {
        using var host = TestSupportApplicationHost.Create(new ThrowingChatClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Application.HandleAsync(
                new SupportRequest("This model call will fail.", ConversationId: "failed")));

        var conversation = await host.ConversationStore.LoadAsync("failed");
        var turn = Assert.Single(conversation.Turns);
        Assert.Equal(SupportConversationRole.Customer, turn.Role);
        Assert.Equal("This model call will fail.", turn.Text);
    }

    [Fact]
    public async Task CanceledBufferedRequestPropagatesCancellation()
    {
        using var host = TestSupportApplicationHost.Create(new CancellationChatClient());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.Application.HandleAsync(
                new SupportRequest("This request is canceled."),
                cancellation.Token));
    }
}
