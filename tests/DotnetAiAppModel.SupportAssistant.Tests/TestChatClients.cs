using System.Runtime.CompilerServices;

namespace DotnetAiAppModel.SupportAssistant.Tests;

internal sealed class ThrowingChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ChatResponse>(
            new InvalidOperationException("intentional test failure"));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        throw new InvalidOperationException("intentional test failure");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class CancellationChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromCanceled<ChatResponse>(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.FromCanceled(cancellationToken);
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class StreamingCancellationChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "complete")));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class FailingThenSuccessfulChatClient : IChatClient
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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, "processed");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class CapturingChatClient : IChatClient
{
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public List<IReadOnlyList<string>> ToolNames { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToArray());
        ToolNames.Add(
            options?.Tools?
                .OfType<AIFunction>()
                .Select(function => function.Name)
                .ToArray()
            ?? []);
        return Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "captured")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToArray());
        ToolNames.Add(
            options?.Tools?
                .OfType<AIFunction>()
                .Select(function => function.Name)
                .ToArray()
            ?? []);
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, "captured");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
