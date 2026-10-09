using System.Net;
using System.Net.Http.Json;
using DotnetAiAppModel.SupportAssistant.Providers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task BufferedEndpointReturnsSupportResponse()
    {
        using var response = await _client.PostAsJsonAsync(
            "/support",
            new SupportRequest(
                "How do I update my email?",
                CustomerId: "CUS-42",
                ConversationId: "api-buffered"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SupportResponse>();
        Assert.NotNull(payload);
        Assert.StartsWith("Thanks for contacting support.", payload.Answer);
        Assert.Equal("api-buffered", payload.ConversationId);
        Assert.NotNull(payload.CorrelationId);
    }

    [Fact]
    public async Task StreamingEndpointWritesTextEventStreamInOrder()
    {
        using var response = await _client.PostAsJsonAsync(
            "/support/stream",
            new SupportRequest(
                "Please explain the next step.",
                ConversationId: "api-stream"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data: {\"text\":\"Thanks", body);
        Assert.EndsWith("event: complete\ndata: {}\n\n", body);
    }

    [Fact]
    public async Task QueueEndpointReturnsAcceptedAndStatusCompletes()
    {
        using var response = await _client.PostAsJsonAsync(
            "/support/queue",
            new SupportRequest(
                "Please check this ticket.",
                "SUP-1001",
                ConversationId: "api-queue"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<QueueAcceptedResponse>();
        Assert.NotNull(payload);
        Assert.Matches("^[0-9a-f]{32}$", payload.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(payload.WorkItemId));
        Assert.Equal($"/support/queue/{payload.WorkItemId}", payload.StatusUrl);

        var statusPayload = await WaitForStatusAsync(payload.StatusUrl!);
        Assert.Equal("completed", statusPayload.Status);
        Assert.NotNull(statusPayload.Response);
        Assert.Equal("SUP-1001", statusPayload.Response.TicketId);
    }

    [Fact]
    public async Task FullQueueReturns429AndRejectedWorkItemIsFailed()
    {
        using var app = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{SupportWorkOptions.SectionName}:QueueCapacity"] = "1"
                }));
            builder.ConfigureServices(services =>
            {
                var workerDescriptor = services.Single(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(SupportWorker));
                services.Remove(workerDescriptor);
            });
        });
        using var client = app.CreateClient();

        using var first = await client.PostAsJsonAsync(
            "/support/queue",
            new SupportRequest("first", ConversationId: "queue-first"));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        using var second = await client.PostAsJsonAsync(
            "/support/queue",
            new SupportRequest("second", ConversationId: "queue-second"));
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        var rejected = await second.Content.ReadFromJsonAsync<QueueAcceptedResponse>();
        Assert.NotNull(rejected);
        Assert.Equal("failed", rejected.Status);

        using var status = await client.GetAsync(rejected.StatusUrl);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var statusPayload =
            await status.Content.ReadFromJsonAsync<SupportWorkItemStatusResponse>();
        Assert.NotNull(statusPayload);
        Assert.Equal("failed", statusPayload.Status);
        Assert.Equal("The support work queue is full.", statusPayload.Error);
    }

    [Fact]
    public async Task InvalidRequestReturnsValidationProblem()
    {
        using var response = await _client.PostAsJsonAsync(
            "/support",
            new SupportRequest(string.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Message is required.", body);
    }

    [Fact]
    public void OpenAiProviderWithoutKeyFailsOptionsValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SupportModelOptions.SectionName}:Provider"] = "openai"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSupportApplication(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<SupportModelOptions>>().Value);

        Assert.Contains("OpenAiApiKey", exception.Message);
    }

    private async Task<SupportWorkItemStatusResponse> WaitForStatusAsync(
        string statusUrl)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var response = await _client.GetAsync(statusUrl);
            if (response.IsSuccessStatusCode)
            {
                var payload =
                    await response.Content.ReadFromJsonAsync<SupportWorkItemStatusResponse>();
                if (payload?.Status is "completed" or "failed")
                {
                    return payload;
                }
            }

            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException(
            $"Work item status did not complete for '{statusUrl}'.");
    }
}
