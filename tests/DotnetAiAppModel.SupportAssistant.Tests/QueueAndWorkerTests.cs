using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class QueueAndWorkerTests
{
    [Fact]
    public async Task BoundedChannelRejectsWhenFull()
    {
        var channel = new SupportRequestChannel(1);
        var first = new SupportRequest("first");
        var second = new SupportRequest("second");

        Assert.True(channel.TryEnqueue(first));
        Assert.False(channel.TryEnqueue(second));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var request in channel.ReadAllAsync(cancellation.Token))
        {
            Assert.Same(first, request);
            break;
        }
    }

    [Fact]
    public async Task WorkerConsumesQueuedRequestWithTheSameHandler()
    {
        var channel = new SupportRequestChannel(2);
        var client = new RecordingChatClient();
        using var host = TestHandlerDependencies.Create(client);
        using var worker = new SupportWorker(
            channel,
            host.ChatClient,
            host.TicketLookup,
            Options.Create(host.Options),
            NullLogger<SupportWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        Assert.True(channel.TryEnqueue(new SupportRequest("queued", "SUP-1001")
        {
            CorrelationId = "queue-correlation"
        }));

        var request = await client.ReceivedRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("queued", request.Message);
        Assert.Equal("SUP-1001", request.TicketId);
        Assert.Equal("queue-correlation", request.CorrelationId);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WorkerLogsRequestFailureAndContinuesConsuming()
    {
        var channel = new SupportRequestChannel(2);
        var client = new FailingThenSuccessfulChatClient();
        using var host = TestHandlerDependencies.Create(client);
        using var worker = new SupportWorker(
            channel,
            host.ChatClient,
            host.TicketLookup,
            Options.Create(host.Options),
            NullLogger<SupportWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        Assert.True(channel.TryEnqueue(new SupportRequest("first")));
        Assert.True(channel.TryEnqueue(new SupportRequest("second")));

        var calls = await client.CompletedCalls.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);

        await worker.StopAsync(CancellationToken.None);
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public TaskCompletionSource<SupportRequest> ReceivedRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var text = messages.Single(message => message.Role == ChatRole.User).Text ?? string.Empty;
            var ticketId = text.Split("Ticket ID: ", StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            ReceivedRequest.TrySetResult(new SupportRequest(
                text.Split(Environment.NewLine, StringSplitOptions.None)[0],
                ticketId)
            {
                CorrelationId = "queue-correlation"
            });

            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "processed")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "processed");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FailingThenSuccessfulChatClient : IChatClient
    {
        private int _calls;

        public TaskCompletionSource<int> CompletedCalls { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var calls = Interlocked.Increment(ref _calls);
            if (calls == 2)
            {
                CompletedCalls.TrySetResult(calls);
            }

            return calls == 1
                ? Task.FromException<ChatResponse>(
                    new InvalidOperationException("intentional test failure"))
                : Task.FromResult(
                    new ChatResponse(new ChatMessage(ChatRole.Assistant, "processed")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "processed");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
