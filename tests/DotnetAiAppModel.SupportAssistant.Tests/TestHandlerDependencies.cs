using DotnetAiAppModel.SupportAssistant.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotnetAiAppModel.SupportAssistant.Tests;

internal sealed class TestHandlerDependencies : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly IChatClient _chatClient;

    private TestHandlerDependencies(
        ServiceProvider services,
        IChatClient chatClient,
        AIFunction ticketLookup,
        SupportAssistantOptions options)
    {
        _services = services;
        _chatClient = chatClient;
        ChatClient = chatClient;
        TicketLookup = ticketLookup;
        Options = options;
        Logger = NullLogger.Instance;
    }

    public IChatClient ChatClient { get; }

    public AIFunction TicketLookup { get; }

    public SupportAssistantOptions Options { get; }

    public ILogger Logger { get; }

    public static TestHandlerDependencies Create(
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
        var options = new SupportAssistantOptions
        {
            Provider = "scripted",
            Model = "scripted-test"
        };

        return new TestHandlerDependencies(services, pipelinedClient, function, options);
    }

    public void Dispose()
    {
        _chatClient.Dispose();
        _services.Dispose();
    }
}
