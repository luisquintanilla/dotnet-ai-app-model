using DotnetAiAppModel.SupportAssistant.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotnetAiAppModel.SupportAssistant.Tests;

internal sealed class TestHandlerDependencies : IDisposable
{
    private readonly ServiceProvider _services;

    private TestHandlerDependencies(ServiceProvider services)
    {
        _services = services;
        RequestHandler = services.GetRequiredService<SupportRequestHandler>();
        StreamHandler = services.GetRequiredService<SupportStreamHandler>();
    }

    public SupportRequestHandler RequestHandler { get; }

    public SupportStreamHandler StreamHandler { get; }

    public static TestHandlerDependencies Create(
        IChatClient? client = null,
        ITicketStore? ticketStore = null)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        var options = new SupportAssistantOptions
        {
            Provider = "scripted",
            Model = "scripted-test"
        };

        serviceCollection.AddSingleton<IChatClient>(serviceProvider =>
        {
            var innerClient = client ?? new ScriptedChatClient("scripted-test");
            return innerClient
                .AsBuilder()
                .UseFunctionInvocation(
                    serviceProvider.GetRequiredService<ILoggerFactory>(),
                    functionClient => functionClient.MaximumIterationsPerRequest = 4)
                .Build(serviceProvider);
        });
        serviceCollection.AddSingleton<ITicketStore>(
            ticketStore ?? new InMemoryTicketStore());
        serviceCollection.AddSingleton<AIFunction>(serviceProvider =>
            SupportTools.CreateTicketLookupFunction(
                serviceProvider.GetRequiredService<ITicketStore>()));
        serviceCollection.AddSingleton<IOptions<SupportAssistantOptions>>(
            Options.Create(options));
        serviceCollection.AddSupportHandlers();

        var services = serviceCollection.BuildServiceProvider();

        return new TestHandlerDependencies(services);
    }

    public void Dispose()
    {
        _services.Dispose();
    }
}
