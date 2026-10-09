using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using DotnetAiAppModel.SupportAssistant.Providers;

namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"))
            .CreateClient();
    }

    [Fact]
    public async Task BufferedEndpointReturnsSupportResponse()
    {
        using var response = await _client.PostAsJsonAsync(
            "/support",
            new SupportRequest("How do I update my email?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SupportResponse>();
        Assert.NotNull(payload);
        Assert.StartsWith("Thanks for contacting support.", payload.Answer);
    }

    [Fact]
    public async Task StreamingEndpointWritesTextEventStreamInOrder()
    {
        using var response = await _client.PostAsJsonAsync(
            "/support/stream",
            new SupportRequest("Please explain the next step."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data: {\"text\":\"Thanks", body);
        Assert.EndsWith("event: complete\ndata: {}\n\n", body);
    }

    [Fact]
    public async Task QueueEndpointReturnsAcceptedCorrelationId()
    {
        using var response = await _client.PostAsJsonAsync(
            "/support/queue",
            new SupportRequest("Please check this ticket.", "SUP-1001"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<QueueAcceptedResponse>();
        Assert.NotNull(payload);
        Assert.Matches("^[0-9a-f]{32}$", payload.CorrelationId);
    }

    [Fact]
    public void OpenAiProviderWithoutKeyFailsOptionsValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SupportAssistantOptions.SectionName}:Provider"] = "openai"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSupportAssistant(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<SupportAssistantOptions>>().Value);

        Assert.Contains("OpenAiApiKey", exception.Message);
    }
}
