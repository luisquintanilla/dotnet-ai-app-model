namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class StreamingTests
{
    [Fact]
    public async Task ComposedStreamingHandlerPreservesUpdateOrder()
    {
        using var host = TestHandlerDependencies.Create();
        var updates = new List<string>();

        await foreach (var update in host.StreamHandler(
                           new SupportRequest("Please explain the next step.")))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                updates.Add(update.Text);
            }
        }

        Assert.True(updates.Count > 1);
        Assert.Equal(
            "Thanks for contacting support. We received your message and will follow up shortly.",
            string.Concat(updates));
    }

    [Fact]
    public async Task ComposedStreamingHandlerReturnsTheFinalToolBackedText()
    {
        using var host = TestHandlerDependencies.Create();
        var text = new List<string>();

        await foreach (var update in host.StreamHandler(
                           new SupportRequest("Can you check my ticket?", "SUP-1002")))
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
}
