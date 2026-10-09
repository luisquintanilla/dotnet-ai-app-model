namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class StreamingTests
{
    [Fact]
    public async Task StreamingApplicationPreservesUpdateOrderAndPersistsAnswer()
    {
        using var host = TestSupportApplicationHost.Create();
        var updates = new List<string>();

        await foreach (var update in host.Application.StreamAsync(
                           new SupportRequest(
                               "Please explain the next step.",
                               ConversationId: "stream-1")))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                updates.Add(update.Text);
            }
        }

        Assert.True(updates.Count > 1);
        var answer = string.Concat(updates);
        Assert.Equal(
            "Thanks for contacting support. We received your message and will follow up shortly.",
            answer);

        var conversation = await host.ConversationStore.LoadAsync("stream-1");
        Assert.Collection(
            conversation.Turns,
            customer => Assert.Equal(SupportConversationRole.Customer, customer.Role),
            assistant =>
            {
                Assert.Equal(SupportConversationRole.SupportAgent, assistant.Role);
                Assert.Equal(answer, assistant.Text);
            });
    }

    [Fact]
    public async Task StreamingApplicationReturnsTheFinalToolBackedText()
    {
        using var host = TestSupportApplicationHost.Create();
        var text = new List<string>();

        await foreach (var update in host.Application.StreamAsync(
                           new SupportRequest(
                               "Can you check my ticket?",
                               "SUP-1002",
                               ConversationId: "stream-ticket")))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                text.Add(update.Text);
            }
        }

        Assert.Equal(
            "Ticket SUP-1002 is pending_customer: The support team is waiting for the requested log file.",
            string.Concat(text));
    }

    [Fact]
    public async Task CanceledStreamPersistsCustomerTurnButNotPartialAssistantTurn()
    {
        using var host = TestSupportApplicationHost.Create(
            new StreamingCancellationChatClient());
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = host.Application
            .StreamAsync(
                new SupportRequest(
                    "Please stream this response.",
                    ConversationId: "stream-cancel"),
                cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("partial", enumerator.Current.Text);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            enumerator.MoveNextAsync().AsTask());

        var conversation = await host.ConversationStore.LoadAsync("stream-cancel");
        var turn = Assert.Single(conversation.Turns);
        Assert.Equal(SupportConversationRole.Customer, turn.Role);
        Assert.Equal("Please stream this response.", turn.Text);
    }
}
