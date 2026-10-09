using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DotnetAiAppModel.SupportAssistant.Providers;

namespace DotnetAiAppModel.SupportAssistant.Tests;

internal sealed class TestAssistantHost : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly IChatClient _chatClient;

    private TestAssistantHost(
        ServiceProvider services,
        IChatClient chatClient,
        SupportAssistant assistant)
    {
        _services = services;
        _chatClient = chatClient;
        Assistant = assistant;
    }

    public SupportAssistant Assistant { get; }

    public static TestAssistantHost Create(
        IChatClient? client = null,
        ITicketStore? ticketStore = null)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        var services = serviceCollection.BuildServiceProvider();

        var innerClient = client ?? new ScriptedChatClient("scripted-test");
        var pipelinedClient = innerClient
            .AsBuilder()
            .UseFunctionInvocation(
                services.GetRequiredService<ILoggerFactory>(),
                functionClient => functionClient.MaximumIterationsPerRequest = 4)
            .Build(services);

        var function = SupportTools.CreateTicketLookupFunction(ticketStore ?? new InMemoryTicketStore());
        var assistant = new SupportAssistant(
            pipelinedClient,
            function,
            Options.Create(new SupportAssistantOptions
            {
                Provider = "scripted",
                Model = "scripted-test"
            }),
            NullLogger<SupportAssistant>.Instance);

        return new TestAssistantHost(services, pipelinedClient, assistant);
    }

    public void Dispose()
    {
        _chatClient.Dispose();
        _services.Dispose();
    }
}
