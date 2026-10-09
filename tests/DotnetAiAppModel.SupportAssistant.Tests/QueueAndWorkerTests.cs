using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class QueueAndWorkerTests
{
    [Fact]
    public async Task BoundedChannelRejectsWhenFull()
    {
        var channel = new SupportWorkChannel(1);

        Assert.True(channel.TryEnqueue("work-1"));
        Assert.False(channel.TryEnqueue("work-2"));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var workItemId in channel.ReadAllAsync(cancellation.Token))
        {
            Assert.Equal("work-1", workItemId);
            break;
        }
    }

    [Fact]
    public async Task FullQueueMarksCreatedWorkItemFailedInsteadOfLeavingItPending()
    {
        using var time = new TestTimeProvider();
        using var host = TestSupportApplicationHost.Create(
            timeProvider: time,
            queueCapacity: 1);
        var first = await host.WorkSubmission.SubmitAsync(new SupportRequest("first"));
        Assert.True(first.Enqueued);

        var rejected = await host.WorkSubmission.SubmitAsync(new SupportRequest("second"));

        Assert.False(rejected.Enqueued);
        Assert.Equal(SupportWorkItemStatus.Failed, rejected.WorkItem.Status);
        Assert.Equal("The support work queue is full.", rejected.WorkItem.Error);
    }

    [Fact]
    public async Task WorkStoreTransitionsPendingProcessingAndCompleted()
    {
        using var time = new TestTimeProvider();
        using var host = TestSupportApplicationHost.Create(timeProvider: time);
        var request = new SupportRequest(
            "queued",
            "SUP-1001",
            ConversationId: "queue-conversation");

        var pending = await host.WorkStore.CreateAsync(
            request,
            "queue-correlation",
            time.GetUtcNow());
        Assert.Equal(SupportWorkItemStatus.Pending, pending.Status);

        time.Advance(TimeSpan.FromMinutes(1));
        var processing = await host.WorkStore.TryMarkProcessingAsync(
            pending.WorkItemId,
            time.GetUtcNow());
        Assert.NotNull(processing);
        Assert.Equal(SupportWorkItemStatus.Processing, processing.Status);
        Assert.Equal(time.GetUtcNow(), processing.StartedAt);

        var response = new SupportResponse(
            "processed",
            request.TicketId,
            "queue-conversation",
            "queue-correlation");
        time.Advance(TimeSpan.FromMinutes(1));
        var completed = await host.WorkStore.MarkCompletedAsync(
            pending.WorkItemId,
            response,
            time.GetUtcNow());

        Assert.NotNull(completed);
        Assert.Equal(SupportWorkItemStatus.Completed, completed.Status);
        Assert.Equal(response, completed.Response);
        Assert.Equal(time.GetUtcNow(), completed.CompletedAt);
    }

    [Fact]
    public async Task WorkerConsumesWorkItemAndCompletesIt()
    {
        using var time = new TestTimeProvider();
        using var host = TestSupportApplicationHost.Create(timeProvider: time);
        using var worker = new SupportWorker(
            host.WorkChannel,
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SupportWorker>.Instance);

        var workItem = await host.WorkStore.CreateAsync(
            new SupportRequest(
                "queued",
                "SUP-1001",
                ConversationId: "worker-conversation")
            {
                CorrelationId = "queue-correlation"
            },
            "queue-correlation",
            time.GetUtcNow());
        Assert.True(host.WorkChannel.TryEnqueue(workItem.WorkItemId));

        await worker.StartAsync(CancellationToken.None);
        var completed = await WaitForStatusAsync(
            host.WorkStore,
            workItem.WorkItemId,
            SupportWorkItemStatus.Completed);

        Assert.NotNull(completed.Response);
        Assert.Equal("queue-correlation", completed.Response.CorrelationId);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WorkerMarksFailureAndContinuesWithLaterWorkItem()
    {
        using var time = new TestTimeProvider();
        using var host = TestSupportApplicationHost.Create(
            new FailingThenSuccessfulChatClient(),
            timeProvider: time);
        using var worker = new SupportWorker(
            host.WorkChannel,
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SupportWorker>.Instance);

        var first = await host.WorkStore.CreateAsync(
            new SupportRequest("first", ConversationId: "worker-first"),
            "worker-first",
            time.GetUtcNow());
        var second = await host.WorkStore.CreateAsync(
            new SupportRequest("second", ConversationId: "worker-second"),
            "worker-second",
            time.GetUtcNow());
        Assert.True(host.WorkChannel.TryEnqueue(first.WorkItemId));
        Assert.True(host.WorkChannel.TryEnqueue(second.WorkItemId));

        await worker.StartAsync(CancellationToken.None);
        var failed = await WaitForStatusAsync(
            host.WorkStore,
            first.WorkItemId,
            SupportWorkItemStatus.Failed);
        var completed = await WaitForStatusAsync(
            host.WorkStore,
            second.WorkItemId,
            SupportWorkItemStatus.Completed);

        Assert.Contains("intentional test failure", failed.Error);
        Assert.Equal("processed", completed.Response?.Answer);
        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task<SupportWorkItem> WaitForStatusAsync(
        InMemorySupportWorkStore store,
        string workItemId,
        SupportWorkItemStatus expectedStatus)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var workItem = await store.GetAsync(workItemId);
            if (workItem?.Status == expectedStatus)
            {
                return workItem;
            }

            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException(
            $"Work item '{workItemId}' did not reach {expectedStatus}.");
    }
}
