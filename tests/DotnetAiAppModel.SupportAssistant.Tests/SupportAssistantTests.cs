using Microsoft.Extensions.AI;

namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class SupportHandlerTests
{
    [Fact]
    public async Task BufferedResponseUsesTheScriptedClient()
    {
        using var host = TestHandlerDependencies.Create();

        var response = await SupportHandlers.CompleteAsync(
            new SupportRequest("How do I update my contact email?"),
            host.ChatClient,
            host.TicketLookup,
            host.Options,
            host.Logger);

        Assert.Equal(
            "Thanks for contacting support. We received your message and will follow up shortly.",
            response.Answer);
        Assert.Null(response.TicketId);
    }

    [Fact]
    public async Task ScriptedToolCallRoundTripInvokesTicketLookup()
    {
        using var host = TestHandlerDependencies.Create();

        var response = await SupportHandlers.CompleteAsync(
            new SupportRequest("What is the status of my ticket?", "SUP-1001"),
            host.ChatClient,
            host.TicketLookup,
            host.Options,
            host.Logger);

        Assert.Equal(
            "Ticket SUP-1001 is open: A billing specialist is reviewing the duplicate charge.",
            response.Answer);
        Assert.Equal("SUP-1001", response.TicketId);
    }

    [Fact]
    public async Task BufferedResponsePropagatesCancellation()
    {
        using var host = TestHandlerDependencies.Create(new CancellationChatClient());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SupportHandlers.CompleteAsync(
                new SupportRequest("This request is canceled."),
                host.ChatClient,
                host.TicketLookup,
                host.Options,
                host.Logger,
                cancellation.Token));
    }

    private sealed class CancellationChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromCanceled<ChatResponse>(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.FromCanceled(cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
